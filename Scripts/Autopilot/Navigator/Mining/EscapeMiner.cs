using System.Text;
using Rynchodon.Autopilot.Data;
using Rynchodon.Autopilot.Pathfinding;
using Rynchodon.Utility;
using Sandbox.Game.Entities;
using VRageMath;

namespace Rynchodon.Autopilot.Navigator.Mining
{
	class EscapeMiner : AMinerComponent
	{

		public enum Stage : byte { None, /*Pathfind,*/Backout, FromCentre, }

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
					case Stage.Backout:
						SetOutsideTarget(m_navBlock.WorldMatrix.Backward);
						break;
					case Stage.FromCentre:
						SetOutsideTarget(Vector3D.Normalize(m_target.WorldPosition() - TargetVoxel.GetCentre()));
						break;
				}

				value_stage = value;
			}
		}

		private Logable Log
		{ get { return LogableFrom.Pseudo(m_navSet.Settings_Current.NavigationBlock, m_stage.ToString()); } }

		public EscapeMiner(Pathfinder pathfinder, MyVoxelBase voxel) : base(pathfinder, string.Empty)
		{
			m_target = Destination.FromWorld(voxel, m_navBlock.WorldPosition);

			AllNavigationSettings.SettingsLevel level = m_navSet.Settings_Task_NavWay;
			level.NavigatorMover = this;
			level.NavigatorRotator = this;
			level.IgnoreAsteroid = true;
			level.SpeedTarget = 1f;
			level.PathfinderCanChangeCourse = false;

			m_stage = Stage.Backout;
			EnableDrills(false);
			Log.DebugLog("started", Logger.severity.DEBUG);
		}

		public override void Rotate()
		{
			switch (m_stage)
			{
				case Stage.Backout:
					{
						Vector3 direction = Vector3.Normalize(m_navBlock.WorldPosition - m_target.WorldPosition());
						m_mover.CalcRotate(m_navBlock, RelativeDirection3F.FromWorld(m_grid, direction));
						break;
					}
				case Stage.FromCentre:
					{
						m_mover.StopRotate();
						break;
					}
			}
		}

		public override void Move()
		{
			switch (m_stage)
			{
				case Stage.Backout:
				case Stage.FromCentre:
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
				case Stage.Backout:
					customInfo.AppendLine("Backing out");
					break;
				case Stage.FromCentre:
					customInfo.AppendLine("Moving away");
					break;
			}
		}

		private void MoveToTarget()
		{
			if (m_navSet.DistanceLessThan(1f))
			{
				Log.DebugLog("Reached position: " + m_target, Logger.severity.WARNING);
				if (m_stage == Stage.Backout)
				{
					m_target.SetWorld(m_target.WorldPosition() + m_navBlock.WorldMatrix.Backward * 100d);
				}
				else
				{
					Vector3D targetWorld = m_target.WorldPosition();
					MyVoxelBase voxel = TargetVoxel;
					m_target.SetWorld(targetWorld + Vector3D.Normalize(targetWorld - voxel.GetCentre()) * voxel.PositionComp.LocalVolume.Radius);
				}
			}
			else if (IsStuck)
			{
				if (m_stage == Stage.Backout)
				{
					Log.DebugLog("Stuck", Logger.severity.DEBUG);
					m_stage = Stage.FromCentre;
				}
				else
				{
					Log.DebugLog("Stuck", Logger.severity.DEBUG);
					m_navSet.OnTaskComplete_NavWay();
				}
			}
			else if (!IsNearVoxel(2d))
			{
				Log.DebugLog("Outside of voxel", Logger.severity.INFO);
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
