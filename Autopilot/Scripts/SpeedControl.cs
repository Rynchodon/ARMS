#define LOG_ENABLED //remove on build

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

using Sandbox.Common;
//using Sandbox.Common.Components;
//using Sandbox.Common.ObjectBuilders;
//using Sandbox.Definitions;
//using Sandbox.Engine;
//using Sandbox.Game;
using Sandbox.ModAPI;
using Ingame = Sandbox.ModAPI.Ingame;
//using Sandbox.ModAPI.Interfaces;
using VRageMath;

namespace Rynchodon.Autopilot
{
	internal static class SpeedControl
	{
		[System.Diagnostics.Conditional("LOG_ENABLED")]
		private static void log(Logger logger, string toLog, string method = null, Logger.severity level = Logger.severity.DEBUG)
		{ alwaysLog(logger, toLog, method, level); }
		private static void alwaysLog(Logger logger, string toLog, string method = null, Logger.severity level = Logger.severity.DEBUG)
		{
			if (logger == null) logger = new Logger(null, "SpeedControl");
			logger.log(level, method, toLog);
		}

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
			//myLogger.debugLog("distanceToDestination = " + distanceToDestination + ", stoppingDistance = " + stoppingDistance + ", stopMultiplierSlowDown = " + stopMultiplierSlowDown + ", stopMultiplierSpeedUp = " + stopMultiplierSpeedUp, "adjustSpeeds()");

			if (distanceToDestination < stopMultiplierSlowDown * stoppingDistance) // distance is small
			{
				//myLogger.debugLog("already slowing down", "adjustSpeeds()");
				if (!(nav.currentMove == Vector3.Zero && nav.dampenersEnabled())) // not slowing down
				{
					float initialSpeedSlow = nav.CNS.getSpeedSlow();

					float speedSlow = (float)(nav.MM.movementSpeed * 0.9);
					nav.CNS.speedSlow_internal = speedSlow;
					nav.CNS.speedCruise_internal = speedSlow / 2;

					//if (nav.CNS.getSpeedSlow() < initialSpeedSlow) // have made a difference
					//log(myLogger, "reducing speeds (" + distanceToDestination + ", " + stoppingDistance + "), setting speed slow = " + nav.MM.movementSpeed * 0.9, "adjustSpeeds()", Logger.severity.TRACE);
				}
				//else
				//	myLogger.debugLog("already slowing down", "adjustSpeeds()");
			}
			else // distance is not small
				if (distanceToDestination > stopMultiplierSpeedUp * stoppingDistance) // distance is great
				{
					if (nav.currentMove == Vector3.Zero || nav.currentMove == cruiseForward) // not speeding up
					{
						float initialSpeedCruise = nav.CNS.getSpeedCruise();

						float speedCruise = nav.MM.movementSpeed * 2;
						nav.CNS.speedCruise_internal = speedCruise;
						nav.CNS.speedSlow_internal = speedCruise * 2;

						//if (nav.CNS.getSpeedCruise() > initialSpeedCruise) // have made a difference
						//log(myLogger, "increasing speeds (" + distanceToDestination + ", " + stoppingDistance + "), setting speed cruise = " + nav.MM.movementSpeed * 2, "adjustSpeeds()", Logger.severity.TRACE);
					}
				}

			//if (nav.CNS.moveState == NavSettings.Moving.HYBRID)
			//	log(myLogger, "distanceToDestination = " + distanceToDestination + ", slow at = " + stopMultiplierSlowDown * stoppingDistance + ", speed at = " + stopMultiplierSpeedUp * stoppingDistance, "adjustSpeeds()", Logger.severity.TRACE);
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

