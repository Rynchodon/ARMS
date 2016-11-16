using System.Text;
using Rynchodon.Autopilot.Data;
using Rynchodon.Autopilot.Pathfinding;
using Sandbox.Game.Entities;
using VRageMath;

namespace Rynchodon.Autopilot.Navigator.Mining
{
	class EscapeMiner : AMinerComponent
	{

		public enum Stage : byte { None, /*Pathfind,*/ FromCentre, Backout }

		private readonly Logger m_logger;

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
					case Stage.FromCentre:
						SetOutsideTarget(Vector3D.Normalize(m_target.WorldPosition() - TargetVoxel.GetCentre()));
						break;
					case Stage.Backout:
						SetOutsideTarget(m_navBlock.WorldMatrix.Backward);
						break;
				}

				value_stage = value;
			}
		}

		public EscapeMiner(Pathfinder pathfinder, MyVoxelBase voxel) : base(pathfinder, string.Empty)
		{
			m_logger = new Logger(m_navSet.Settings_Current.NavigationBlock, () => m_stage.ToString());
			m_target = Destination.FromWorld(voxel, m_navBlock.WorldPosition);

			AllNavigationSettings.SettingsLevel level = m_navSet.Settings_Task_NavWay;
			level.NavigatorMover = this;
			level.NavigatorRotator = this;
			level.IgnoreAsteroid = true;
			level.SpeedTarget = 1f;
			level.PathfinderCanChangeCourse = false;

			m_stage = Stage.FromCentre;
			EnableDrills(false);
			m_logger.debugLog("started", Logger.severity.DEBUG);
		}

		public override void Rotate()
		{
			switch (m_stage)
			{
				case Stage.FromCentre:
					{
						m_mover.StopRotate();
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

		public override void Move()
		{
			switch (m_stage)
			{
				case Stage.FromCentre:
				case Stage.Backout:
					MoveToTarget();
					return;
			}
		}

		public override void AppendCustomInfo(StringBuilder customInfo)
		{
			switch (m_stage)
			{
				//case Stage.Pathfind:
				//	customInfo.AppendLine("Pathfind");
				//	break;
				case Stage.FromCentre:
					customInfo.AppendLine("From Centre");
					break;
				case Stage.Backout:
					customInfo.AppendLine("Backout");
					break;
			}
		}

		private void MoveToTarget()
		{
			if (m_navSet.DistanceLessThan(1f))
			{
				m_logger.debugLog("Reached position: " + m_target, Logger.severity.WARNING);
				if (m_stage == Stage.FromCentre)
				{
					Vector3D targetWorld = m_target.WorldPosition();
					MyVoxelBase voxel = TargetVoxel;
					m_target.SetWorld(targetWorld + Vector3D.Normalize(targetWorld - voxel.GetCentre()) * voxel.PositionComp.LocalVolume.Radius);
				}
				else
					m_target.SetWorld(m_target.WorldPosition() + m_navBlock.WorldMatrix.Backward * 100d);
			}
			else if (IsStuck)
			{
				if (m_stage == Stage.FromCentre)
				{
					m_logger.debugLog("Stuck", Logger.severity.DEBUG);
					m_stage = Stage.Backout;
				}
				else
				{
					m_logger.debugLog("Stuck", Logger.severity.DEBUG);
					m_navSet.OnTaskComplete_NavWay();
				}
			}
			else if (!IsNearVoxel(2d))
			{
				m_logger.debugLog("Outside of voxel", Logger.severity.INFO);
				m_mover.MoveAndRotateStop();
				m_navSet.OnTaskComplete_NavMove();
			}
			else
			{
				m_pathfinder.MoveTo(destinations: m_target);
			}
		}

	}
}
