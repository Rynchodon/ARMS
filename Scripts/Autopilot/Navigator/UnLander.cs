using System;
using System.Text;
using Rynchodon.Autopilot.Data;
using Rynchodon.Autopilot.Movement;
using Sandbox.Common.ObjectBuilders;
using Sandbox.ModAPI;
using SpaceEngineers.Game.ModAPI;
using VRage.Game.ModAPI;
using VRage.ModAPI;
using VRageMath;

namespace Rynchodon.Autopilot.Navigator
{
	public class UnLander : NavigatorMover, INavigatorRotator
	{

		private readonly Logger m_logger;
		private readonly PseudoBlock m_unlandBlock;
		private readonly IMyEntity m_attachedEntity;
		//private readonly Vector3D m_detatchedOffset;
		private readonly Vector3 m_detachOffset;
		private readonly Vector3 m_detachDirection;
		
		private float m_detachLength;
		//private Vector3D m_destination;
		private bool m_attached = true;

		public UnLander(Mover mover, PseudoBlock unlandBlock = null)
			: base(mover)
		{
			this.m_logger = new Logger(m_controlBlock.CubeBlock);
			this.m_unlandBlock = unlandBlock ?? m_navSet.Settings_Current.LandingBlock ?? m_navSet.LastLandingBlock;

			if (this.m_unlandBlock == null)
			{
				m_logger.debugLog("No unland block", Logger.severity.INFO);
				return;
			}
			m_logger.debugLog("Unland block is missing Block property", Logger.severity.FATAL, condition: this.m_unlandBlock.Block == null);

			IMyLandingGear asGear = m_unlandBlock.Block as IMyLandingGear;
			if (asGear != null)
			{
				m_attachedEntity = asGear.GetAttachedEntity();
				m_logger.debugLog("m_attachedEntity: " + m_attachedEntity);
				if (m_attachedEntity == null)
				{
					m_logger.debugLog("Not attached: " + m_unlandBlock.Block.DisplayNameText, Logger.severity.INFO);
					return;
				}
				m_logger.debugLog("Got attached entity from Landing Gear : " + m_unlandBlock.Block.DisplayNameText, Logger.severity.DEBUG);
			}
			else
			{
				IMyShipConnector asConn = m_unlandBlock.Block as IMyShipConnector;
				if (asConn != null)
				{
					m_logger.debugLog("connector");
					IMyShipConnector other = asConn.OtherConnector;
					if (other == null)
					{
						m_logger.debugLog("Not connected: " + m_unlandBlock.Block.DisplayNameText, Logger.severity.INFO);
						return;
					}
					m_logger.debugLog("Got attached connector from Connector : " + m_unlandBlock.Block.DisplayNameText, Logger.severity.DEBUG);
					m_attachedEntity = other.CubeGrid;
				}
				else
				{
					m_logger.debugLog("Cannot unland block: " + m_unlandBlock.Block.DisplayNameText, Logger.severity.INFO);
					IMyFunctionalBlock func = m_unlandBlock.Block as IMyFunctionalBlock;
					MyAPIGateway.Utilities.TryInvokeOnGameThread(() => {
						if (func != null)
							func.RequestEnable(false);
					});
					return;
				}
			}

			IMyCubeBlock block = this.m_unlandBlock.Block;
			if (block is IMyLandingGear)
				MyAPIGateway.Utilities.TryInvokeOnGameThread(() => {
					(block as IMyFunctionalBlock).RequestEnable(true);
					if ((block.GetObjectBuilderCubeBlock() as MyObjectBuilder_LandingGear).AutoLock)
						asGear.ApplyAction("Autolock");
				});

			m_detachOffset = m_unlandBlock.Block.GetPosition() - m_attachedEntity.GetPosition();
			m_detachDirection = m_unlandBlock.WorldMatrix.Backward;
			m_detachLength = 2f;
			//m_detatchedOffset = attachOffset + leaveDirection * (20f + m_navSet.Settings_Current.DestinationRadius);
			//m_logger.debugLog("m_detatchedOffset: " + m_detatchedOffset, "UnLander()", Logger.severity.DEBUG);
			//m_detatchDirection = attachOffset + leaveDirection

			m_logger.debugLog("offset: " + m_detachOffset + ", direction: " + m_detachDirection);

			m_navSet.Settings_Task_NavMove.DestinationRadius = 1f;
			m_navSet.Settings_Task_NavMove.NavigatorMover = this;
			m_navSet.Settings_Task_NavMove.NavigatorRotator = this;
		}

