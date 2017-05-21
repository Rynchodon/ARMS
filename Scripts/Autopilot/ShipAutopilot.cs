using System;
using System.Collections.Generic;
using System.Text;
using System.Xml.Serialization;
using Rynchodon.AntennaRelay;
using Rynchodon.Autopilot.Data;
using Rynchodon.Autopilot.Instruction;
using Rynchodon.Autopilot.Movement;
using Rynchodon.Autopilot.Navigator;
using Rynchodon.Autopilot.Pathfinding;
using Rynchodon.Settings;
using Rynchodon.Threading;
using Rynchodon.Utility;
using Sandbox.Common.ObjectBuilders;
using Sandbox.Game.Entities;
using Sandbox.ModAPI;
using VRage.Game.Components;
using VRage.Game.ModAPI;
using VRageMath;

namespace Rynchodon.Autopilot
{
	/// <summary>
	/// Contains components of the autopilot block.
	/// </summary>
	public class ShipControllerBlock
	{

		public readonly IMyCubeBlock CubeBlock;
		public readonly PseudoBlock Pseudo;
		public readonly RelayNode NetworkNode;
		public readonly AutopilotTerminal AutopilotTerminal;

		public MyShipController Controller { get { return (MyShipController)CubeBlock; } }
		public IMyTerminalBlock Terminal { get { return (IMyTerminalBlock)CubeBlock; } }
		public RelayStorage NetworkStorage { get { return NetworkNode.Storage; } }
		public IMyCubeGrid CubeGrid { get { return Controller.CubeGrid; } }
		public MyPhysicsComponentBase Physics { get { return Controller.CubeGrid.Physics; } }

		public bool AutopilotControl
		{
			get { return AutopilotTerminal.AutopilotControlSwitch; }
			set { AutopilotTerminal.AutopilotControlSwitch = value; }
		}

		public ShipControllerBlock(IMyCubeBlock block, Action<Message> messageHandler)
		{
			CubeBlock = block;
			Pseudo = new PseudoBlock(block);
			NetworkNode = new RelayNode(block) { MessageHandler = messageHandler };
			AutopilotTerminal = new AutopilotTerminal(block);
		}

	}

	/// <summary>
	/// Core class for all Autopilot functionality.
	/// </summary>
	public class ShipAutopilot
	{

		[Serializable]
		public class Builder_Autopilot
		{
			[XmlAttribute]
			public long AutopilotBlock;
			public string Commands;
			public int CurrentCommand;

			public Vector3D EngagerOriginalPosition = Vector3D.PositiveInfinity;
			public long EngagerOriginalEntity;
		}

		public const uint UpdateFrequency = 3u;

		private const string subtype_autopilotBlock = "Autopilot-Block";

		public static ThreadManager AutopilotThread = new ThreadManager(threadName: "Autopilot");
		private static HashSet<IMyCubeGrid> GridBeingControlled = new HashSet<IMyCubeGrid>();

		/// <summary>
		/// Determines if the given block is an autopilot block. Does not check ServerSettings.
		/// </summary>
		/// <param name="block">The block to check</param>
		/// <returns>True iff the given block is an autopilot block.</returns>
		public static bool IsAutopilotBlock(VRage.Game.ModAPI.Ingame.IMyCubeBlock block)
		{
			if (block is MyCockpit)
				return block.BlockDefinition.SubtypeId.Contains(subtype_autopilotBlock);

			return block is MyRemoteControl;
		}

		/// <summary>
		/// Determines if the given grid has an autopilot block. Does check ServerSettings.
		/// </summary>
		/// <param name="grid">The grid to search</param>
		/// <returns>True iff the given grid contains one or more autopilot blocks.</returns>
		public static bool HasAutopilotBlock(IMyCubeGrid grid)
		{
			if (!ServerSettings.GetSetting<bool>(ServerSettings.SettingName.bAllowAutopilot))
				return false;

			var cache = CubeGridCache.GetFor(grid);
			foreach (IMyCubeBlock cockpit in cache.BlocksOfType(typeof(MyObjectBuilder_Cockpit)))
				if (IsAutopilotBlock(cockpit))
					return true;

			if (ServerSettings.GetSetting<bool>(ServerSettings.SettingName.bUseRemoteControl))
			{
				foreach (IMyCubeBlock remote in cache.BlocksOfType(typeof(MyObjectBuilder_RemoteControl)))
					if (IsAutopilotBlock(remote))
						return true;
			}

			return false;
		}

		public enum State : byte { Disabled, Player, Enabled, Halted, Closed }