		public static readonly Vector3 cruiseForward = new Vector3(0, 0, -0.01); // 10 * observed minimum
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
					//case NavSettings.Moving.STOP_MOVE:
					break; // continue method
				default:
					if (nav.MM.movementSpeed > 1 && !nav.dampenersEnabled())
					{
						nav.EnableDampeners();
						log(myLogger, "wrong state(" + nav.CNS.moveState + "), enabling dampeners", "checkAndCruise()", Logger.severity.TRACE);
					}
					else
						myLogger.debugLog("wrong state: " + nav.CNS.moveState, "checkAndCruise()", Logger.severity.TRACE);
					return;
			}

			//if (nav.CNS.moveState == NavSettings.Moving.HYBRID)
			//	log(myLogger, "speed = " + nav.MM.movementSpeed + ", speed cruise = " + nav.CNS.getSpeedCruise() + ", speed slow = " + nav.CNS.getSpeedSlow(), "checkAndCruise()", Logger.severity.TRACE);

			if (nav.MM.movementSpeed > nav.CNS.getSpeedSlow())
			{
				myLogger.debugLog("too fast, checking dampeners = " + nav.dampenersEnabled() + ", and current move = " + nav.currentMove, "checkAndCruise()", Logger.severity.TRACE);
				if (!nav.dampenersEnabled() || nav.currentMove != Vector3.Zero)
				{
					log(myLogger, "too fast(" + nav.CNS.getSpeedCruise() + " : " + nav.MM.movementSpeed + " : " + nav.CNS.getSpeedSlow() + "), slowing", "checkAndCruise()", Logger.severity.TRACE);
					nav.EnableDampeners();
					nav.moveOrder(Vector3.Zero);
				}
				return;
			}
			if (nav.MM.movementSpeed < nav.CNS.getSpeedCruise())
			{
				// as long as there is acceleration, do not calcAndMove
				myLogger.debugLog("current move = " + nav.currentMove + ", moving too slow = " + nav.movingTooSlow, "checkAndCruise()", Logger.severity.TRACE);
				myLogger.debugLog("too slow(" + nav.CNS.getSpeedCruise() + " : " + nav.MM.movementSpeed + " : " + nav.CNS.getSpeedSlow() + ")", "checkAndCruise()", Logger.severity.TRACE);
				if ((nav.currentMove == Vector3.Zero || nav.currentMove == cruiseForward) && !nav.movingTooSlow)
				{
					log(myLogger, "too slow(" + nav.CNS.getSpeedCruise() + " : " + nav.MM.movementSpeed + " : " + nav.CNS.getSpeedSlow() + "), setting nav.movingTooSlow", "checkAndCruise()", Logger.severity.TRACE);
					nav.movingTooSlow = true;
					//nav.EnableDampeners();
				}
				return;
			}

			// between cruise and slow speed
			//if (nav.CNS.rotateState == NavSettings.Rotating.NOT_ROTA) // as long as state change comes after checkAndCruise, this will work
			//{
				if (nav.dampenersEnabled() || nav.currentMove != Vector3.Zero)
				{
					log(myLogger, "speed is good(" + nav.CNS.getSpeedCruise() + " : " + nav.MM.movementSpeed + " : " + nav.CNS.getSpeedSlow() + "), disabling dampeners", "checkAndCruise()", Logger.severity.TRACE);
					//// disable dampeners
					//nav.setDampeners(false);
					nav.DisableReverseThrust();
					nav.moveOrder(Vector3.Zero);
					return;
				}

				myLogger.debugLog("no adjustments made", "checkAndCruise()", Logger.severity.TRACE);
			//}
			//else
			//{
			//	// use cruise vector
			//	if (!nav.dampenersEnabled() || nav.currentMove != cruiseForward)
			//	{
			//		log(myLogger, "speed is good(" + nav.CNS.getSpeedCruise() + " : " + nav.MM.movementSpeed + " : " + nav.CNS.getSpeedSlow() + "), using cruise vector", "checkAndCruise()", Logger.severity.TRACE);
			//		nav.setDampeners();
			//		nav.moveOrder(cruiseForward, false);
			//	}
			//	return;
			//}
		}
	}
}
