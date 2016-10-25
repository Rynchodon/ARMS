using System;
using System.Collections.Generic;
using System.Text;
using System.Xml.Serialization;
using Rynchodon.AntennaRelay;
using Rynchodon.Autopilot.Data;
using Rynchodon.Autopilot.Instruction;
using Rynchodon.Autopilot.Movement;
using Rynchodon.Autopilot.Navigator;
using Rynchodon.Settings;
using Rynchodon.Threading;
using Rynchodon.Update;
using Rynchodon.Utility;
using Sandbox.Common.ObjectBuilders;
using Sandbox.Game.Entities;
using Sandbox.ModAPI;
using VRage;
using VRage.Game;
using VRage.Game.Components;
using VRage.Game.ModAPI;
using VRage.Game.ObjectBuilders.Definitions;
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

		private readonly Logger m_logger;

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
			m_logger = new Logger(block);
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

		private static ThreadManager AutopilotThread = new ThreadManager(threadName: "Autopilot");
		private static HashSet<IMyCubeGrid> GridBeingControlled = new HashSet<IMyCubeGrid>();

		static ShipAutopilot()
		{
			MyAPIGateway.Entities.OnCloseAll += Entities_OnCloseAll;
		}

		private static void Entities_OnCloseAll()
		{
			MyAPIGateway.Entities.OnCloseAll -= Entities_OnCloseAll;
			AutopilotThread = null;
			GridBeingControlled = null;
		}

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
			var cockpits = cache.GetBlocksOfType(typeof(MyObjectBuilder_Cockpit));
			if (cockpits != null)
				foreach (IMyCubeBlock cockpit in cockpits)
					if (IsAutopilotBlock(cockpit))
						return true;

			if (ServerSettings.GetSetting<bool>(ServerSettings.SettingName.bUseRemoteControl))
			{
				var remotes = cache.GetBlocksOfType(typeof(MyObjectBuilder_RemoteControl));
				if (remotes != null)
					foreach (IMyCubeBlock remote in remotes)
						if (IsAutopilotBlock(remote))
							return true;
			}

			return false;
		}

		public enum State : byte { Disabled, Player, Enabled, Halted, Closed }

		public readonly ShipControllerBlock m_block;
		private readonly Logger m_logger;
		private readonly FastResourceLock lock_execution = new FastResourceLock();
		private Mover m_mover;
		private AutopilotCommands m_commands;

		private IMyCubeGrid m_controlledGrid;
		private State value_state = State.Disabled;
		private TimeSpan m_previousInstructions = TimeSpan.MinValue;
		private TimeSpan m_endOfHalt;

		private ulong m_nextCustomInfo;

		private AutopilotActionList m_autopilotActions;

		private AllNavigationSettings m_navSet { get { return m_mover.NavSet; } }

		private State m_state
		{
			get { return value_state; }
			set
			{
				if (value_state == value || value_state == State.Closed)
					return;
				m_logger.debugLog("state change from " + value_state + " to " + value, Logger.severity.DEBUG);
				value_state = value;

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
						if (GridBeingControlled != null)
							ReleaseControlledGrid();
						m_mover = null;
						m_commands = null;
						return;

					default:
						m_logger.alwaysLog("State not implemented: " + value, Logger.severity.FATAL);
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
			this.m_logger = new Logger(block);
			this.m_mover = new Mover(m_block);
			this.m_commands = AutopilotCommands.GetOrCreate(m_block.Terminal);

			this.m_block.CubeBlock.OnClosing += CubeBlock_OnClosing;

			((MyCubeBlock)block).ResourceSink.SetRequiredInputFuncByType(new MyDefinitionId(typeof(MyObjectBuilder_GasProperties), "Electricity"), PowerRequired);

			m_logger.debugLog("Created autopilot for: " + block.DisplayNameText);

			Registrar.Add(block, this);
		}

		private void CubeBlock_OnClosing(VRage.ModAPI.IMyEntity obj)
		{
			m_block.CubeBlock.OnClosing -= CubeBlock_OnClosing;
			m_state = State.Closed;
		}

		public void Update()
		{
			if (lock_execution.TryAcquireExclusive())
				AutopilotThread.EnqueueAction(UpdateThread);
		}

		/// <summary>
		/// Run the autopilot
		/// </summary>
		private void UpdateThread()
		{
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
							m_logger.debugLog("finder: " + m_navSet.Settings_Current.EnemyFinder);
							m_autopilotActions = null;
							return;
						}
						m_autopilotActions.Current.Invoke(m_mover);
						if (m_navSet.Settings_Current.WaitUntil > Globals.ElapsedTime)
						{
							m_logger.debugLog("now waiting until " + m_navSet.Settings_Current.WaitUntil);
							return;
						}
						if (m_navSet.Settings_Current.NavigatorMover != null)
						{
							m_logger.debugLog("now have a navigator mover: " + m_navSet.Settings_Current.NavigatorMover);
							return;
						}
					}

				if (RotateOnly())
					return;

				TimeSpan nextInstructions = m_previousInstructions + TimeSpan.FromSeconds(ef != null ? 10d : 1d);
				m_logger.debugLog("ef: " + ef + ", next: " + nextInstructions);
				if (nextInstructions > Globals.ElapsedTime)
				{
					m_logger.debugLog("Delaying instructions", Logger.severity.INFO);
					m_navSet.Settings_Task_NavWay.WaitUntil = nextInstructions;
					return;
				}
				m_logger.debugLog("enqueing instructions", Logger.severity.DEBUG);
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
				m_logger.alwaysLog("Commands: " + m_commands.Commands, Logger.severity.DEBUG);
				m_logger.alwaysLog("Exception: " + ex, Logger.severity.ERROR);
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
				m_logger.alwaysLog("Failed to remove " + m_controlledGrid.DisplayName + " from GridBeingControlled", Logger.severity.FATAL);
				throw new InvalidOperationException("Failed to remove " + m_controlledGrid.DisplayName + " from GridBeingControlled");
			}

			//myLogger.debugLog("Released control of " + ControlledGrid.DisplayName, "ReleaseControlledGrid()", Logger.severity.DEBUG);
			m_controlledGrid = null;
		}

		#endregion

		#region Custom Info

		private void UpdateCustomInfo()
		{
			AutopilotTerminal ApTerm = m_block.AutopilotTerminal;
			AllNavigationSettings.SettingsLevel Settings_Current = m_navSet.Settings_Current;

			ApTerm.m_autopilotStatus.Value = m_state;
			if (m_state == State.Halted)
				return;

			AutopilotTerminal.AutopilotFlags flags = AutopilotTerminal.AutopilotFlags.None;
			if (m_controlledGrid != null)
				flags |= AutopilotTerminal.AutopilotFlags.HasControl;
			if (m_mover.Pathfinder != null)
			{
				if (!m_mover.Pathfinder.ReportCanMove)
				{
					flags |= AutopilotTerminal.AutopilotFlags.MovementBlocked;
					ApTerm.m_blockedBy.Value = m_mover.Pathfinder.MoveObstruction != null ? m_mover.Pathfinder.MoveObstruction.EntityId : 0L;
					if (!m_mover.Pathfinder.ReportCanRotate)
						flags |= AutopilotTerminal.AutopilotFlags.RotationBlocked;
				}
				else if (!m_mover.Pathfinder.ReportCanRotate)
				{
					flags |= AutopilotTerminal.AutopilotFlags.RotationBlocked;
					ApTerm.m_blockedBy.Value = m_mover.Pathfinder.RotateObstruction != null ? m_mover.Pathfinder.RotateObstruction.EntityId : 0L;
				}
			}
			EnemyFinder ef = Settings_Current.EnemyFinder;
			if (ef != null && ef.Grid == null)
			{
				flags |= AutopilotTerminal.AutopilotFlags.EnemyFinderIssue;
				ApTerm.m_reasonCannotTarget.Value = ef.m_reason;
				if (ef.m_bestGrid != null)
					ApTerm.m_enemyFinderBestTarget.Value = ef.m_bestGrid.Entity.EntityId;
			}
			INavigatorMover navM = Settings_Current.NavigatorMover;
			if (navM != null)
			{
				flags |= AutopilotTerminal.AutopilotFlags.HasNavigatorMover;
				ApTerm.m_prevNavMover.Value = navM.GetType().Name;
				ApTerm.m_prevNavMoverInfo.Update(navM.AppendCustomInfo);
			}
			INavigatorRotator navR = Settings_Current.NavigatorRotator;
			if (navR != null && navR != navM)
			{
				flags |= AutopilotTerminal.AutopilotFlags.HasNavigatorRotator;
				ApTerm.m_prevNavRotator.Value = navR.GetType().Name;
				ApTerm.m_prevNavRotatorInfo.Update(navR.AppendCustomInfo);
			}
			ApTerm.m_autopilotFlags.Value = flags;

			ApTerm.SetWaitUntil(Settings_Current.WaitUntil);
			ApTerm.SetDistance(Settings_Current.Distance, Settings_Current.DistanceAngle);
			ApTerm.m_welderUnfinishedBlocks.Value = m_navSet.WelderUnfinishedBlocks;
			ApTerm.m_complaint.Value = Settings_Current.Complaint;
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

		private float PowerRequired()
		{
			return m_state == State.Enabled && m_navSet.Settings_Current.WaitUntil < Globals.ElapsedTime ? 0.1f : 0.01f;
		}

		public Builder_Autopilot GetBuilder()
		{
			if (!m_block.AutopilotControl)
				return null;

			Builder_Autopilot result = new Builder_Autopilot() { AutopilotBlock = m_block.CubeBlock.EntityId };

			if (m_autopilotActions == null || m_autopilotActions.CurrentIndex <= 0 || m_autopilotActions.Current == null)
				return null;

			result.CurrentCommand = m_autopilotActions.CurrentIndex;
			m_logger.debugLog("current command: " + result.CurrentCommand);

			result.Commands = m_commands.Commands;
			m_logger.debugLog("commands: " + result.Commands);

			EnemyFinder finder = m_navSet.Settings_Current.EnemyFinder;
			if (finder != null)
			{
				result.EngagerOriginalEntity = finder.m_originalDestEntity == null ? 0L : finder.m_originalDestEntity.EntityId;
				result.EngagerOriginalPosition = finder.m_originalPosition;
				m_logger.debugLog("added EngagerOriginalEntity: " + result.EngagerOriginalEntity + ", and EngagerOriginalPosition: " + result.EngagerOriginalPosition);
			}

			return result;
		}

		public void ResumeFromSave(Builder_Autopilot builder)
		{
			using (lock_execution.AcquireExclusiveUsing())
			{
				m_navSet.OnStartOfCommands();
				m_logger.debugLog("resume: " + builder.Commands, Logger.severity.DEBUG);
				m_autopilotActions = m_commands.GetActions(builder.Commands);

				while (m_autopilotActions.CurrentIndex < builder.CurrentCommand && m_autopilotActions.MoveNext())
				{
					m_logger.debugLog("fast forward: " + m_autopilotActions.Current);
					m_autopilotActions.Current.Invoke(m_mover);

					// clear navigators' levels
					for (AllNavigationSettings.SettingsLevelName levelName = AllNavigationSettings.SettingsLevelName.NavRot; levelName < AllNavigationSettings.SettingsLevelName.NavWay; levelName++)
					{
						AllNavigationSettings.SettingsLevel settingsAtLevel = m_navSet.GetSettingsLevel(levelName);
						if (settingsAtLevel.NavigatorMover != null || settingsAtLevel.NavigatorRotator != null)
						{
							m_logger.debugLog("clear " + levelName);
							m_navSet.OnTaskComplete(levelName);
						}
					}
				}
				m_autopilotActions.MoveNext();

				// clear wait
				m_navSet.OnTaskComplete(AllNavigationSettings.SettingsLevelName.NavWay);

				EnemyFinder finder = m_navSet.Settings_Current.EnemyFinder;
				if (finder != null)
				{
					if (builder.EngagerOriginalEntity != 0L)
					{
						if (!MyAPIGateway.Entities.TryGetEntityById(builder.EngagerOriginalEntity, out finder.m_originalDestEntity))
						{
							m_logger.alwaysLog("Failed to restore original destination enitity for enemy finder: " + builder.EngagerOriginalEntity, Logger.severity.WARNING);
							finder.m_originalDestEntity = null;
						}
						else
							m_logger.debugLog("Restored original destination enitity for enemy finder: " + finder.m_originalDestEntity.getBestName());
					}
					if (builder.EngagerOriginalPosition.IsValid())
					{
						finder.m_originalPosition = builder.EngagerOriginalPosition;
						m_logger.debugLog("Restored original position for enemy finder: " + builder.EngagerOriginalPosition);
					}
				}
			}
		}

	}
}
