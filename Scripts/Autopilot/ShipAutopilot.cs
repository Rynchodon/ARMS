using System;
using System.Collections.Generic;
using System.Text;
using System.Xml.Serialization;
using Rynchodon.AntennaRelay;
using Rynchodon.Autopilot.Data;
using Rynchodon.Autopilot.Instruction;
using Rynchodon.Autopilot.Navigator;
using Rynchodon.Settings;
using Rynchodon.Threading;
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

	public class ShipControllerBlock
	{

		public readonly MyShipController Controller;
		public readonly IMyCubeBlock CubeBlock;
		public readonly IMyTerminalBlock Terminal;
		public readonly PseudoBlock Pseudo;
		public readonly NetworkClient NetClient;

		private readonly Logger m_logger;

		public IMyCubeGrid CubeGrid { get { return Controller.CubeGrid; } }
		public MyPhysicsComponentBase Physics { get { return Controller.CubeGrid.Physics; } }

		public ShipControllerBlock(IMyCubeBlock block, NetworkClient netClient)
		{
			m_logger = new Logger(GetType().Name, block);
			Controller = block as MyShipController;
			CubeBlock = block;
			Terminal = block as IMyTerminalBlock;
			Pseudo = new PseudoBlock(block);
			NetClient = netClient;
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
		public const ushort ModId_CustomInfo = 54311;

		private const string subtype_autopilotBlock = "Autopilot-Block";

		private static readonly TimeSpan MinTimeInstructions = new TimeSpan(0, 0, 10);
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
		public static bool IsAutopilotBlock(IMyCubeBlock block)
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

		private enum State : byte { Disabled, Player, Enabled, Halted, Closed }

		public readonly ShipControllerBlock m_block;

		private readonly Logger m_logger;
		private Interpreter m_interpreter;

		private readonly FastResourceLock lock_execution = new FastResourceLock();

		private IMyCubeGrid m_controlledGrid;
		private State value_state = State.Disabled;
		private TimeSpan m_nextAllowedInstructions = TimeSpan.MinValue;
		private TimeSpan m_endOfHalt;

		private StringBuilder m_customInfo_build = new StringBuilder(), m_customInfo_send = new StringBuilder();
		private List<byte> m_customInfo_message = new List<byte>();
		private ulong m_nextCustomInfo;
		private Message m_message;

		private AllNavigationSettings m_navSet { get { return m_interpreter.NavSet; } }

		public Builder_Autopilot Resume;

		public StringBuilder CustomInfo { get { return m_customInfo_send; } }

		public bool Enabled { get { return value_state == State.Enabled; } }

		private State m_state
		{
			get { return value_state; }
			set
			{
				if (value_state == value || value_state == State.Closed)
					return;
				m_logger.debugLog("state change from " + value_state + " to " + value, "set_m_state()", Logger.severity.DEBUG);
				value_state = value;
				m_interpreter.Mover.MoveAndRotateStop();

				switch (value_state)
				{
					case State.Enabled:
						return;

					case State.Disabled:
						m_navSet.OnStartOfCommands(); // here so that fighter gets thrown out and weapons disabled
						m_interpreter.instructionQueue.Clear();
						m_nextAllowedInstructions = Globals.ElapsedTime;
						return;

					case State.Halted:
						m_endOfHalt = Globals.ElapsedTime.Add(new TimeSpan(0, 5, 0));
						m_interpreter.Mover.SetDamping(true);
						return;

					case State.Closed:
						if (GridBeingControlled != null)
							ReleaseControlledGrid();
						m_interpreter = null;
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
			this.m_block = new ShipControllerBlock(block, new NetworkClient(block, HandleMessage));
			this.m_logger = new Logger(GetType().Name, block);
			this.m_interpreter = new Interpreter(m_block);

			this.m_block.CubeBlock.OnClosing += CubeBlock_OnClosing;

			((MyCubeBlock)block).ResourceSink.SetRequiredInputFuncByType(new MyDefinitionId(typeof(MyObjectBuilder_GasProperties), "Electricity"), PowerRequired);

			// for my German friends...
			if (!m_block.Terminal.DisplayNameText.Contains("[") && !m_block.Terminal.DisplayNameText.Contains("]"))
				m_block.Terminal.SetCustomName(m_block.Terminal.DisplayNameText + " []");

			m_logger.debugLog("Created autopilot for: " + block.DisplayNameText, GetType().Name);

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
					m_nextCustomInfo = Globals.UpdateCount + 100ul;
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
						if (!m_block.Controller.ControlThrusters || Globals.ElapsedTime > m_endOfHalt)
							m_state = State.Disabled;
						return;
					case State.Closed:
						return;
					default:
						throw new Exception("Case not implemented: "+m_state);
				}

				if (MyAPIGateway.Players.GetPlayerControllingEntity(m_controlledGrid) != null)
				{
					m_state = State.Player;
					return;
				}

				if (m_message != null)
				{
					m_interpreter.enqueueAllActions(m_message.Content);
					m_message = null;
					m_navSet.OnStartOfCommands();
					m_interpreter.Mover.MoveAndRotateStop(false);
				}

				if (this.Resume != null)
					ResumeFromSave();

				EnemyFinder ef = m_navSet.Settings_Current.EnemyFinder;
				if (ef != null)
					ef.Update();

				if (m_navSet.Settings_Current.WaitUntil > Globals.ElapsedTime)
					return;

				if (m_interpreter.SyntaxError)
					m_interpreter.Mover.MoveAndRotateStop();
				else if (MoveAndRotate())
					return;

				if (m_interpreter.hasInstructions())
				{
					m_logger.debugLog("running instructions", "Update()");

					while (m_interpreter.instructionQueue.Count != 0 && m_navSet.Settings_Current.NavigatorMover == null)
					{
						m_interpreter.instructionQueue.Dequeue().Invoke();
						if (m_navSet.Settings_Current.WaitUntil > Globals.ElapsedTime)
						{
							m_logger.debugLog("now waiting until " + m_navSet.Settings_Current.WaitUntil, "Update()");
							return;
						}
					}

					if (m_navSet.Settings_Current.NavigatorMover == null)
					{
						m_logger.debugLog("interpreter did not yield a navigator", "Update()", Logger.severity.INFO);
						ReleaseControlledGrid();
					}
					return;
				}

				if (!m_interpreter.SyntaxError)
					if (Rotate())
						return;

				if (m_nextAllowedInstructions > Globals.ElapsedTime)
				{
					m_logger.debugLog("Delaying instructions", "UpdateThread()", Logger.severity.INFO);
					m_navSet.Settings_Task_NavWay.WaitUntil = m_nextAllowedInstructions;
					return;
				}

				m_logger.debugLog("enqueing instructions", "Update()", Logger.severity.DEBUG);
				m_nextAllowedInstructions = Globals.ElapsedTime + MinTimeInstructions;
				m_interpreter.enqueueAllActions();

				if (!m_interpreter.hasInstructions())
					ReleaseControlledGrid();
				m_navSet.OnStartOfCommands();
				m_interpreter.Mover.MoveAndRotateStop(false);
			}
			catch (Exception ex)
			{
				m_logger.alwaysLog("Exception: " + ex, "Update()", Logger.severity.ERROR);
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

				Profiler.Profile(m_interpreter.Mover.MoveAndRotate);
				return true;
			}
			return false;
		}

		private bool Rotate()
		{
			INavigatorRotator navR = m_navSet.Settings_Current.NavigatorRotator;
			if (navR != null)
			{
				//run the rotator by itself until direction is matched

				Profiler.Profile(navR.Rotate);

				Profiler.Profile(m_interpreter.Mover.MoveAndRotate);

				if (!m_navSet.DirectionMatched())
					return true;
				m_interpreter.Mover.StopRotate();
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
				|| !m_block.Controller.ControlThrusters)
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
				m_logger.alwaysLog("Failed to remove " + m_controlledGrid.DisplayName + " from GridBeingControlled", "ReleaseControlledGrid()", Logger.severity.FATAL);
				throw new InvalidOperationException("Failed to remove " + m_controlledGrid.DisplayName + " from GridBeingControlled");
			}

			//myLogger.debugLog("Released control of " + ControlledGrid.DisplayName, "ReleaseControlledGrid()", Logger.severity.DEBUG);
			m_controlledGrid = null;
		}

		#endregion

		#region Custom Info

		private void UpdateCustomInfo()
		{
			if (m_state == State.Halted)
				m_customInfo_build.AppendLine("Autopilot crashed, see log for details");
			else
				BuildCustomInfo();

			if (!m_customInfo_build.EqualsIgnoreCapacity( m_customInfo_send))
			{
				StringBuilder temp = m_customInfo_send;
				m_customInfo_send = m_customInfo_build;
				m_customInfo_build = temp;
				SendCustomInfo();
			}
			//else
			//	m_logger.debugLog("no change in custom info", "UpdateCustomInfo()");

			m_customInfo_build.Clear();
		}

		private void BuildCustomInfo()
		{
			if (m_interpreter.Errors.Length != 0)
			{
				m_customInfo_build.AppendLine("Errors:");
				m_customInfo_build.Append(m_interpreter.Errors);
				m_customInfo_build.AppendLine();
			}

			if (m_controlledGrid == null)
			{
				if (!m_block.Controller.ControlThrusters)
					m_customInfo_build.AppendLine("Disabled");
				else if (m_block.CubeGrid.IsStatic)
					m_customInfo_build.AppendLine("Grid is a station");
				//else if (m_block.CubeGrid.BigOwners.Count == 0)
				//	m_customInfo_build.AppendLine("Grid is unowned");
				else if (!m_block.Controller.IsWorking)
					m_customInfo_build.AppendLine("Not working");
				//else if (!m_block.CubeGrid.BigOwners.Contains(m_block.Controller.OwnerId))
				//	m_customInfo_build.AppendLine("Block cannot control grid");
				else
				{
					MyCubeGrid mcg = m_block.CubeGrid as MyCubeGrid;
					if (mcg.HasMainCockpit() && !m_block.Controller.IsMainCockpit)
						m_customInfo_build.AppendLine("Not main cockpit");
					else
						m_customInfo_build.AppendLine("Another autpilot controlling ship");
				}
				return;
			}

			bool moving = true;

			TimeSpan waitUntil = m_navSet.Settings_Current.WaitUntil;
			if (waitUntil > Globals.ElapsedTime)
			{
				moving = false;
				m_customInfo_build.Append("Waiting for ");
				m_customInfo_build.AppendLine(PrettySI.makePretty(waitUntil - Globals.ElapsedTime));
			}

			IMyPlayer controlling = MyAPIGateway.Players.GetPlayerControllingEntity(m_controlledGrid);
			if (controlling != null)
			{
				moving = false;
				m_customInfo_build.Append("Player controlling: ");
				m_customInfo_build.AppendLine(controlling.DisplayName);
			}

			if (moving)
			{

				Pathfinder.Pathfinder path = m_interpreter.Mover.Pathfinder;
				if (path != null && m_navSet.Settings_Current.CollisionAvoidance)
				{
					if (!path.ReportCanMove || !path.ReportCanRotate)
					{
						m_customInfo_build.AppendLine("Pathfinder:");
						if (!path.ReportCanMove)
						{
							if (path.MoveObstruction != null)
							{
								m_customInfo_build.Append("Movement blocked by ");
								m_customInfo_build.AppendLine(path.MoveObstruction.GetNameForDisplay(m_block.CubeBlock.OwnerId));
							}
							else
								m_customInfo_build.AppendLine("Cannot move");
						}
						else if (!path.ReportCanRotate)
						{
							if (path.RotateObstruction != null)
							{
								m_customInfo_build.Append("Rotation blocked by ");
								m_customInfo_build.AppendLine(path.RotateObstruction.GetNameForDisplay(m_block.CubeBlock.OwnerId));
							}
							else
								m_customInfo_build.AppendLine("Cannot rotate safely");
						}
						m_customInfo_build.AppendLine();
					}
				}

				INavigatorMover navM = m_navSet.Settings_Current.NavigatorMover;
				if (navM != null)
				{
					navM.AppendCustomInfo(m_customInfo_build);
					if (!float.IsNaN(m_navSet.Settings_Current.Distance))
					{
						m_customInfo_build.Append("Distance: ");
						m_customInfo_build.AppendLine(m_navSet.PrettyDistance());
					}
				}

				INavigatorRotator navR = m_navSet.Settings_Current.NavigatorRotator;
				if (navR != null && navR != navM)
					navR.AppendCustomInfo(m_customInfo_build);
			}

			EnemyFinder ef = m_navSet.Settings_Current.EnemyFinder;
			if (ef != null && ef.Grid == null)
			{
				m_customInfo_build.Append("Enemy finder: ");
				switch (ef.m_reason)
				{
					case GridFinder.ReasonCannotTarget.None:
						m_customInfo_build.AppendLine("No enemy detected");
						break;
					case GridFinder.ReasonCannotTarget.Too_Far:
						m_customInfo_build.Append(ef.m_bestGrid.HostileName());
						m_customInfo_build.AppendLine(" is too far");
						break;
					case GridFinder.ReasonCannotTarget.Too_Fast:
						m_customInfo_build.Append(ef.m_bestGrid.HostileName());
						m_customInfo_build.AppendLine(" is too fast");
						break;
					case GridFinder.ReasonCannotTarget.Grid_Condition:
						m_customInfo_build.Append(ef.m_bestGrid.HostileName());
						m_customInfo_build.AppendLine(" cannot be targeted");
						break;
				}
			}

			string complaint = m_navSet.Settings_Current.Complaint;
			if (complaint != null)
				m_customInfo_build.AppendLine(complaint);
		}

		private void SendCustomInfo()
		{
			ByteConverter.AppendBytes(m_customInfo_message, m_block.CubeBlock.EntityId);
			ByteConverter.AppendBytes(m_customInfo_message, m_customInfo_send.ToString());

			m_logger.debugLog("sending message, length: " + m_customInfo_message.Count, "SendCustomInfo()");
			m_logger.debugLog("Message:\n" + m_customInfo_send, "SendCustomInfo()");
			byte[] asByteArray = m_customInfo_message.ToArray();
			MyAPIGateway.Utilities.TryInvokeOnGameThread(() => {
				MyAPIGateway.Multiplayer.SendMessageToOthers(ModId_CustomInfo, asByteArray);
				Autopilot_CustomInfo.MessageHandler(asByteArray);
			}, m_logger);

			m_customInfo_message.Clear();
		}

		#endregion Custom Info

		private void HandleMessage(Message msg)
		{
			m_message = msg;
			m_interpreter.Mover.SetControl(true);
		}

		private float PowerRequired()
		{
			switch (m_state)
			{
				case State.Enabled:
					if (m_navSet.Settings_Current.WaitUntil > Globals.ElapsedTime)
						return 0.01f;
					return 0.1f;
				case State.Halted:
					return 0.01f;
				case State.Closed:
				case State.Disabled:
				default:
					return 0f;
			}
		}

		public Builder_Autopilot GetBuilder()
		{
			Builder_Autopilot result = new Builder_Autopilot() { AutopilotBlock = m_block.CubeBlock.EntityId };

			result.CurrentCommand = m_interpreter.instructionQueueString.Count - m_interpreter.instructionQueue.Count - 1;
			m_logger.debugLog("current command: " + result.CurrentCommand, "GetBuilder()");

			// don't need to save if we are not running (-1) or on first command (0)
			if (result.CurrentCommand <= 0)
				return null;

			result.Commands = string.Join(";", m_interpreter.instructionQueueString);
			m_logger.debugLog("commands: " + result.Commands, "GetBuilder()");

			EnemyFinder finder = m_navSet.Settings_Current.EnemyFinder;
			if (finder != null)
			{
				result.EngagerOriginalEntity = finder.m_originalDestEntity == null ? 0L : finder.m_originalDestEntity.EntityId;
				result.EngagerOriginalPosition = finder.m_originalPosition;
				m_logger.debugLog("added EngagerOriginalEntity: " + result.EngagerOriginalEntity + ", and EngagerOriginalPosition: " + result.EngagerOriginalPosition, "GetBuilder()");
			}

			return result;
		}

		private void ResumeFromSave()
		{
			Builder_Autopilot builder = this.Resume;
			this.Resume = null;
			m_navSet.OnStartOfCommands();
			m_logger.debugLog("resume: " + builder.Commands, "ResumeFromSave()", Logger.severity.DEBUG);
			m_interpreter.enqueueAllActions(builder.Commands);
			m_logger.debugLog("from builder, added " + m_interpreter.instructionQueue.Count + " commands", "ResumeFromSave()");

			int i;
			for (i = 0; i < builder.CurrentCommand; i++)
			{
				m_logger.debugLog("fast forward: " + m_interpreter.instructionQueueString[i], "ResumeFromSave()");
				m_interpreter.instructionQueue.Dequeue().Invoke();

				// clear navigators' levels
				for (AllNavigationSettings.SettingsLevelName levelName = AllNavigationSettings.SettingsLevelName.NavRot; levelName < AllNavigationSettings.SettingsLevelName.NavWay; levelName++)
				{
					AllNavigationSettings.SettingsLevel settingsAtLevel = m_navSet.GetSettingsLevel(levelName);
					if (settingsAtLevel.NavigatorMover != null || settingsAtLevel.NavigatorRotator != null)
					{
						m_logger.debugLog("clear " + levelName, "ResumeFromSave()");
						m_navSet.OnTaskComplete(levelName);
					}
				}
			}

			// clear wait
			m_navSet.OnTaskComplete(AllNavigationSettings.SettingsLevelName.NavWay);

			EnemyFinder finder = m_navSet.Settings_Current.EnemyFinder;
			if (finder != null)
			{
				if (builder.EngagerOriginalEntity != 0L)
				{
					if (!MyAPIGateway.Entities.TryGetEntityById(builder.EngagerOriginalEntity, out finder.m_originalDestEntity))
					{
						m_logger.alwaysLog("Failed to restore original destination enitity for enemy finder: " + builder.EngagerOriginalEntity, "ResumeFromSave()", Logger.severity.WARNING);
						finder.m_originalDestEntity = null;
					}
					else
						m_logger.debugLog("Restored original destination enitity for enemy finder: " + finder.m_originalDestEntity.getBestName(), "ResumeFromSave()");
				}
				if (builder.EngagerOriginalPosition.IsValid())
				{
					finder.m_originalPosition = builder.EngagerOriginalPosition;
					m_logger.debugLog("Restored original position for enemy finder: " + builder.EngagerOriginalPosition, "ResumeFromSave()");
				}
			}

			m_logger.debugLog("resume: " + m_interpreter.instructionQueueString[i], "ResumeFromSave()");
			m_interpreter.instructionQueue.Dequeue().Invoke();
		}

	}
}