		public readonly ShipControllerBlock m_block;
		private readonly FastResourceLock lock_execution = new FastResourceLock();
		private Pathfinder m_pathfinder;
		private AutopilotCommands m_commands;

		private IMyCubeGrid m_controlledGrid;
		private State value_state = State.Disabled;
		private TimeSpan m_previousInstructions = TimeSpan.MinValue;
		private TimeSpan m_endOfHalt;

		private ulong m_nextCustomInfo;

		private AutopilotActionList m_autopilotActions;

		private Logable Log { get { return new Logable(m_block?.CubeBlock); } }
		private Mover m_mover { get { return m_pathfinder.Mover; } }
		private AllNavigationSettings m_navSet { get { return m_mover.NavSet; } }

		private State m_state
		{
			get { return value_state; }
			set
			{
				if (value_state == value || value_state == State.Closed)
					return;
				Log.DebugLog("state change from " + value_state + " to " + value, Logger.severity.DEBUG);
				value_state = value;
				m_pathfinder.Halt();

				switch (value_state)
				{
					case State.Enabled:
					case State.Player:
						m_mover.MoveAndRotateStop(false);
						return;

					case State.Disabled:
						m_navSet.OnStartOfCommands(); // here so that navigators are disposed of
						m_autopilotActions = null;
						m_mover.MoveAndRotateStop(false);
						return;

					case State.Halted:
						m_endOfHalt = Globals.ElapsedTime.Add(new TimeSpan(0, 5, 0));
						m_mover.MoveAndRotateStop(true);
						return;

					case State.Closed:
						ReleaseControlledGrid();
						m_pathfinder = null;
						m_commands = null;
						return;

					default:
						Log.AlwaysLog("State not implemented: " + value, Logger.severity.FATAL);
						return;
				}
			}
		}



		/// <summary>
		/// Creates an Autopilot for the given ship controller.
		/// </summary>
		/// <param name="block">The ship controller to use</param>
		public ShipAutopilot(IMyCubeBlock block)
		{
			this.m_block = new ShipControllerBlock(block, HandleMessage);
			this.m_pathfinder = new Pathfinder(m_block);
			this.m_commands = AutopilotCommands.GetOrCreate(m_block.Terminal);
			this.m_block.CubeBlock.OnClosing += CubeBlock_OnClosing;

			int start = block.DisplayNameText.IndexOf('[') + 1, end = block.DisplayNameText.IndexOf(']');
			if (start > 0 && end > start)
			{
				m_block.AutopilotTerminal.AutopilotCommandsText = new StringBuilder(block.DisplayNameText.Substring(start, end - start).Trim());
				int lengthBefore = start - 1;
				string nameBefore = lengthBefore > 0 ? m_block.Terminal.DisplayNameText.Substring(0, lengthBefore) : string.Empty;
				end++;
				int lengthAfter = m_block.Terminal.DisplayNameText.Length - end;
				string nameAfter = lengthAfter > 0 ? m_block.Terminal.DisplayNameText.Substring(end, lengthAfter) : string.Empty;
				m_block.Terminal.CustomName = (nameBefore + nameAfter).Trim();
			}

			Log.DebugLog("Created autopilot for: " + block.DisplayNameText);
			Registrar.Add(block, this);
		}

		private void CubeBlock_OnClosing(VRage.ModAPI.IMyEntity obj)
		{
			m_block.CubeBlock.OnClosing -= CubeBlock_OnClosing;
			m_state = State.Closed;
		}

		public void Update()
		{
			AutopilotThread.EnqueueAction(UpdateThread);
		}

