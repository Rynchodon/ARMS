using System.Collections.Generic;
using System.Text; // from mscorlib.dll
using Rynchodon.Autopilot.Data;
using Rynchodon.Autopilot.Movement;
using Sandbox.Common.ObjectBuilders; // from MedievalEngineers.ObjectBuilders.dll and SpaceEngineers.ObjectBuilders.dll
using Sandbox.ModAPI; // from Sandbox.Common.dll
using VRage.Game.Entity;
using VRage.Game.ModAPI; // from VRage.Math.dll
using VRageMath;

namespace Rynchodon.Autopilot.Navigator
{
	public class WeldBlock : NavigatorMover, INavigatorRotator
	{

		private struct ComponentIndex
		{
			public MyPhysicalInventoryItem Component;
			public int Index;
		}

		private const float OffsetAdd = 5f;
		private const ulong TimeoutRepair = 1000ul;

		private enum Stage : byte { Lineup, Approach, Weld, Retreat }

		private readonly Logger m_logger;
		private readonly PseudoBlock m_welder;
		private readonly IMySlimBlock m_slimTarget;
		private readonly List<IMySlimBlock> m_neighbours = new List<IMySlimBlock>();
		private readonly List<Vector3I> m_emptyNeighbours = new List<Vector3I>();

		private bool m_weldersEnabled;
		private float m_offset, m_damage, m_buildLevelRatio;
		private ulong m_timeout_start, m_lastWeld;
		private Vector3D m_slimPos;
		private Stage value_stage;
		private readonly LineSegmentD m_lineUp = new LineSegmentD();

		private Stage m_stage
		{
			get { return value_stage; }
			set
			{
				if (value_stage == value)
					return;

				m_logger.debugLog("stage changed from " + value_stage + " to " + value, "set_m_stage()", Logger.severity.DEBUG);
				value_stage = value;
				m_navSet.OnTaskComplete_NavWay();

				switch (value_stage)
				{
					case Stage.Approach:
						m_lastWeld = Globals.UpdateCount;
						break;
					case Stage.Weld:
						m_lastWeld = Globals.UpdateCount;
						m_navSet.Settings_Task_NavWay.DestinationEntity = m_slimTarget.CubeGrid;
						m_navSet.Settings_Task_NavWay.SpeedMaxRelative = 1f;
						break;
				}
			}
		}

		public WeldBlock(Mover mover, AllNavigationSettings navSet, PseudoBlock welder, IMySlimBlock block)
			: base(mover, navSet)
		{
			this.m_logger = new Logger(GetType().Name, () => mover.Block.CubeGrid.DisplayName, () => block.getBestName(), () => m_stage.ToString());
			this.m_offset = welder.Block.LocalAABB.GetLongestDim() * 0.5f; // this works for default welders, may not work if mod has an exotic design
			this.m_welder = welder;
			this.m_slimTarget = block;
			this.m_timeout_start = Globals.UpdateCount + 3600ul;

			m_navSet.Settings_Task_NavEngage.NavigatorMover = this;
			m_navSet.Settings_Task_NavEngage.NavigatorRotator = this;

			m_slimTarget.ForEachCell(cell => {
				foreach (Vector3I offset in Globals.CellNeighbours)
				{
					Vector3I neighbourCell = cell + offset;
					IMySlimBlock neighbour = m_slimTarget.CubeGrid.GetCubeBlock(neighbourCell);
					if (neighbour != null)
						m_neighbours.Add(neighbour);
					else
						m_emptyNeighbours.Add(neighbourCell);
				}
				return false;
			});
			m_lineUp.To = m_slimTarget.CubeGrid.GridIntegerToWorld(m_slimTarget.Position);
		}

