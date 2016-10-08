using System.Text; // from mscorlib.dll
using Rynchodon.Autopilot.Data;
using Rynchodon.Autopilot.Pathfinding;
using VRage.Game.ModAPI;
using VRage.ModAPI; // from VRage.Game.dll
using VRageMath;

namespace Rynchodon.Autopilot.Navigator
{
	/// <summary>
	/// Fly to an entity with a world offset.
	/// </summary>
	public class Waypoint : NavigatorMover, INavigatorRotator
	{

		private readonly Logger m_logger;
		private readonly AllNavigationSettings.SettingsLevelName m_level;
		private Destination m_destination;

		private PseudoBlock NavBlock
		{
			get { return m_navSet.Settings_Current.NavigationBlock; }
		}

		public Waypoint(NewPathfinder pathfinder, AllNavigationSettings navSet, AllNavigationSettings.SettingsLevelName level, IMyEntity targetEntity, Vector3D worldOffset)
			: base(pathfinder)
		{
			this.m_logger = new Logger(m_controlBlock.CubeBlock);
			this.m_level = level;
			this.m_destination = new Destination(targetEntity, worldOffset);

			m_logger.debugLog("targetEntity is not top-most", Logger.severity.FATAL, condition: targetEntity != targetEntity.GetTopMostParent());

			IMyCubeGrid asGrid = targetEntity as IMyCubeGrid;
			if (asGrid != null && Attached.AttachedGrid.IsGridAttached(asGrid, m_controlBlock.CubeGrid, Attached.AttachedGrid.AttachmentKind.Physics))
			{
				m_logger.debugLog("Cannot fly to entity, attached: " + targetEntity.getBestName() + ", creating GOLIS", Logger.severity.WARNING);
				new GOLIS(pathfinder, m_destination.WorldPosition(), level);
				return;
			}
			if (targetEntity.Physics == null)
			{
				m_logger.debugLog("Target has no physics: " + targetEntity.getBestName() + ", creating GOLIS", Logger.severity.WARNING);
				new GOLIS(pathfinder, m_destination.WorldPosition(), level);
				return;
			}

			var setLevel = navSet.GetSettingsLevel(level);
			setLevel.NavigatorMover = this;
			//setLevel.DestinationEntity = mover.Block.CubeBlock; // to force avoidance 

			m_logger.debugLog("created, level: " + level + ", target: " + targetEntity.getBestName() + ", target position: " + targetEntity.GetPosition() + ", offset: " + worldOffset + ", position: " + m_destination.WorldPosition(), Logger.severity.DEBUG);
		}

		public override void Move()
		{
			if (m_navSet.DistanceLessThanDestRadius() || m_destination.Entity.MarkedForClose)
			{
				m_navSet.OnTaskComplete(m_level);
				m_mover.StopMove();
				m_mover.StopRotate();
			}
			else
				m_pathfinder.MoveTo(NavBlock, ref m_destination);
		}

		public void Rotate()
		{
			m_mover.CalcRotate();
		}

		public override void AppendCustomInfo(StringBuilder customInfo)
		{
			customInfo.Append("Flying to waypoint: ");
			customInfo.AppendLine(m_destination.WorldPosition().ToPretty());
		}

	}
}