		/// <summary>
		/// Run the autopilot
		/// </summary>
		private void UpdateThread()
		{
			if (!lock_execution.TryAcquireExclusive())
				return;
			try
			{
				if (Globals.UpdateCount > m_nextCustomInfo)
				{
					m_nextCustomInfo = Globals.UpdateCount + 10ul;
					UpdateCustomInfo();
				}

				switch (m_state)
				{
					case State.Disabled:
						if (CheckControl())
							m_state = State.Enabled;
						return;
					case State.Enabled:
						if (CheckControl())
							break;
						m_state = State.Disabled;
						return;
					case State.Player:
						// wait for player to give back control, do not reset
						if (MyAPIGateway.Players.GetPlayerControllingEntity(m_controlledGrid) == null)
							m_state = State.Enabled;
						return;
					case State.Halted:
						if (!m_block.AutopilotControl || Globals.ElapsedTime > m_endOfHalt)
							m_state = State.Disabled;
						return;
					case State.Closed:
						return;
					default:
						throw new Exception("Case not implemented: " + m_state);
				}

				if (MyAPIGateway.Players.GetPlayerControllingEntity(m_controlledGrid) != null)
				{
					m_state = State.Player;
					return;
				}

				EnemyFinder ef = m_navSet.Settings_Current.EnemyFinder;
				if (ef != null)
					ef.Update();

				if (m_navSet.Settings_Current.WaitUntil > Globals.ElapsedTime)
					return;

				if (MoveAndRotate())
					return;

				if (m_autopilotActions != null)
					while (true)
					{
						if (!m_autopilotActions.MoveNext())
						{
							Log.DebugLog("finder: " + m_navSet.Settings_Current.EnemyFinder);
							m_autopilotActions = null;
							return;
						}
						m_autopilotActions.Current.Invoke(m_pathfinder);
						if (m_navSet.Settings_Current.WaitUntil > Globals.ElapsedTime)
						{
							Log.DebugLog("now waiting until " + m_navSet.Settings_Current.WaitUntil);
							return;
						}
						if (m_navSet.Settings_Current.NavigatorMover != null)
						{
							Log.DebugLog("now have a navigator mover: " + m_navSet.Settings_Current.NavigatorMover);
							return;
						}
					}

				if (RotateOnly())
					return;

				TimeSpan nextInstructions = m_previousInstructions + TimeSpan.FromSeconds(m_navSet.Settings_Current.Complaint != InfoString.StringId.None || ef != null ? 60d : 1d);
				if (nextInstructions > Globals.ElapsedTime)
				{
					Log.DebugLog("Delaying instructions until " + nextInstructions, Logger.severity.INFO);
					m_navSet.Settings_Task_NavWay.WaitUntil = nextInstructions;
					return;
				}
				Log.DebugLog("enqueing instructions", Logger.severity.DEBUG);
				m_previousInstructions = Globals.ElapsedTime;

				m_autopilotActions = m_commands.GetActions();

				if (m_autopilotActions == null || m_autopilotActions.IsEmpty)
					ReleaseControlledGrid();
				m_navSet.OnStartOfCommands();
				m_mover.MoveAndRotateStop(false);

				if (m_commands.HasSyntaxErrors)
					m_navSet.Settings_Task_NavWay.WaitUntil = Globals.ElapsedTime + TimeSpan.FromMinutes(1d);
			}
			catch (Exception ex)
			{
				Log.AlwaysLog("Commands: " + m_commands.Commands, Logger.severity.DEBUG);
				Log.AlwaysLog("Exception: " + ex, Logger.severity.ERROR);
				m_state = State.Halted;
			}
			finally
			{ lock_execution.ReleaseExclusive(); }
		}

		private bool MoveAndRotate()
		{
			INavigatorMover navM = m_navSet.Settings_Current.NavigatorMover;
			if (navM != null)
			{
				Profiler.Profile(navM.Move);

				INavigatorRotator navR = m_navSet.Settings_Current.NavigatorRotator; // fetched here because mover might remove it
				if (navR != null)
					Profiler.Profile(navR.Rotate);
				else
				{
					navR = m_navSet.Settings_Current.NavigatorMover as INavigatorRotator; // fetch again in case it was removed
					if (navR != null)
						Profiler.Profile(navR.Rotate);
				}

				Profiler.Profile(m_mover.MoveAndRotate);
				return true;
			}
			return false;
		}

		/// <summary>
		/// run the rotator by itself until direction is matched
		/// </summary>
		private bool RotateOnly()
		{
			INavigatorRotator navR = m_navSet.Settings_Current.NavigatorRotator;
			if (navR != null)
			{
				// direction might have been matched by another rotator, so run it first

				Profiler.Profile(navR.Rotate);
				Profiler.Profile(m_mover.MoveAndRotate);

				if (m_navSet.DirectionMatched())
				{
					m_mover.StopRotate();
					m_mover.MoveAndRotate();
				}
				else
					return true;
			}
			return false;
		}

		#region Control

