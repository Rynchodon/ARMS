using System;
using System.Text;
using Rynchodon.AntennaRelay;
using Rynchodon.Autopilot.Data;
using Rynchodon.Autopilot.Movement;
using Sandbox.Common.ObjectBuilders;
using Sandbox.ModAPI;
using VRage;
using VRage.ModAPI;
using VRageMath;
using Ingame = Sandbox.ModAPI.Ingame;

namespace Rynchodon.Autopilot.Navigator
{

	public class Grinder : NavigatorMover, IEnemyResponse
	{

		private enum Stage : byte { None, Intercept, Grind }

		private readonly Logger m_logger;
		private readonly MultiBlock<MyObjectBuilder_ShipGrinder> m_navGrind;
		private readonly Vector3 m_startPostion;
		private readonly float m_grinderOffset;
		private readonly float m_longestDimension;

		private Vector3D m_targetPosition;
		private IMyEntity m_enemy;
		private ulong m_next_grinderFullCheck;
		private bool m_grinderFull;
		private Stage value_stage;

		private Stage m_stage
		{
			get { return value_stage; }
			set
			{
				if (value == value_stage)
					return;
				m_logger.debugLog("Changing stage from " + value_stage + " to " + value, "set_m_stage()", Logger.severity.DEBUG);
				value_stage = value;
			}
		}

		public Grinder(Mover mover, AllNavigationSettings navSet, Vector3 startPosition)
			: base(mover, navSet)
		{
			this.m_logger = new Logger(GetType().Name, () => m_controlBlock.CubeGrid.DisplayName, () => m_stage.ToString());
			this.m_startPostion = startPosition;
			this.m_longestDimension = m_controlBlock.CubeGrid.GetLongestDim();

			PseudoBlock navBlock = m_navSet.Settings_Current.NavigationBlock;
			m_navGrind = navBlock.Block is Ingame.IMyShipGrinder
				? new MultiBlock<MyObjectBuilder_ShipGrinder>(navBlock.Block)
				: new MultiBlock<MyObjectBuilder_ShipGrinder>(m_mover.Block.CubeGrid);

			if (m_navGrind.FunctionalBlocks == 0)
			{
				m_logger.debugLog("no working drills", "Grinder()", Logger.severity.INFO);
				return;
			}

			m_grinderOffset = m_navGrind.Block.GetLengthInDirection(m_navGrind.Block.GetFaceDirection()[0]) * 0.5f;
			if (m_navSet.Settings_Current.DestinationRadius > m_longestDimension)
			{
				m_logger.debugLog("Reducing DestinationRadius from " + m_navSet.Settings_Current.DestinationRadius + " to " + m_longestDimension, "MinerVoxel()", Logger.severity.DEBUG);
				m_navSet.Settings_Task_NavRot.DestinationRadius = m_longestDimension;
			}
			m_logger.debugLog("m_grinderOffset: " + m_grinderOffset, "Grinder()");
		}

		~Grinder()
		{
			try
			{
				m_logger.debugLog("disposing", "~Grinder()");
				EnableGrinders(false);
			}
			catch { }
		}

		#region IEnemyResponse Members

		public bool CanRespond()
		{
			return m_mover.CanMoveForward(m_mover.Block.Pseudo) && m_navGrind.FunctionalBlocks != 0 && !GrinderFull();
		}

		public bool CanTarget(IMyCubeGrid grid)
		{
			return true;
		}

		public void UpdateTarget(LastSeen enemy)
		{
			if (enemy == null)
			{
				if (m_enemy != null)
				{
					m_logger.debugLog("lost enemy", "UpdateTarget()");
					//EnableGrinders(false);
					m_enemy = null;
				}
			}
			else if (enemy.Entity != m_enemy)
			{
				m_logger.debugLog("have enemy", "UpdateTarget()");
				//EnableGrinders(true);
				m_enemy = enemy.Entity;
			}
		}

		#endregion

		public override void Move()
		{
			if (m_enemy == null)
			{
				m_mover.StopMove();
				return;
			}

			Vector3 targetCentre = m_enemy.GetCentre();

			Vector3 enemyVelocity = m_enemy.GetLinearVelocity();
			if (enemyVelocity.LengthSquared() > 10f)
			{
				float targetLongest = m_enemy.LocalAABB.GetLongestDim();

				Vector3 furthest = targetCentre + enemyVelocity * 100f;
				Line approachTo = new Line(targetCentre, furthest, false);

				float multi = m_stage == Stage.Intercept ? 0.5f : 1f;
				if (!approachTo.PointInCylinder(targetLongest * multi, m_navGrind.WorldPosition))
				{
					m_targetPosition = targetCentre;
					Vector3 direction = furthest - targetCentre;
					direction.Normalize();
					Move_Intercept(targetCentre + direction * (targetLongest + m_longestDimension));
					return;
				}
			}

			Move_Grind();
		}

