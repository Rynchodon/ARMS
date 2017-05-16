using System;
using System.Text;
using Rynchodon.Autopilot.Data;
using Rynchodon.Autopilot.Pathfinding;
using Rynchodon.Threading;
using Rynchodon.Utility;
using Rynchodon.Utility.Collections;
using Sandbox.Game.Entities;
using VRageMath;

namespace Rynchodon.Autopilot.Navigator.Mining
{
	class SurfaceMiner : AMinerComponent
	{

		public enum Stage : byte { None, GetSurface, Mine, Escape }

		private static ThreadManager Thread = new ThreadManager(1, true, "Surface Miner");

		private Vector3 m_perp1, m_perp2;
		private int m_ringIndex, m_squareIndex;
		private Vector3D m_surfacePoint;
		private bool m_finalMine;

		private Stage value_stage;
		private Stage m_stage
		{
			get { return value_stage; }
			set
			{
				if (value_stage == value)
					return;

				Log.DebugLog("stage changed from " + value_stage + " to " + value, Logger.severity.DEBUG);
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
		private Logable Log
		{ get { return LogableFrom.Pseudo(m_navSet.Settings_Current.NavigationBlock, m_stage.ToString()); } }


		public SurfaceMiner(Pathfinder pathfinder, Destination target, string oreName) : base(pathfinder, oreName)
		{
			m_target = target;

			Vector3 toDeposit = Vector3.Normalize(m_target.WorldPosition() - m_grid.GetCentre());
			toDeposit.CalculatePerpendicularVector(out m_perp1);
			Vector3.Cross(ref toDeposit, ref m_perp1, out m_perp2);

			AllNavigationSettings.SettingsLevel level = m_navSet.Settings_Task_NavMove;
			level.NavigatorMover = this;
			level.NavigatorRotator = this;
			level.IgnoreAsteroid = true;
			level.SpeedTarget = 1f;
			level.PathfinderCanChangeCourse = false;

			m_stage = Stage.GetSurface;
			Log.DebugLog("started", Logger.severity.DEBUG);
		}

		public override void AppendCustomInfo(StringBuilder customInfo)
		{
			switch (m_stage)
			{
				case Stage.GetSurface:
					customInfo.AppendLine("Scanning surface.");
					break;
				case Stage.Mine:
					customInfo.Append("Surface mining ");
					customInfo.Append(m_oreName);
					customInfo.Append(" at ");
					customInfo.AppendLine(m_target.WorldPosition().ToPretty());
					break;
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
					Log.DebugLog("Reached target", Logger.severity.DEBUG);
					m_stage = Stage.Escape;
					new EscapeMiner(m_pathfinder, TargetVoxel);
				}
				else
				{
					Log.DebugLog("Reached surface point", Logger.severity.DEBUG);
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
			surfaceFinder.Radius = 1f;
			float maxRingSize = m_grid.LocalVolume.Radius; maxRingSize *= maxRingSize;
			bool overMax = false;
			surfaceFinder.P0 = m_grid.GetCentre();
			Vector3D targetWorld = m_target.WorldPosition();

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

				surfaceFinder.P1 = targetWorld + direct1 + direct2;
				if (CapsuleDExtensions.Intersects(ref surfaceFinder, (MyVoxelBase)m_target.Entity, out m_surfacePoint))
				{
					Log.DebugLog("test from " + surfaceFinder.P0 + " to " + surfaceFinder.P1 + ", hit voxel at " + m_surfacePoint);
					m_finalMine = Vector3D.DistanceSquared(m_surfacePoint, m_target.WorldPosition()) < 1d;
					m_stage = Stage.Mine;
					return;
				}
				Log.DebugLog("test from " + surfaceFinder.P0 + " to " + surfaceFinder.P1 + ", did not hit voxel. P1 constructed from " + targetWorld + ", " + direct1 + ", " + direct2);

				if (ring.DistanceSquared > maxRingSize)
				{
					if (overMax)
					{
						Log.AlwaysLog("Infinite loop", Logger.severity.FATAL);
						throw new Exception("Infinte loop");
					}
					overMax = true;
					Log.DebugLog("Over max ring size, starting next level", Logger.severity.INFO);
					m_squareIndex = 0;
					m_ringIndex = 0;
				}
			}

			Log.AlwaysLog("Infinite loop", Logger.severity.FATAL);
			throw new Exception("Infinte loop");
		}

	}
}
