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
		private readonly IMyCubeBlock NavigationBlock;
		private readonly Vector3 location;
		private readonly float DestinationRadius;
		private readonly bool Rotating;

		private double distance;

		/// <summary>
		/// Creates a GOLIS
		/// </summary>
		/// <param name="mover">The mover to use</param>
		/// <param name="navSet">The settings to use</param>
		/// <param name="location">The location to fly to</param>
		public GOLIS(Mover mover, AllNavigationSettings navSet, Vector3 location)
			: base(mover, navSet)
		{
			this.myLogger = new Logger("GOLIS", m_controlBlock.CubeBlock);
			this.NavigationBlock = m_navSet.CurrentSettings.NavigationBlock;
			this.location = location;
			this.DestinationRadius = m_navSet.CurrentSettings.DestinationRadius;

			var atLevel = m_navSet.Settings_Task_Secondary;
			atLevel.NavigatorMover = this;
			if (atLevel.NavigatorRotator == null)
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
			distance = Vector3D.Distance(location, NavigationBlock.GetPosition());
			if (distance < DestinationRadius)
			{
				myLogger.debugLog("Reached destination: " + location, "PerformTask()", Logger.severity.INFO);
				m_navSet.OnTaskSecondaryComplete();
				m_mover.FullStop();
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
			customInfo.Append(PrettySI.makePretty(distance));
			customInfo.AppendLine("m");
		}

		#endregion

		#region INavigatorRotator Members

		/// <summary>
		/// Calculates the rotation to face the navigation block towards the target location.
		/// </summary>
		public void Rotate()
		{
			if (distance > 100)
			{
				Vector3 direction = location - NavigationBlock.GetPosition();
				m_mover.CalcRotate(RelativeDirection3F.FromWorld(m_controlBlock.CubeGrid, direction), NavigationBlock.LocalMatrix);
			}
			else
				m_mover.StopRotate();
		}

		#endregion

	}
}