		public override void Move()
		{
			if (m_slimTarget.Closed())
			{
				m_logger.debugLog("target block closed: " + m_slimTarget.getBestName(), "Move()", Logger.severity.INFO);
				m_navSet.OnTaskComplete_NavEngage();
				EnableWelders(false);
				return;
			}

			m_slimTarget.ComputeWorldCenter(out m_slimPos);

			if (m_stage == Stage.Retreat)
			{
				float minDist = m_offset + 10f; minDist *= minDist;
				if (Vector3D.DistanceSquared(m_welder.WorldPosition, m_slimPos) > minDist)
				{
					m_logger.debugLog("moved away from: " + m_slimTarget.getBestName(), "Move()", Logger.severity.DEBUG);
					m_navSet.OnTaskComplete_NavEngage();
					m_mover.StopMove();
					m_mover.StopRotate();
					return;
				}
				Vector3D direction = m_welder.WorldPosition - m_slimPos;
				direction.Normalize();
				Vector3D destination = m_welder.WorldPosition + direction * 10d;
				m_navSet.Settings_Task_NavEngage.DestinationEntity = m_slimTarget.CubeGrid;
				m_mover.CalcMove(m_welder, destination, m_slimTarget.CubeGrid.Physics.LinearVelocity, true);
				return;
			}

			float offsetSquared = m_offset + OffsetAdd + OffsetAdd; offsetSquared *= offsetSquared;

			if (Vector3.DistanceSquared(m_welder.WorldPosition, m_slimPos) > offsetSquared)
			{
				EnableWelders(false);

				if (Globals.UpdateCount > m_timeout_start)
				{
					m_logger.debugLog("failed to start", "Move()");
					EnableWelders(false);
					m_stage = Stage.Retreat;
					return;
				}

				Vector3 closestPoint = m_lineUp.ClosestPoint(m_welder.WorldPosition);
				if (Vector3.DistanceSquared(m_welder.WorldPosition, closestPoint) > 1f)
				{
					m_stage = Stage.Lineup;

					double closest = float.MaxValue;
					foreach (Vector3I emptyCell in m_emptyNeighbours)
					{
						Vector3D emptyPos = m_slimTarget.CubeGrid.GridIntegerToWorld(emptyCell);
						double dist = Vector3D.DistanceSquared(m_welder.WorldPosition, emptyPos);
						if (dist < closest)
						{
							closest = dist;
							m_lineUp.From = emptyPos;
						}
					}

					//m_logger.debugLog("target: " + m_lineUp.To.ToGpsTag("Target") + ", cell: " + m_lineUp.From.ToGpsTag("Cell"), "Move()");

					Vector3 lineDirection = m_lineUp.From - m_lineUp.To;
					lineDirection.Normalize();
					m_lineUp.From = m_lineUp.To + lineDirection * 100f;

					//m_logger.debugLog("to: " + m_lineUp.To.ToGpsTag("To") + ", from: " + m_lineUp.From.ToGpsTag("From") + ", closest point: " + m_lineUp.ClosestPoint(m_welder.WorldPosition).ToGpsTag("Closest"), "Move()");

					m_mover.CalcMove(m_welder, m_lineUp.ClosestPoint(m_welder.WorldPosition), m_slimTarget.CubeGrid.Physics.LinearVelocity, false);

					return;
				}
				else // linedup up
					m_stage = Stage.Approach;
			}
			else // near target
			{
				m_stage = Stage.Weld;
				EnableWelders(true);
			}

			m_logger.debugLog(m_stage != Stage.Approach && m_stage != Stage.Weld, "moving to target in wrong stage", "Move()", Logger.severity.FATAL);

			if ((Globals.UpdateCount - m_lastWeld) > 1200ul) // this really only happens when running out of components, so a long timeout is ok
			{
				m_logger.debugLog("failed to repair block", "Move()");
				EnableWelders(false);
				m_stage = Stage.Retreat;
				return;
			}

			CheckForWeld();

			float dmg = m_slimTarget.CurrentDamage;
			float blr = m_slimTarget.BuildLevelRatio;

			if (m_slimTarget.CurrentDamage == 0f && m_slimTarget.BuildLevelRatio == 1f && (Globals.UpdateCount - m_lastWeld) > 120ul)
			{
				m_logger.debugLog("target block repaired: " + m_slimTarget.getBestName(), "Move()", Logger.severity.DEBUG);
				EnableWelders(false);
				m_stage = Stage.Retreat;
				return;
			}
			else
			{
				float offset = m_stage == Stage.Weld ? m_offset : m_offset + OffsetAdd;
				Vector3D welderFromTarget = m_controlBlock.CubeBlock.GetPosition() - m_slimPos;
				welderFromTarget.Normalize();
				m_mover.CalcMove(m_welder, m_slimPos + welderFromTarget * offset, m_slimTarget.CubeGrid.Physics.LinearVelocity, true);
			}
		}

		public void Rotate()
		{
			m_mover.CalcRotate(m_welder, RelativeDirection3F.FromWorld(m_welder.Grid, m_slimPos - m_welder.WorldPosition));
		}

		public override void AppendCustomInfo(StringBuilder customInfo)
		{
			customInfo.Append(m_stage);
			customInfo.Append(": ");
			customInfo.AppendLine(m_slimTarget.getBestName());
		}

		/// <summary>
		/// Checks the target and all neighbours for welding and updates m_lastWeld.
		/// </summary>
		private void CheckForWeld()
		{
			float buildLevel = m_slimTarget.BuildLevelRatio;
			float damage = m_slimTarget.CurrentDamage;
			foreach (IMySlimBlock slim in m_neighbours)
			{
				buildLevel += slim.BuildLevelRatio;
				damage += slim.CurrentDamage;
			}

			m_logger.debugLog("build level: " + buildLevel + ", previous: " + m_buildLevelRatio + ", damage: " + damage + ", previous: " + m_damage, "CheckForWeld()");

			if (buildLevel > m_buildLevelRatio || damage < m_damage)
				m_lastWeld = Globals.UpdateCount;

			m_buildLevelRatio = buildLevel;
			m_damage = damage;
		}

		/// <summary>
		/// Enabled/disable all welders.
		/// </summary>
		private void EnableWelders(bool enable)
		{
			if (enable == m_weldersEnabled)
				return;
			m_weldersEnabled = enable;

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
