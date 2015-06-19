#define LOG_ENABLED //remove on build

using Rynchodon.Autopilot.NavigationSettings;
using VRageMath;

namespace Rynchodon.Autopilot
{
	internal static class SpeedControl
	{
		public static void controlSpeed(Navigator nav)
		{
			Logger myLogger = new Logger(nav.myGrid.DisplayName, "SpeedControl");

			if (nav.CNS.isAMissile)
			{
				checkAndCruise(nav, myLogger);
				return;
			}

			switch (nav.CNS.moveState)
			{
				case NavSettings.Moving.MOVING:
				case NavSettings.Moving.SIDELING:
				case NavSettings.Moving.HYBRID:
					double distanceToDestination = nav.MM.distToWayDest;
					adjustSpeeds(nav, myLogger, distanceToDestination, 1f, 2f);
					adjustSpeedsByClosest(nav, myLogger);
					break;
			}
			checkAndCruise(nav, myLogger);
		}

		/// <summary>
		/// adjust cruise/slow speed based on current speed and distance to way/dest
		/// </summary>
		private static void adjustSpeeds(Navigator nav, Logger myLogger, double distanceToDestination, float stopMultiplierSlowDown, float stopMultiplierSpeedUp)
		{
			float stoppingDistance = nav.currentThrust.getStoppingDistance();

			if (distanceToDestination < stopMultiplierSlowDown * stoppingDistance) // distance is small
			{
				if (!(nav.currentMove == Vector3.Zero && nav.dampenersEnabled())) // not slowing down
				{
					float initialSpeedSlow = nav.CNS.getSpeedSlow();

					float speedSlow = (float)(nav.MM.movementSpeed * 0.9);
					nav.CNS.speedSlow_internal = speedSlow;
					nav.CNS.speedCruise_internal = speedSlow / 2;

					myLogger.debugLog("adjusting speeds (down). distanceToDestination = " + distanceToDestination + ", stoppingDistance = " + stoppingDistance
						+ ", speedSlow = " + speedSlow + ", speedCruise = " + speedSlow / 2, "adjustSpeeds()");
				}
			}
			else // distance is not small
				if (distanceToDestination > stopMultiplierSpeedUp * stoppingDistance) // distance is great
				{
					if (nav.currentMove == Vector3.Zero) // not speeding up
					{
						float initialSpeedCruise = nav.CNS.getSpeedCruise();

						float speedCruise = nav.MM.movementSpeed * 2;
						nav.CNS.speedCruise_internal = speedCruise;
						nav.CNS.speedSlow_internal = speedCruise * 2;

						myLogger.debugLog("adjusting speeds (up). distanceToDestination = " + distanceToDestination + ", stoppingDistance = " + stoppingDistance
							+ ", speedSlow = " + speedCruise * 2 + ", speedCruise = " + speedCruise, "adjustSpeeds()");
					}
				}
		}

		/// <summary>
		/// Adjust speeds for closest entity.
		/// </summary>
		private static void adjustSpeedsByClosest(Navigator nav, Logger myLogger)
		{
			if (nav.myPathfinder_Output == null)
			{
				myLogger.debugLog("No pathfinder output", "adjustSpeedsByClosest()");
				return;
			}
			double closestDistance = nav.myPathfinder_Output.DistanceToClosest;
			float cruiseSpeed = MathHelper.Max((float)closestDistance + 10f, 10f),
				slowSpeed = cruiseSpeed * 1.5f;
			if (nav.CNS.getSpeedSlow() > slowSpeed)
			{
				//myLogger.debugLog("slow speed is now " + slowSpeed, "adjustSpeedsByClosest()");
				nav.CNS.speedSlow_internal = slowSpeed;
			}
			if (nav.CNS.getSpeedCruise() > cruiseSpeed)
			{
				//myLogger.debugLog("cruise speed is now " + cruiseSpeed, "adjustSpeedsByClosest()");
				nav.CNS.speedCruise_internal = cruiseSpeed;
			}
		}

		/// <summary>
		/// call every run, needed to enable dampeners
		/// two options for cruise: use cruiseForward, or disable dampeners
		/// </summary>
		private static void checkAndCruise(Navigator nav, Logger myLogger) //float rotLengthSq)
		{
			myLogger.debugLog("entered checkAndCruise, speed=" + nav.MM.movementSpeed + ", slow=" + nav.CNS.getSpeedSlow() + ", cruise=" + nav.CNS.getSpeedCruise(), "checkAndCruise()");

			switch (nav.CNS.moveState)
			{
				case NavSettings.Moving.MOVING:
				case NavSettings.Moving.SIDELING:
				case NavSettings.Moving.HYBRID:
					break; // continue method
				default:
					if (nav.MM.movementSpeed > 1 && !nav.dampenersEnabled())
					{
						nav.EnableDampeners();
						myLogger.debugLog("wrong state(" + nav.CNS.moveState + "), enabling dampeners", "checkAndCruise()", Logger.severity.TRACE);
					}
					else
						myLogger.debugLog("wrong state: " + nav.CNS.moveState, "checkAndCruise()", Logger.severity.TRACE);
					return;
			}

			if (nav.MM.movementSpeed > nav.CNS.getSpeedSlow())
			{
				myLogger.debugLog("too fast, checking dampeners = " + nav.dampenersEnabled() + ", and current move = " + nav.currentMove, "checkAndCruise()", Logger.severity.TRACE);
				if (!nav.dampenersEnabled() || nav.currentMove != Vector3.Zero)
				{
					myLogger.debugLog("too fast(" + nav.CNS.getSpeedCruise() + " : " + nav.MM.movementSpeed + " : " + nav.CNS.getSpeedSlow() + "), slowing", "checkAndCruise()", Logger.severity.TRACE);
					nav.EnableDampeners();
					nav.moveOrder(Vector3.Zero);
				}
				return;
			}
			if (nav.MM.movementSpeed < nav.CNS.getSpeedCruise())
			{
				// as long as there is acceleration, do not calcAndMove
				//myLogger.debugLog("current move = " + nav.currentMove + ", moving too slow = " + nav.movingTooSlow, "checkAndCruise()", Logger.severity.TRACE);
				//myLogger.debugLog("too slow(" + nav.CNS.getSpeedCruise() + " : " + nav.MM.movementSpeed + " : " + nav.CNS.getSpeedSlow() + ")", "checkAndCruise()", Logger.severity.TRACE);
				if ((nav.currentMove == Vector3.Zero) && !nav.movingTooSlow)
				{
					myLogger.debugLog("too slow(" + nav.CNS.getSpeedCruise() + " : " + nav.MM.movementSpeed + " : " + nav.CNS.getSpeedSlow() + "), setting nav.movingTooSlow", "checkAndCruise()", Logger.severity.TRACE);
					nav.movingTooSlow = true;
				}
				return;
			}

			// between cruise and slow speed
			if (nav.dampenersEnabled() || nav.currentMove != Vector3.Zero)
			{
				myLogger.debugLog("speed is good(" + nav.CNS.getSpeedCruise() + " : " + nav.MM.movementSpeed + " : " + nav.CNS.getSpeedSlow() + "), disabling dampeners", "checkAndCruise()", Logger.severity.TRACE);
				nav.DisableReverseThrust();
				nav.moveOrder(Vector3.Zero);
				return;
			}

			myLogger.debugLog("no adjustments made", "checkAndCruise()", Logger.severity.TRACE);
		}
	}
}