		/// <summary>
		/// Checks if the Autopilot has permission to run.
		/// </summary>
		/// <returns>True iff the Autopilot has permission to run.</returns>
		private bool CheckControl()
		{
			// cache current grid in case it changes
			IMyCubeGrid myGrid = m_block.CubeGrid;

			if (m_controlledGrid != null)
			{
				if (m_controlledGrid != myGrid)
				{
					// a (de)merge happened
					ReleaseControlledGrid();
				}
				else if (CanControlBlockGrid(m_controlledGrid))
				{
					// OK to continue controlling
					return true;
				}
				else
				{
					// cannot continue to control
					ReleaseControlledGrid();
					return false;
				}
			}

			if (!CanControlBlockGrid(myGrid) || !GridBeingControlled.Add(myGrid))
				return false;

			m_controlledGrid = myGrid;
			// toggle thrusters off and on to make sure thrusters are actually online
			MyAPIGateway.Utilities.InvokeOnGameThread(() => {
				if (this.m_block.Controller.ControlThrusters)
					this.m_block.CubeBlock.ApplyAction("ControlThrusters");
				this.m_block.CubeBlock.ApplyAction("ControlThrusters");
			});
			return true;
		}

		/// <summary>
		/// Checks if block and grid can be controlled.
		/// </summary>
		/// <returns>True iff block and grid can be controlled.</returns>
		private bool CanControlBlockGrid(IMyCubeGrid grid)
		{
			// is grid ready
			if (grid.IsStatic)
				return false;

			// is block ready
			if (!m_block.Controller.IsWorking
				|| !m_block.AutopilotControl)
				return false;

			MyCubeGrid mcg = grid as MyCubeGrid;
			if (mcg.HasMainCockpit() && !m_block.Controller.IsMainCockpit)
				return false;

			return true;
		}

		/// <summary>
		/// Release the grid so another Autopilot can control it.
		/// </summary>
		private void ReleaseControlledGrid()
		{
			if (m_controlledGrid == null)
				return;

			if (!GridBeingControlled.Remove(m_controlledGrid))
			{
				Log.AlwaysLog("Failed to remove " + m_controlledGrid.DisplayName + " from GridBeingControlled", Logger.severity.FATAL);
				throw new InvalidOperationException("Failed to remove " + m_controlledGrid.DisplayName + " from GridBeingControlled");
			}

			//Log.DebugLog("Released control of " + ControlledGrid.DisplayName, "ReleaseControlledGrid()", Logger.severity.DEBUG);
			m_controlledGrid = null;
		}

		#endregion

		#region Custom Info

		private void UpdateCustomInfo()
		{
			AutopilotTerminal ApTerm = m_block.AutopilotTerminal;
			AllNavigationSettings.SettingsLevel Settings_Current = m_navSet.Settings_Current;

			ApTerm.m_autopilotStatus = m_state;
			if (m_state == State.Halted)
				return;

			AutopilotTerminal.AutopilotFlags flags = AutopilotTerminal.AutopilotFlags.None;
			if (m_controlledGrid != null)
				flags |= AutopilotTerminal.AutopilotFlags.HasControl;

			if (m_pathfinder.ReportedObstruction != null)
			{
				ApTerm.m_blockedBy = m_pathfinder.ReportedObstruction.EntityId;
				if (m_pathfinder.RotateCheck.ObstructingEntity != null)
					flags |= AutopilotTerminal.AutopilotFlags.RotationBlocked;
			}
			else if (m_pathfinder.RotateCheck.ObstructingEntity != null)
			{
				flags |= AutopilotTerminal.AutopilotFlags.RotationBlocked;
				ApTerm.m_blockedBy = m_pathfinder.RotateCheck.ObstructingEntity.EntityId;
			}

			EnemyFinder ef = Settings_Current.EnemyFinder;
			if (ef != null && ef.Grid == null)
			{
				flags |= AutopilotTerminal.AutopilotFlags.EnemyFinderIssue;
				ApTerm.m_reasonCannotTarget = ef.m_reason;
				if (ef.m_bestGrid != null)
					ApTerm.m_enemyFinderBestTarget = ef.m_bestGrid.Entity.EntityId;
			}
			INavigatorMover navM = Settings_Current.NavigatorMover;
			if (navM != null)
			{
				flags |= AutopilotTerminal.AutopilotFlags.HasNavigatorMover;
				ApTerm.m_prevNavMover = navM.GetType().Name;
				AutopilotTerminal.Static.prevNavMoverInfo.Update((IMyTerminalBlock)m_block.CubeBlock, navM.AppendCustomInfo);
			}
			INavigatorRotator navR = Settings_Current.NavigatorRotator;
			if (navR != null && navR != navM)
			{
				flags |= AutopilotTerminal.AutopilotFlags.HasNavigatorRotator;
				ApTerm.m_prevNavRotator = navR.GetType().Name;
				AutopilotTerminal.Static.prevNavRotatorInfo.Update((IMyTerminalBlock)m_block.CubeBlock, navR.AppendCustomInfo);
			}
			ApTerm.m_autopilotFlags = flags;
			ApTerm.m_pathfinderState = m_pathfinder.CurrentState;
			ApTerm.SetWaitUntil(Settings_Current.WaitUntil);
			ApTerm.SetDistance(Settings_Current.Distance, Settings_Current.DistanceAngle);
			ApTerm.m_welderUnfinishedBlocks = m_navSet.WelderUnfinishedBlocks;
			ApTerm.m_complaint = Settings_Current.Complaint;
			ApTerm.m_jumpComplaint = m_pathfinder.JumpComplaint;
		}

