using System;
using System.Text;
using Rynchodon.Autopilot.Data;
using Rynchodon.Autopilot.Movement;
using Sandbox.ModAPI;
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
		private readonly Vector3 location;
		private readonly bool Rotating;
		private readonly bool Tertiary;

		/// <summary>
		/// Creates a GOLIS
		/// </summary>
		/// <param name="mover">The mover to use</param>
		/// <param name="navSet">The settings to use</param>
		/// <param name="location">The location to fly to</param>
		public GOLIS(Mover mover, AllNavigationSettings navSet, Vector3 location, bool waypoint = false)
			: base(mover, navSet)
		{
			this.myLogger = new Logger("GOLIS", m_controlBlock.CubeBlock);
			this.NavigationBlock = m_navSet.Settings_Current.NavigationBlock;
			this.location = location;
			this.Tertiary = waypoint;

			var atLevel = waypoint ? m_navSet.Settings_Task_NavWay : m_navSet.Settings_Task_NavMove;
			atLevel.NavigatorMover = this;
			if (m_navSet.Settings_Current.NavigatorRotator == null)
			{
				atLevel.NavigatorRotator = this;
				this.Rotating = true;
				myLogger.debugLog("added as mover and rotator", "GOLIS()");
			}
			else
				myLogger.debugLog("added as mover only", "GOLIS()");
		}

		#region NavigatorMover Members

		/// <summary>
		/// Calculates the movement force required to reach the target point.
		/// </summary>
		public override void Move()
		{
			if (m_navSet.DistanceLessThanDestRadius())
			{
				myLogger.debugLog("Reached destination: " + location, "Move()", Logger.severity.INFO);
				if (Tertiary)
					m_navSet.OnTaskComplete_NavWay();
				else
					m_navSet.OnTaskComplete_NavMove();
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
			if (Rotating)
				customInfo.Append("Moving");
			else
				customInfo.Append("Sideling");
			customInfo.Append(" to ");
			customInfo.AppendLine(location.ToString());

			customInfo.Append("Distance: ");
			customInfo.Append(PrettySI.makePretty(m_navSet.Settings_Current.Distance));
			customInfo.AppendLine("m");
		}

		#endregion

		#region INavigatorRotator Members

		/// <summary>
		/// Calculates the rotation to face the navigation block towards the target location.
		/// </summary>
		public void Rotate()
		{
			if (m_navSet.Settings_Current.Distance > 10)
			{
				Vector3 direction = location - NavigationBlock.WorldPosition;
				m_mover.CalcRotate(NavigationBlock, RelativeDirection3F.FromWorld(m_controlBlock.CubeGrid, direction));
			}
			else
				m_mover.StopRotate();
		}

		#endregion

	}
}
