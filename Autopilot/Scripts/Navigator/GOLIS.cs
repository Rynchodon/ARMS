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
	public class GOLIS : ANavigator
	{

		private readonly Logger myLogger;
		private readonly DestinationPoint destination;
		private readonly float DestinationRadius;

		private double distance;

		public GOLIS(Mover mover, AllNavigationSettings CurNavSet, Vector3D location)
			: base(mover, CurNavSet)
		{
			this.myLogger = new Logger("GOLIS", mover.NavigationBlock);
			this.DestinationRadius = CurNavSet.CurrentSettings.DestinationRadius;
			this.destination = new DestinationPoint(location);
			if (mover.Destination == null)
			{
				myLogger.debugLog("setting destination point: " + this.destination.Point, "GOLIS()", Logger.severity.DEBUG);
				mover.Destination = this.destination;
			}
			if (mover.RotateDest == null)
			{
				myLogger.debugLog("setting destination rotation: " + this.destination.Point, "GOLIS()", Logger.severity.DEBUG);
				mover.RotateDest = this.destination;
			}

			myLogger.debugLog("created GOLIS", "GOLIS()", Logger.severity.INFO);
		}

		public override void PerformTask()
		{
			distance = Vector3D.Distance(_mover.Destination.Point, _mover.NavigationBlock.GetPosition());
			if (distance < DestinationRadius)
			{
				myLogger.debugLog("Reached destination: " + destination.Point, "PerformTask()", Logger.severity.INFO);
				_navSet.OnTaskComplete();
				_mover.StopDest(destination);
			}

			_mover.Update();
		}

		public override void AppendCustomInfo(StringBuilder customInfo)
		{
			customInfo.Append(ReportableState);
			customInfo.Append(" to ");
			customInfo.AppendLine(destination.Point.ToString());

			customInfo.Append("Distance: ");
			customInfo.Append(PrettySI.makePretty(distance));
			customInfo.AppendLine("m");
		}

		private string ReportableState
		{
			get
			{
				if (_mover.Destination == this.destination)
				{
					if (_mover.RotateDest == this.destination)
						return "Moving";
					return "Sideling";
				}
				if (_mover.RotateDest == this.destination)
					return "Rotating";

				throw new Exception("Mover is not using GOLIS destination");
			}
		}
	}
}
