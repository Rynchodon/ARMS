using System.Text;
using Rynchodon.Autopilot.Data;
using Rynchodon.Autopilot.Pathfinding;
using Rynchodon.Utility;
using Sandbox.Game.Entities;
using VRageMath;

namespace Rynchodon.Autopilot.Navigator.Mining
{
	class TunnelMiner : AMinerComponent
	{
		public enum Stage : byte { None, Mine, Tunnel, Escape }

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
					case Stage.Mine:
						EnableDrills(true);
						break;
					case Stage.Tunnel:
						if (m_target.Entity is MyPlanet)
						{
							m_stage = Stage.Escape;
							new EscapeMiner(m_pathfinder, TargetVoxel);
							return;
						}
						EnableDrills(true);
						SetOutsideTarget(m_navBlock.WorldMatrix.Forward);
						break;
				}

				m_navSet.OnTaskComplete_NavWay();
				value_stage = value;
			}
		}
		private Logable Log
		{ get { return LogableFrom.Pseudo(m_navSet.Settings_Current.NavigationBlock, m_stage.ToString()); } }

		public TunnelMiner(Pathfinder pathfinder, Destination target, string oreName) : base(pathfinder, oreName)
		{
			m_target = target;

			AllNavigationSettings.SettingsLevel level = m_navSet.Settings_Task_NavMove;
			level.NavigatorMover = this;
			level.NavigatorRotator = this;
			level.IgnoreAsteroid = true;
			level.SpeedTarget = 1f;
			level.PathfinderCanChangeCourse = false;

			m_stage = Stage.Mine;
			Log.DebugLog("started", Logger.severity.DEBUG);
		}

		public override void AppendCustomInfo(StringBuilder customInfo)
		{
			switch (m_stage)
			{
				case Stage.Mine:
					customInfo.Append("Tunnel mining ");
					customInfo.Append(m_oreName);
					customInfo.Append(" at ");
					customInfo.AppendLine(m_target.WorldPosition().ToPretty());
					break;
				case Stage.Tunnel:
					customInfo.Append("Tunneling to ");
					customInfo.AppendLine(m_target.WorldPosition().ToPretty());
					break;
			}
		}

		public override void Move()
		{
			switch (m_stage)
			{
				case Stage.Mine:
					if (!MineTarget())
					{
						m_stage = Stage.Escape;
						new EscapeMiner(m_pathfinder, TargetVoxel);
					}
					break;
				case Stage.Tunnel:
					if (!IsNearVoxel(2d))
					{
						Log.DebugLog("Outside of voxel", Logger.severity.INFO);
						m_mover.MoveAndRotateStop();
						m_navSet.OnTaskComplete_NavMove();
					}
					else if (!TunnelTarget())
					{
						m_stage = Stage.Escape;
						new EscapeMiner(m_pathfinder, TargetVoxel);
					}
					break;
				case Stage.Escape:
					m_stage = Stage.Tunnel;
					break;
			}
		}

		public override void Rotate()
		{
			switch (m_stage)
			{
				case Stage.Mine:
				case Stage.Tunnel:
					{
						Vector3 direction = Vector3.Normalize(m_target.WorldPosition() - m_navBlock.WorldPosition);
						m_mover.CalcRotate(m_navBlock, RelativeDirection3F.FromWorld(m_grid, direction));
						break;
					}
			}
		}

		private bool MineTarget()
		{
			//Log.DebugLog("distance: " + m_navSet.Settings_Current.Distance);

			if (m_navSet.Settings_Current.Distance > 10f && !m_navSet.DirectionMatched() && IsNearVoxel())
			{
				// match direction
				m_mover.StopMove();
				return true;
			}
			else if (AbortMining())
			{
				return false;
			}
			else if (m_navSet.DistanceLessThan(1f))
			{
				Log.DebugLog("Reached position: " + m_target, Logger.severity.DEBUG);
				return false;
			}
			else
			{
				m_pathfinder.MoveTo(destinations: m_target);
				return true;
			}
		}

		private bool TunnelTarget()
		{
			if (m_navSet.DistanceLessThan(1f))
			{
				Log.DebugLog("Reached position: " + m_target, Logger.severity.WARNING);
				m_target.SetWorld(m_target.WorldPosition() + m_navBlock.WorldMatrix.Forward * 100d);
				return true;
			}
			else if (IsStuck)
			{
				Log.DebugLog("Stuck", Logger.severity.DEBUG);
				return false;
			}
			else
			{
				m_pathfinder.MoveTo(destinations: m_target);
				return true;
			}
		}

	}
}