		#endregion Custom Info

		private void HandleMessage(Message msg)
		{
			using (lock_execution.AcquireExclusiveUsing())
			{
				m_autopilotActions = m_commands.GetActions(msg.Content);
				m_navSet.OnStartOfCommands();
				m_mover.MoveAndRotateStop(false);
				m_mover.SetControl(true);
			}
		}
		
		public Builder_Autopilot GetBuilder()
		{
			if (!m_block.AutopilotControl)
				return null;

			Builder_Autopilot result = new Builder_Autopilot() { AutopilotBlock = m_block.CubeBlock.EntityId };

			if (m_autopilotActions == null || m_autopilotActions.CurrentIndex <= 0 || m_autopilotActions.Current == null)
				return null;

			result.CurrentCommand = m_autopilotActions.CurrentIndex;
			Log.DebugLog("current command: " + result.CurrentCommand);

			result.Commands = m_commands.Commands;
			Log.DebugLog("commands: " + result.Commands);

			EnemyFinder finder = m_navSet.Settings_Current.EnemyFinder;
			if (finder != null)
			{
				result.EngagerOriginalEntity = finder.m_originalDestEntity == null ? 0L : finder.m_originalDestEntity.EntityId;
				result.EngagerOriginalPosition = finder.m_originalPosition;
				Log.DebugLog("added EngagerOriginalEntity: " + result.EngagerOriginalEntity + ", and EngagerOriginalPosition: " + result.EngagerOriginalPosition);
			}

			return result;
		}

		public void ResumeFromSave(Builder_Autopilot builder)
		{
			using (lock_execution.AcquireExclusiveUsing())
			{
				m_navSet.OnStartOfCommands();
				Log.DebugLog("resume: " + builder.Commands + ", current: " + builder.CurrentCommand, Logger.severity.DEBUG);
				m_autopilotActions = m_commands.GetActions(builder.Commands);

				while (m_autopilotActions.CurrentIndex < builder.CurrentCommand - 1 && m_autopilotActions.MoveNext())
				{
					m_autopilotActions.Current.Invoke(m_pathfinder);
					Log.DebugLog("fast forward: " + m_autopilotActions.CurrentIndex);

					// clear navigators' levels
					for (AllNavigationSettings.SettingsLevelName levelName = AllNavigationSettings.SettingsLevelName.NavRot; levelName < AllNavigationSettings.SettingsLevelName.NavWay; levelName++)
					{
						AllNavigationSettings.SettingsLevel settingsAtLevel = m_navSet.GetSettingsLevel(levelName);
						if (settingsAtLevel.NavigatorMover != null || settingsAtLevel.NavigatorRotator != null)
						{
							Log.DebugLog("clear " + levelName);
							m_navSet.OnTaskComplete(levelName);
							break;
						}
					}
				}
				if (m_autopilotActions.MoveNext())
					m_autopilotActions.Current.Invoke(m_pathfinder);

				// clear wait
				m_navSet.OnTaskComplete(AllNavigationSettings.SettingsLevelName.NavWay);

				EnemyFinder finder = m_navSet.Settings_Current.EnemyFinder;
				if (finder != null)
				{
					if (builder.EngagerOriginalEntity != 0L)
					{
						if (!MyAPIGateway.Entities.TryGetEntityById(builder.EngagerOriginalEntity, out finder.m_originalDestEntity))
						{
							Log.AlwaysLog("Failed to restore original destination enitity for enemy finder: " + builder.EngagerOriginalEntity, Logger.severity.WARNING);
							finder.m_originalDestEntity = null;
						}
						else
							Log.DebugLog("Restored original destination enitity for enemy finder: " + finder.m_originalDestEntity.getBestName());
					}
					if (builder.EngagerOriginalPosition.IsValid())
					{
						finder.m_originalPosition = builder.EngagerOriginalPosition;
						Log.DebugLog("Restored original position for enemy finder: " + builder.EngagerOriginalPosition);
					}
				}
			}
		}

	}
}
