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

		public GOLIS(Mover mover, AllNavigationSettings navSet, Vector3 location)
			: base(mover, navSet)
		{
			this.myLogger = new Logger("GOLIS", m_controlBlock.CubeBlock);
			this.NavigationBlock = _navSet.CurrentSettings.NavigationBlock;
			this.location = location;
			this.DestinationRadius = _navSet.CurrentSettings.DestinationRadius;

			var atLevel = _navSet.Settings_Task_Secondary;
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

		public override void Move()
		{
			distance = Vector3D.Distance(location, NavigationBlock.GetPosition());
			if (distance < DestinationRadius)
			{
				myLogger.debugLog("Reached destination: " + location, "PerformTask()", Logger.severity.INFO);
				_navSet.OnTaskSecondaryComplete();
				_mover.FullStop();
			}
			else
				_mover.CalcMove(NavigationBlock, location, Vector3.Zero);
		}

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

		#region INavigatorRotator Members

		void INavigatorRotator.Rotate()
		{
			if (distance > 100)
			{
				Vector3 direction = location - NavigationBlock.GetPosition();
				_mover.CalcRotate(NavigationBlock, RelativeDirection3F.FromWorld(m_controlBlock.CubeGrid, direction));
			}
			else
				_mover.StopRotate();
		}

		void INavigatorRotator.AppendCustomInfo(StringBuilder customInfo) { }

		#endregion

	}
}
