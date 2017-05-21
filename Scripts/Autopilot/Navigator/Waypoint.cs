using System.Text; // from mscorlib.dll
using Rynchodon.Autopilot.Data;
using Rynchodon.Autopilot.Pathfinding;
using Rynchodon.Utility;
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
		private readonly AllNavigationSettings.SettingsLevelName m_level;
		private Destination m_destination;

		private Logable Log { get { return new Logable(m_controlBlock?.CubeBlock); } }
		private PseudoBlock NavBlock
		{
			get { return m_navSet.Settings_Current.NavigationBlock; }
		}

		public Waypoint(Pathfinder pathfinder, AllNavigationSettings.SettingsLevelName level, IMyEntity targetEntity, Vector3D worldOffset)
			: this(pathfinder, level, new Destination(targetEntity, ref worldOffset)) { }

		public Waypoint(Pathfinder pathfinder, AllNavigationSettings.SettingsLevelName level, Destination destination)
			: base(pathfinder)
		{
			this.m_level = level;
			this.m_destination = destination;

			Log.DebugLog("targetEntity is not top-most", Logger.severity.FATAL, condition: destination.Entity != destination.Entity.GetTopMostParent());

			IMyCubeGrid asGrid = destination.Entity as IMyCubeGrid;
			if (asGrid != null && Attached.AttachedGrid.IsGridAttached(asGrid, m_controlBlock.CubeGrid, Attached.AttachedGrid.AttachmentKind.Physics))
			{
				Log.DebugLog("Cannot fly to entity, attached: " + destination.Entity.getBestName() + ", creating GOLIS", Logger.severity.WARNING);
				new GOLIS(pathfinder, m_destination.WorldPosition(), level);
				return;
			}
			if (destination.Entity.Physics == null)
			{
				Log.DebugLog("Target has no physics: " + destination.Entity.getBestName() + ", creating GOLIS", Logger.severity.WARNING);
				new GOLIS(pathfinder, m_destination.WorldPosition(), level);
				return;
			}

			var setLevel = m_navSet.GetSettingsLevel(level);
			setLevel.NavigatorMover = this;
			Log.DebugLog("created, level: " + level + ", target: " + destination.Entity.getBestName() + ", target: " + m_destination + ", position: " + m_destination.WorldPosition(), Logger.severity.DEBUG);
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
				m_pathfinder.MoveTo(destinations: m_destination);
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