		private void Move_Grind()
		{
			EnableGrinders(true);
			m_stage = Stage.Grind;

			m_navSet.Settings_Task_NavEngage.DestinationEntity = m_enemy;

			if (m_navSet.Settings_Current.DistanceAngle > 1f)
			{
				m_mover.CalcMove(m_navGrind, m_navGrind.WorldPosition, m_enemy.GetLinearVelocity(), false);
				return;
			}

			IMyCubeGrid targetGrid = m_enemy as IMyCubeGrid;

			m_targetPosition = GridCellCache.GetCellCache(targetGrid).GetClosestOccupiedCell(m_navGrind.WorldPosition);

			Vector3 blockToGrinder = m_navGrind.WorldPosition - m_targetPosition;
			blockToGrinder.Normalize();

			Vector3D destination = m_targetPosition + blockToGrinder * (m_grinderOffset + targetGrid.GridSize * 0.5f);
			m_logger.debugLog("total offset: " + (m_grinderOffset + targetGrid.GridSize * 0.5f), "Move_Grind()");

			m_logger.debugLog("grind pos: " + m_navGrind.WorldPosition + ", moving to " + destination, "Move_Grind()");
			m_mover.CalcMove(m_navGrind, destination, m_enemy.GetLinearVelocity(), true);
		}

		private void Move_Intercept(Vector3 position)
		{
			EnableGrinders(false);
			m_stage = Stage.Intercept;

			m_logger.debugLog("Moving to " + position, "Move_Intercept()");

			m_navSet.Settings_Task_NavEngage.DestinationEntity = null;

			m_mover.CalcMove(m_navGrind, position, m_enemy.GetLinearVelocity());
		}

		public void Rotate()
		{
			if (m_enemy == null)
			{
				m_mover.StopRotate();
				return;
			}

			m_logger.debugLog("rotating to " + m_targetPosition, "Rotate()");
			m_mover.CalcRotate(m_navGrind, RelativeDirection3F.FromWorld(m_navGrind.Grid, m_targetPosition - m_navGrind.WorldPosition));
		}

		public override void AppendCustomInfo(StringBuilder customInfo)
		{
			customInfo.AppendLine("Reducing an enemy to its constituent parts");
		}

		private void EnableGrinders(bool enable)
		{
			//if (enable)
			//	m_logger.debugLog("enabling grinders", "EnableGrinders()", Logger.severity.DEBUG);
			//else
			//	m_logger.debugLog("disabling grinders", "EnableGrinders()", Logger.severity.DEBUG);

			var allGrinders = CubeGridCache.GetFor(m_controlBlock.CubeGrid).GetBlocksOfType(typeof(MyObjectBuilder_ShipGrinder));
			MyAPIGateway.Utilities.TryInvokeOnGameThread(() => {
				foreach (Ingame.IMyShipGrinder grinder in allGrinders)
					if (!grinder.Closed)
						grinder.RequestEnable(enable);
			}, m_logger);
		}

		private bool GrinderFull()
		{
			if (Globals.UpdateCount < m_next_grinderFullCheck)
				return m_grinderFull;
			m_next_grinderFullCheck = Globals.UpdateCount + 100ul;

			MyFixedPoint content = 0, capacity = 0;
			int grinderCount = 0;
			var allGrinders = CubeGridCache.GetFor(m_controlBlock.CubeGrid).GetBlocksOfType(typeof(MyObjectBuilder_ShipGrinder));
			if (allGrinders == null)
				return true;

			foreach (Ingame.IMyShipGrinder grinder in allGrinders)
			{
				IMyInventory grinderInventory = (IMyInventory)Ingame.TerminalBlockExtentions.GetInventory(grinder, 0);
				content += grinderInventory.CurrentVolume;
				capacity += grinderInventory.MaxVolume;
				grinderCount++;
			}

			m_grinderFull = (float)content / (float)capacity >= 0.9f;
			return m_grinderFull;
		}

	}
}
