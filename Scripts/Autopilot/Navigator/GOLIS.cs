using System.Text;
using Rynchodon.Autopilot.Data;
using Rynchodon.Autopilot.Movement;
using VRageMath;

namespace Rynchodon.Autopilot.Navigator
{
	/// <summary>
	/// Flies to a point in space
	/// </summary>
	public class GOLIS : NavigatorMover, INavigatorRotator
	{

		private readonly Logger myLogger;
		private readonly PseudoBlock NavigationBlock;
		private readonly Vector3D location;
		private readonly AllNavigationSettings.SettingsLevelName m_level;

		/// <summary>
		/// Creates a GOLIS
		/// </summary>
		/// <param name="mover">The mover to use</param>
		/// <param name="navSet">The settings to use</param>
		/// <param name="location">The location to fly to</param>
		public GOLIS(Mover mover, Vector3D location, AllNavigationSettings.SettingsLevelName level = AllNavigationSettings.SettingsLevelName.NavMove)
			: base(mover)
		{
			this.myLogger = new Logger(m_controlBlock.CubeBlock);
			this.NavigationBlock = m_navSet.Settings_Current.NavigationBlock;
			this.location = location;
			this.m_level = level;

			var atLevel = m_navSet.GetSettingsLevel(level);
			atLevel.NavigatorMover = this;
			//atLevel.DestinationEntity = mover.Block.CubeBlock; // in case waypoint is blocked by moving target
		}

		#region NavigatorMover Members

		/// <summary>
		/// Calculates the movement force required to reach the target point.
		/// </summary>
		public override void Move()
		{
			if (m_navSet.DistanceLessThanDestRadius())
			{
				myLogger.debugLog("Reached destination: " + location, Logger.severity.INFO);
				m_navSet.OnTaskComplete(m_level);
				m_mover.StopMove();
				m_mover.StopRotate();
			}
			else
				m_mover.CalcMove(NavigationBlock, location, Vector3.Zero);
		}

		/// <summary>
		/// Appends:
		/// [Moving/Sideling] to (destination)
		/// Distance: " + (distance to destination)
		/// </summary>
		/// <param name="customInfo">Custom info to append to</param>
		public override void AppendCustomInfo(StringBuilder customInfo)
		{
			customInfo.Append("Moving to ");
			if (m_level == AllNavigationSettings.SettingsLevelName.NavWay)
				customInfo.Append("waypoint: ");
			customInfo.AppendLine(location.ToPretty());

			//customInfo.Append("Distance: ");
			//customInfo.Append(PrettySI.makePretty(m_navSet.Settings_Current.Distance));
			//customInfo.AppendLine("m");
		}

		#endregion

		#region INavigatorRotator Members

		/// <summary>
		/// Calculates the rotation to face the navigation block towards the target location.
		/// </summary>
		public void Rotate()
		{
			if (m_navSet.Settings_Current.Distance > m_controlBlock.CubeGrid.GetLongestDim() * 2)
			{
				m_mover.CalcRotate();
				//Vector3 direction = location - NavigationBlock.WorldPosition;
				//m_mover.CalcRotate(NavigationBlock, RelativeDirection3F.FromWorld(m_controlBlock.CubeGrid, direction));
			}
			else
				m_mover.StopRotate();
		}

		#endregion

	}
}
