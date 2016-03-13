using System.Text; // from mscorlib.dll
using Rynchodon.Autopilot.Data;
using Rynchodon.Autopilot.Movement;
using Sandbox.Common.ObjectBuilders; // from MedievalEngineers.ObjectBuilders.dll and SpaceEngineers.ObjectBuilders.dll
using Sandbox.ModAPI; // from Sandbox.Common.dll
using VRage.Game.Entity;
using VRageMath; // from VRage.Math.dll

namespace Rynchodon.Autopilot.Navigator
{
	public class WeldBlock : NavigatorMover, INavigatorRotator
	{

		private struct ComponentIndex
		{
			public MyPhysicalInventoryItem Component;
			public int Index;
		}

		private enum Stage : byte { Approach, Repair, Retreat }

		private readonly Logger m_logger;
		private readonly PseudoBlock m_welder;
		private readonly IMySlimBlock m_slimBlock;

		private float m_damage, m_buildLevelRatio;
		private ulong m_timeout;
		private Vector3D m_slimPos;
		private Stage m_stage;
		private readonly LineSegmentD m_approach = new LineSegmentD();

		public WeldBlock(Mover mover, AllNavigationSettings navSet, PseudoBlock welder, IMySlimBlock block)
			: base(mover, navSet)
		{
			this.m_logger = new Logger(GetType().Name, mover.Block.CubeBlock, block.getBestName);
			this.m_welder = welder;
			this.m_slimBlock = block;

			m_navSet.Settings_Task_NavEngage.NavigatorMover = this;
			m_navSet.Settings_Task_NavEngage.NavigatorRotator = this;
		}

		public override void Move()
		{
			if (m_slimBlock.Closed())
			{
				m_logger.debugLog("target block closed: " + m_slimBlock.getBestName(), "Move()", Logger.severity.INFO);
				m_navSet.OnTaskComplete_NavEngage();
				EnableWelders(false);
				return;
			}

			m_slimBlock.ComputeWorldCenter(out m_slimPos);

			switch (m_stage)
			{
				case Stage.Approach:
					if (m_navSet.DistanceLessThanDestRadius())
					{
						m_logger.debugLog("close to target, enabling welders", "Move()", Logger.severity.DEBUG);
						EnableWelders(true);
						m_stage = Stage.Repair;
					}
					UpdateTimeout();
					goto case Stage.Repair;
				case Stage.Repair:
					{
						if (Globals.UpdateCount > m_timeout)
						{
							m_logger.debugLog("failed to repair block", "Move()");
							EnableWelders(false);
							m_stage = Stage.Retreat;
						}

						float dmg = m_slimBlock.CurrentDamage;
						float blr = m_slimBlock.BuildLevelRatio;

						if (dmg < m_damage || blr > m_buildLevelRatio)
							UpdateTimeout();

						m_damage = dmg;
						m_buildLevelRatio = blr;

						if (m_damage == 0f && m_buildLevelRatio == 1f)
						{
							m_logger.debugLog("target block repaired: " + m_slimBlock.getBestName(), "Move()", Logger.severity.DEBUG);
							EnableWelders(false);
							m_stage = Stage.Retreat;
						}
						else
						{
							Vector3D direction = m_controlBlock.CubeBlock.GetPosition() - m_slimPos;
							direction.Normalize();
							m_mover.CalcMove(m_welder, m_slimPos + direction * 2.5f, m_slimBlock.CubeGrid.Physics.LinearVelocity, true);
						}
						return;
					}
				case Stage.Retreat:
					{
						float destRadius = m_navSet.Settings_Current.DestinationRadius;
						if (Vector3D.DistanceSquared(m_welder.WorldPosition, m_slimPos) > destRadius * destRadius)
						{
							m_logger.debugLog("moved away from: " + m_slimBlock.getBestName(), "Move()", Logger.severity.DEBUG);
							m_navSet.OnTaskComplete_NavEngage();
							m_mover.StopMove();
							m_mover.StopRotate();
							return;
						}
						Vector3D direction = m_welder.WorldPosition - m_slimPos;
						direction.Normalize();
						Vector3D destination = m_welder.WorldPosition + direction * 10d;
						m_mover.CalcMove(m_welder, destination, m_slimBlock.CubeGrid.Physics.LinearVelocity, true);
						return;
					}
			}
		}

		public void Rotate()
		{
			m_mover.CalcRotate(m_welder, RelativeDirection3F.FromWorld(m_welder.Grid, m_slimPos - m_welder.WorldPosition));
		}

		public override void AppendCustomInfo(StringBuilder customInfo)
		{
			switch (m_stage)
			{
				case Stage.Approach:
					customInfo.Append("Approaching: ");
					break;
				case Stage.Repair:
					customInfo.Append("Repairing: ");
					break;
				case Stage.Retreat:
					customInfo.Append("Moving away from: ");
					break;
			}
			customInfo.AppendLine(m_slimBlock.getBestName());
		}

		/// <summary>
		/// Update the repairing timeout
		/// </summary>
		private void UpdateTimeout()
		{
			this.m_timeout = Globals.UpdateCount + 1000ul;
		}

		/// <summary>
		/// Enabled/disable all welders.
		/// </summary>
		private void EnableWelders(bool enable)
		{
			if (enable)
				m_logger.debugLog("Enabling welders", "EnableWelders()", Logger.severity.DEBUG);
			else
				m_logger.debugLog("Disabling welders", "EnableWelders()", Logger.severity.DEBUG);

			var cache = CubeGridCache.GetFor(m_controlBlock.CubeGrid);
			if (cache == null)
			{
				m_logger.debugLog("Failed to get cache", "EnableWelders()", Logger.severity.INFO);
				return;
			}
			var allWelders = cache.GetBlocksOfType(typeof(MyObjectBuilder_ShipWelder));
			if (allWelders == null)
			{
				m_logger.debugLog("Failed to get block list", "EnableWelders()", Logger.severity.INFO);
				return;
			}

			MyAPIGateway.Utilities.TryInvokeOnGameThread(() => {
				foreach (IMyShipWelder welder in allWelders)
					if (!welder.Closed)
						welder.RequestEnable(enable);
			}, m_logger);
		}

	}
}