		public override void Move()
		{
			if (m_attached)
			{
				IMyLandingGear asGear = m_unlandBlock.Block as IMyLandingGear;
				if (asGear != null)
				{
					m_attached = asGear.GetAttachedEntity() != null;
					if (m_attached)
					{
						m_logger.debugLog("Unlocking landing gear", Logger.severity.DEBUG);
						MyAPIGateway.Utilities.TryInvokeOnGameThread(() => {
							asGear.ApplyAction("Unlock");
						});
					}
				}
				else
				{
					IMyShipConnector asConn = m_unlandBlock.Block as IMyShipConnector;
					if (asConn != null)
					{
						m_attached = asConn.IsConnected;
						if (m_attached)
						{
							m_logger.debugLog("Unlocking connector", Logger.severity.DEBUG);
							MyAPIGateway.Utilities.TryInvokeOnGameThread(() => {
								asConn.ApplyAction("Unlock");
							});
						}
					}
					else
					{
						m_logger.debugLog("cannot unlock: " + m_unlandBlock.Block.DisplayNameText, Logger.severity.INFO);
						m_attached = false;
					}
				}

				return;
			}
			else if (m_unlandBlock.Block.IsWorking)
			{
				MyAPIGateway.Utilities.TryInvokeOnGameThread(() => {
					((IMyFunctionalBlock)m_unlandBlock.Block).RequestEnable(false);
				});
			}

			if (m_navSet.DistanceLessThanDestRadius())
			{
				if (m_detachLength >= Math.Min(m_controlBlock.CubeGrid.GetLongestDim(), m_navSet.Settings_Task_NavEngage.DestinationRadius))
				{
					m_logger.debugLog("Moved away. detach length: " + m_detachLength + ", longest dim: " + m_controlBlock.CubeGrid.GetLongestDim() + ", dest radius: " + m_navSet.Settings_Task_NavEngage.DestinationRadius, Logger.severity.INFO);
					//MyAPIGateway.Utilities.TryInvokeOnGameThread(() => {
					//	(m_unlandBlock.Block as IMyFunctionalBlock).RequestEnable(false);
					//});
					m_navSet.OnTaskComplete_NavMove();
					m_mover.MoveAndRotateStop(false);
					return;
				}
				else
				{
					m_detachLength *= 1.1f;
					m_navSet.Settings_Current.DestinationRadius = m_detachLength * 0.5f;
					m_logger.debugLog("increased detach length to " + m_detachLength);
				}
			}

			Vector3D destination = m_attachedEntity.GetPosition() + m_detachOffset + m_detachDirection * m_detachLength;
			m_mover.CalcMove(m_unlandBlock, destination, m_attachedEntity.GetLinearVelocity());
		}

		public void Rotate()
		{
			// if waypoint or anything moves the ship, there is no point in returning to unland
			if (m_navSet.Settings_Current.NavigatorMover != this)
			{
				m_logger.debugLog("lost control over movement", Logger.severity.INFO);
				m_navSet.OnTaskComplete_NavMove();
				m_mover.MoveAndRotateStop(false);
				return;
			}

			m_mover.StopRotate();
		}

		public override void AppendCustomInfo(StringBuilder customInfo)
		{
			customInfo.Append("Separating from ");
			customInfo.AppendLine(m_attachedEntity.getBestName());
			//customInfo.Append(" to ");
			//customInfo.AppendLine(m_destination.ToPretty());

			//customInfo.AppendLine(m_navSet.PrettyDistance());
		}

	}
}
