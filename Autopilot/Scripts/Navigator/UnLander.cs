using System;
using System.Text;
using Rynchodon.Autopilot.Data;
using Rynchodon.Autopilot.Movement;
using Sandbox.Common.ObjectBuilders;
using Sandbox.Game.Gui;
using Sandbox.ModAPI;
using VRage.ModAPI;
using VRageMath;
using Ingame = Sandbox.ModAPI.Ingame;

namespace Rynchodon.Autopilot.Navigator
{
	public class UnLander : NavigatorMover, INavigatorRotator
	{

		private readonly Logger m_logger;
		private readonly PseudoBlock m_unlandBlock;
		private readonly IMyEntity m_attachedEntity;
		private readonly Vector3D m_detatchedOffset;

		private Vector3D m_destination;
		private bool m_attached = true;

		public UnLander(Mover mover, AllNavigationSettings navSet, PseudoBlock unlandBlock = null)
			: base(mover, navSet)
		{
			this.m_logger = new Logger(GetType().Name, m_controlBlock.CubeBlock);
			this.m_unlandBlock = unlandBlock ?? m_navSet.Settings_Current.LandingBlock ?? m_navSet.LastLandingBlock;

			if (this.m_unlandBlock == null)
			{
				m_logger.debugLog("No unland block", "UnLander()", Logger.severity.INFO);
				return;
			}
			m_logger.debugLog(this.m_unlandBlock.Block == null, "Unland block is missing Block property", "UnLander()", Logger.severity.FATAL);

			IMyLandingGear asGear = m_unlandBlock.Block as IMyLandingGear;
			if (asGear != null)
			{
				m_attachedEntity = asGear.GetAttachedEntity();
				m_logger.debugLog("m_attachedEntity: " + m_attachedEntity, "UnLander()");
				if (m_attachedEntity == null)
				{
					m_logger.debugLog("Not attached: " + m_unlandBlock.Block.DisplayNameText, "UnLander()", Logger.severity.INFO);
					return;
				}
				m_logger.debugLog("Got attached entity from Landing Gear : " + m_unlandBlock.Block.DisplayNameText, "UnLander()", Logger.severity.DEBUG);
			}
			else
			{
				Ingame.IMyShipConnector asConn = m_unlandBlock.Block as Ingame.IMyShipConnector;
				if (asConn != null)
				{
					m_logger.debugLog("connector", "UnLander()");
					Ingame.IMyShipConnector other = asConn.OtherConnector;
					if (other == null)
					{
						m_logger.debugLog("Not connected: " + m_unlandBlock.Block.DisplayNameText, "UnLander()", Logger.severity.INFO);
						return;
					}
					m_logger.debugLog("Got attached connector from Connector : " + m_unlandBlock.Block.DisplayNameText, "UnLander()", Logger.severity.DEBUG);
					m_attachedEntity = other.CubeGrid;
				}
				else
				{
					m_logger.debugLog("Cannot unland block: " + m_unlandBlock.Block.DisplayNameText, "UnLander()", Logger.severity.INFO);
					return;
				}
			}

			IMyCubeBlock block = this.m_unlandBlock.Block;
			if (block is IMyLandingGear)
				MyAPIGateway.Utilities.TryInvokeOnGameThread(() => {
					(block as IMyFunctionalBlock).RequestEnable(true);
					if ((block.GetObjectBuilderCubeBlock() as MyObjectBuilder_LandingGear).AutoLock)
						asGear.ApplyAction("Autolock");
				}, m_logger);

			Vector3D attachOffset = m_unlandBlock.Block.GetPosition() - m_attachedEntity.GetPosition();
			Vector3 leaveDirection = m_unlandBlock.WorldMatrix.Backward;
			m_detatchedOffset = attachOffset + leaveDirection * 20f;
			m_logger.debugLog("m_detatchedOffset: " + m_detatchedOffset, "UnLander()", Logger.severity.DEBUG);

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
						m_logger.debugLog("Unlocking landing gear", "Move()", Logger.severity.DEBUG);
						MyAPIGateway.Utilities.TryInvokeOnGameThread(() => {
							asGear.ApplyAction("Unlock");
						}, m_logger);
					}
				}
				else
				{
					Ingame.IMyShipConnector asConn = m_unlandBlock.Block as Ingame.IMyShipConnector;
					if (asConn != null)
					{
						m_attached = asConn.IsConnected;
						if (m_attached)
						{
							m_logger.debugLog("Unlocking connector", "Move()", Logger.severity.DEBUG);
							MyAPIGateway.Utilities.TryInvokeOnGameThread(() => {
								asConn.ApplyAction("Unlock");
								asConn.RequestEnable(false);
							}, m_logger);
						}
					}
					else
					{
						m_logger.debugLog("cannot unlock: " + m_unlandBlock.Block.DisplayNameText, "Move()", Logger.severity.INFO);
						m_attached = false;
					}
				}

				return;
			}

			if (m_navSet.DistanceLessThan(10f))
			{
				m_logger.debugLog("Reached destination: " + m_destination, "Move()", Logger.severity.INFO);
				m_navSet.OnTaskComplete_NavMove();
				m_mover.StopMove();
				m_mover.StopRotate(); 
				return;
			}

			m_destination = m_attachedEntity.GetPosition() + m_detatchedOffset;
			m_mover.CalcMove(m_unlandBlock, m_destination, m_attachedEntity.GetLinearVelocity());
		}

		public void Rotate()
		{
			m_mover.StopRotate();
		}

		public override void AppendCustomInfo(StringBuilder customInfo)
		{
			customInfo.Append("Sideling");
			customInfo.Append(" to ");
			customInfo.AppendLine(m_destination.ToPretty());

			//customInfo.AppendLine(m_navSet.PrettyDistance());
		}

	}
}
