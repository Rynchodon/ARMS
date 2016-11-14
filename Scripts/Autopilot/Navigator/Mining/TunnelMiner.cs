using System.Text;
using Rynchodon.Autopilot.Data;
using Rynchodon.Autopilot.Pathfinding;
using Sandbox.Game.Entities;
using VRageMath;

namespace Rynchodon.Autopilot.Navigator.Mining
{
	class TunnelMiner : AMinerComponent
	{
		public enum Stage : byte { None, Mine, Tunnel, /*Backout*/ }

		private readonly Logger m_logger;

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
					case Stage.Mine:
						EnableDrills(true);
						break;
					case Stage.Tunnel:
						if (m_target.Entity is MyPlanet)
						{
							new EscapeMiner(m_pathfinder, TargetVoxel);
							return;
						}
						EnableDrills(true);
						SetOutsideTarget(m_navBlock.WorldMatrix.Forward);
						break;
					//case Stage.Backout:
					//	EnableDrills(false);
					//	SetOutsideTarget(false);
					//	break;
				}

				TaskCompleteNavWay();
				value_stage = value;
			}
		}

		public TunnelMiner(Pathfinder pathfinder, Destination target, string oreName) : base(pathfinder, oreName)
		{
			m_logger = new Logger(m_navSet.Settings_Current.NavigationBlock, () => m_stage.ToString());
			m_target = target;

			m_logger.debugLog("created", Logger.severity.DEBUG);
		}

		public override void Start()
		{
			AllNavigationSettings.SettingsLevel level = m_navSet.Settings_Task_NavMove;
			level.NavigatorMover = this;
			level.NavigatorRotator = this;
			level.IgnoreAsteroid = true;
			level.SpeedTarget = 1f;
			level.PathfinderCanChangeCourse = false;

			m_stage = Stage.Mine;
			m_logger.debugLog("started", Logger.severity.DEBUG);
		}

		public override void AppendCustomInfo(StringBuilder customInfo)
		{
			switch (m_stage)
			{
				case Stage.Mine:
					customInfo.Append("Mining ore at ");
					break;
				case Stage.Tunnel:
					customInfo.Append("Tunneling to ");
					break;
				//case Stage.Backout:
				//	customInfo.Append("Backing out to ");
				//	break;
			}
			customInfo.AppendLine(m_target.WorldPosition().ToPretty());
		}

		public override void Move()
		{
			switch (m_stage)
			{
				case Stage.Mine:
					if (!MineTarget())
					{
						m_stage = Stage.Tunnel;
						new EscapeMiner(m_pathfinder, TargetVoxel);
					}
					break;
				case Stage.Tunnel:
					if (!IsNearVoxel(2d))
					{
						m_logger.debugLog("Outside of voxel", Logger.severity.INFO);
						m_mover.MoveAndRotateStop();
						m_navSet.OnTaskComplete_NavMove();
					}
					else if (!TunnelTarget())
						new EscapeMiner(m_pathfinder, TargetVoxel);
					break;
				//case Stage.Backout:
				//	if (!IsNearVoxel(2d))
				//	{
				//		m_logger.debugLog("Outside of voxel", Logger.severity.INFO);
				//		m_mover.MoveAndRotateStop();
				//		m_navSet.OnTaskComplete_NavMove();
				//	}
				//	else if (!BackoutTarget())
				//		m_stage = Stage.Tunnel;
				//	break;
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
				//case Stage.Backout:
				//	{
				//		Vector3 direction = Vector3.Normalize(m_navBlock.WorldPosition - m_target.WorldPosition());
				//		m_mover.CalcRotate(m_navBlock, RelativeDirection3F.FromWorld(m_grid, direction));
				//		break;
				//	}
			}
		}

		private bool MineTarget()
		{
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
				m_logger.debugLog("Reached position: " + m_target, Logger.severity.DEBUG);
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
				m_logger.debugLog("Reached position: " + m_target, Logger.severity.WARNING);
				m_target.SetWorld(m_target.WorldPosition() + m_navBlock.WorldMatrix.Forward * 100d);
				return true;
			}
			else if (IsStuck)
			{
				m_logger.debugLog("Stuck", Logger.severity.DEBUG);
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
