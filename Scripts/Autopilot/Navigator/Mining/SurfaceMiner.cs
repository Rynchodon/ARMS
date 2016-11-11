using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Rynchodon.Autopilot.Data;
using Rynchodon.Autopilot.Pathfinding;
using Rynchodon.Utility.Collections;
using Sandbox.Game.Entities;
using VRageMath;

namespace Rynchodon.Autopilot.Navigator.Mining
{
	class SurfaceMiner : AMinerComponent
	{

		public enum Stage : byte { None, Mine, Backout }

		private readonly Logger m_logger;
		private Vector3 m_perp1, m_perp2;
		private int m_ringIndex, m_squareIndex;
		private Vector3D m_startPosition, m_surfacePoint;

		private Stage value_stage;
		private Stage m_stage
		{
			get { return value_stage; }
			set
			{
				if (value_stage == value)
					return;

				switch (value)
				{
					case Stage.Mine:
						EnableDrills(true);
						break;
					case Stage.Backout:
						EnableDrills(false);
						SetOutsideTarget(false);
						break;
				}

				value_stage = value;
			}
		}

		public SurfaceMiner(Pathfinder pathfinder, Destination target, Stage initialStage = Stage.Mine) : base(pathfinder)
		{
			m_logger = new Logger(m_navSet.Settings_Current.NavigationBlock, () => m_stage.ToString());
			m_target = target;
			m_stage = initialStage;

			m_startPosition = m_navBlock.WorldPosition;
			Vector3 toVoxelCentre = Vector3.Normalize(m_target.WorldPosition() - m_startPosition);
			toVoxelCentre.CalculatePerpendicularVector(out m_perp1);
			Vector3.Cross(ref toVoxelCentre, ref m_perp1, out m_perp2);

			AllNavigationSettings.SettingsLevel level = m_navSet.Settings_Task_NavWay;
			level.NavigatorMover = this;
			level.IgnoreAsteroid = true;
			level.SpeedTarget = 1f;
			level.PathfinderCanChangeCourse = false;
		}

		public override void AppendCustomInfo(StringBuilder customInfo)
		{
			switch (m_stage)
			{
				case Stage.Mine:
					customInfo.Append("Mining ore at ");
					break;
				case Stage.Backout:
					customInfo.Append("Backing out to ");
					break;
			}
			customInfo.AppendLine(m_target.WorldPosition().ToPretty());
		}

		public override void Move()
		{
			switch (m_stage)
			{
				case Stage.Mine:
					MineTarget();
					break;
				case Stage.Backout:
					if (!IsNearVoxel(2d))
					{
						m_logger.debugLog("Outside of voxel", Logger.severity.INFO);
						m_mover.MoveAndRotateStop();
						m_navSet.OnTaskComplete_NavWay();
					}
					if (!BackoutTarget())
						m_stage = Stage.Mine;
					break;
			}
		}

		public override void Rotate()
		{
			switch (m_stage)
			{
				case Stage.Mine:
					{
						Vector3 direction = Vector3.Normalize(m_target.WorldPosition() - m_navBlock.WorldPosition);
						m_mover.CalcRotate(m_navBlock, RelativeDirection3F.FromWorld(m_grid, direction));
						break;
					}
				case Stage.Backout:
					{
						Vector3 direction = Vector3.Normalize(m_navBlock.WorldPosition - m_target.WorldPosition());
						m_mover.CalcRotate(m_navBlock, RelativeDirection3F.FromWorld(m_grid, direction));
						break;
					}
			}
		}

		private void MineTarget()
		{

		}

		private void SetNextSurfacePoint()
		{
			CapsuleD surfaceFinder;
			surfaceFinder.P1 = m_target.Entity.GetCentre();
			surfaceFinder.Radius = 1f;
			float maxRingSize = m_grid.LocalVolume.Radius; maxRingSize *= maxRingSize;

			while (true)
			{
				ExpandingRings.Ring ring = ExpandingRings.GetRing(m_ringIndex);
				if (m_squareIndex >= ring.Squares.Length)
				{
					ring = ExpandingRings.GetRing(++m_ringIndex);
					m_squareIndex = 0;
				}
				Vector2I square = ring.Squares[m_squareIndex++];

				Vector3 direct1; Vector3.Multiply(ref m_perp1, square.X, out direct1);
				Vector3 direct2; Vector3.Multiply(ref m_perp2, square.Y, out direct2);

				surfaceFinder.P0 = m_startPosition + direct1 + direct2;
				if (CapsuleDExtensions.Intersects(ref surfaceFinder, (MyVoxelBase)m_target.Entity, out m_surfacePoint))
					return;

				if (ring.DistanceSquared > maxRingSize)
				{
					m_logger.debugLog("Over max ring size, starting next level", Logger.severity.INFO);
					m_squareIndex = 0;
					m_ringIndex = 0;
				}
			}
		}

	}
}
