using System;
using System.Text;
using Rynchodon.Autopilot.Data;
using Rynchodon.Autopilot.Pathfinding;
using Rynchodon.Threading;
using Rynchodon.Utility.Collections;
using Sandbox.Game.Entities;
using VRageMath;

namespace Rynchodon.Autopilot.Navigator.Mining
{
	class SurfaceMiner : AMinerComponent
	{

		public enum Stage : byte { None, GetSurface, Mine, Escape }

		private static ThreadManager Thread = new ThreadManager(1, true, "Surface Miner");

		private readonly Logger m_logger;
		private Vector3 m_perp1, m_perp2;
		private int m_ringIndex, m_squareIndex;
		private Vector3D m_startPosition, m_surfacePoint;
		private bool m_finalMine;

		private Stage value_stage;
		private Stage m_stage
		{
			get { return value_stage; }
			set
			{
				if (value_stage == value)
					return;

				m_logger.debugLog("stage changed from " + value_stage + " to " + value, Logger.severity.DEBUG);
				switch (value)
				{
					case Stage.GetSurface:
						Thread.EnqueueAction(SetNextSurfacePoint);
						break;
					case Stage.Mine:
						EnableDrills(true);
						break;
				}

				m_navSet.OnTaskComplete_NavWay();
				value_stage = value;
			}
		}

		public SurfaceMiner(Pathfinder pathfinder, Destination target, string oreName) : base(pathfinder, oreName)
		{
			m_logger = new Logger(m_navSet.Settings_Current.NavigationBlock, () => m_stage.ToString());
			m_target = target;

			m_logger.debugLog("created", Logger.severity.DEBUG);
		}

		public override void Start()
		{
			m_startPosition = m_navBlock.WorldPosition;
			Vector3 toVoxelCentre = Vector3.Normalize(m_target.WorldPosition() - m_startPosition);
			toVoxelCentre.CalculatePerpendicularVector(out m_perp1);
			Vector3.Cross(ref toVoxelCentre, ref m_perp1, out m_perp2);

			AllNavigationSettings.SettingsLevel level = m_navSet.Settings_Task_NavMove;
			level.NavigatorMover = this;
			level.NavigatorRotator = this;
			level.IgnoreAsteroid = true;
			level.SpeedTarget = 1f;
			level.PathfinderCanChangeCourse = false;

			m_stage = Stage.GetSurface;
			m_logger.debugLog("started", Logger.severity.DEBUG);
		}

		public override void AppendCustomInfo(StringBuilder customInfo)
		{
			switch (m_stage)
			{
				case Stage.GetSurface:
					customInfo.AppendLine("Scanning surface.");
					break;
				case Stage.Mine:
					customInfo.Append("Mining ");
					customInfo.Append(m_oreName);
					customInfo.Append(" at ");
					customInfo.AppendLine(m_target.WorldPosition().ToPretty());
					break;
				//case Stage.Backout:
				//	customInfo.Append("Backing out to ");
				//	break;
			}
		}

		public override void Move()
		{
			switch (m_stage)
			{
				case Stage.GetSurface:
					m_mover.StopMove();
					break;
				case Stage.Mine:
					MineTarget();
					break;
				case Stage.Escape:
					m_stage = Stage.GetSurface;
					break;
			}
		}

		public override void Rotate()
		{
			switch (m_stage)
			{
				case Stage.GetSurface:
					{
						m_mover.StopRotate();
						break;
					}
				case Stage.Mine:
					{
						Vector3 direction = Vector3.Normalize(m_target.WorldPosition() - m_navBlock.WorldPosition);
						m_mover.CalcRotate(m_navBlock, RelativeDirection3F.FromWorld(m_grid, direction));
						break;
					}
			}
		}

		private void MineTarget()
		{
			if (m_navSet.Settings_Current.Distance > 10f && !m_navSet.DirectionMatched() && IsNearVoxel())
			{
				// match direction
				m_mover.StopMove();
			}
			else if (AbortMining())
			{
				m_stage = Stage.Escape;
				new EscapeMiner(m_pathfinder, TargetVoxel);
			}
			else if (m_navSet.DistanceLessThan(1f))
			{
				if (m_finalMine)
				{
					m_logger.debugLog("Reached target", Logger.severity.DEBUG);
					m_stage = Stage.Escape;
					new EscapeMiner(m_pathfinder, TargetVoxel);
				}
				else
				{
					m_logger.debugLog("Reached surface point", Logger.severity.DEBUG);
					m_stage = Stage.GetSurface;
				}
			}
			else
			{
				Destination dest = Destination.FromWorld(m_target.Entity, ref m_surfacePoint);
				m_pathfinder.MoveTo(destinations: dest);
			}
		}

		private void SetNextSurfacePoint()
		{
			CapsuleD surfaceFinder;
			surfaceFinder.P1 = m_target.Entity.GetCentre();
			surfaceFinder.Radius = 1f;
			float maxRingSize = m_grid.LocalVolume.Radius; maxRingSize *= maxRingSize;

			for (int i = 0; i < 1000; i++)
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
				{
					m_logger.debugLog("test from " + surfaceFinder.P0 + " to " + surfaceFinder.P1 + ", hit voxel at " + m_surfacePoint);
					m_finalMine = Vector3D.DistanceSquared(m_surfacePoint, m_target.WorldPosition()) < 1d;
					m_stage = Stage.Mine;
					return;
				}
				m_logger.debugLog("test from " + surfaceFinder.P0 + " to " + surfaceFinder.P1 + ", did not hit voxel. P0 constructed from " + m_startPosition + ", " + direct1 + ", " + direct2);

				if (ring.DistanceSquared > maxRingSize)
				{
					m_logger.debugLog("Over max ring size, starting next level", Logger.severity.INFO);
					m_squareIndex = 0;
					m_ringIndex = 0;
				}
			}

			m_logger.alwaysLog("Infinite loop", Logger.severity.FATAL);
			throw new Exception("Infinte loop");
		}

	}
}
