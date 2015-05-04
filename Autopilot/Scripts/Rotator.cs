#define LOG_ENABLED //remove on build

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

using Sandbox.Common;
//using Sandbox.Common.Components;
using Sandbox.Common.ObjectBuilders;
//using Sandbox.Definitions;
//using Sandbox.Engine;
//using Sandbox.Game;
using Sandbox.ModAPI;
using Ingame = Sandbox.ModAPI.Ingame;
using Sandbox.ModAPI.Interfaces;
using VRageMath;

namespace Rynchodon.Autopilot
{
	internal class Rotator
	{
		private class RotateProfile
		{
			/// <summary>
			/// adjust decelPortion by
			/// </summary>
			private const float
				adjustUp = 1.1f,
				adjustDown = 0.9f;

			/// <summary>
			/// multiplier for rotate
			/// </summary>
			public float power { get; private set; }
			/// <summary>
			/// how much of rotate should be deceleration
			/// </summary>
			public float decelPortion { get; private set; }

			private static Random myRandom = new Random();

			public RotateProfile(float power, float stopAfter)
			{ this.power = power; this.decelPortion = stopAfter; }

			public void adjust(bool over)
			{
				if (myRandom.Next(2) == 0)
					adjustDecel(over);
				else
					adjustPower(over);
			}

			private void adjustDecel(bool over)
			{
				if (over)
					decelPortion *= adjustUp;
				else
					decelPortion *= adjustDown;
			}

			private void adjustPower(bool over)
			{
				if (over)
					power *= adjustDown;
				else
					power *= adjustUp;
			}
		}

		/// <summary>
		/// minimum component (2°)
		/// </summary>
		public const float rotComp_minimum = 0.0349f;

		///// <summary>
		///// stop and rotate when greater than (90°)
		///// </summary>
		//public const float rotLenSq_stopAndRot = 2.47f;

		/// <summary>stop and rotate when greater than</summary>
		public float rotLenSq_stopAndRot
		{
			get
			{
				if (CNS.isAMissile)
					return 2.47f; // 90°
				else
					return 0.685f; // 15°
			}
		}

		private RotateProfile
			stoppedRotate = new RotateProfile(1.0f, 1f / 2f),
			stoppedRoll = new RotateProfile(1.0f, 1f / 2f),
			inflightRotate = new RotateProfile(1.0f, 1f / 4f);

		private float current_pitch, current_yaw, current_roll;
		private float needToRotate_pitch, needToRotate_yaw, needToRotate_roll;
		
		private float previous_roll;

		private Logger myLogger;
		private Navigator owner;
		private NavSettings CNS { get { return owner.CNS; } }
		private MovementMeasure CMM { get { return owner.MM; } }

		public Rotator(Navigator owner)
		{
			this.owner = owner;
			myLogger = new Logger("Rotator", () => this.owner.myGrid.DisplayName, () => this.owner.CNS.moveState.ToString(), () => this.owner.CNS.rotateState.ToString());
		}

		/// <summary>
		/// calculates the required rotation and executes the rotation order
		/// </summary>
		public void calcAndRotate()
		{
			switch (CNS.rotateState)
			{
				case NavSettings.Rotating.NOT_ROTA:
					//myLogger.debugLog("entered NOT_ROTA, pitch = " + CMM.pitch + ", yaw = " + CMM.yaw, "calcAndRotate()");
					switch (CNS.landingState)
					{
						case NavSettings.LANDING.OFF:
						case NavSettings.LANDING.ORIENT:
							break;
						default:
							//myLogger.debugLog("Cannot rotate, landing > ORIENT", "calcAndRotate()");
							return;
					}

					switch (CNS.moveState)
					{
						case NavSettings.Moving.MOVING:
							//myLogger.debugLog("CMM.rotLenSq = " + CMM.rotLenSq + ", rotLenSq_stopAndRot = " + rotLenSq_stopAndRot, "calcAndRotate()");
							if (CMM.rotLenSq > rotLenSq_stopAndRot)
							{
								owner.fullStop("stopping to rotate");
								return;
							}
							else
								goto case NavSettings.Moving.HYBRID;
						case NavSettings.Moving.NOT_MOVE:
							//if (!owner.PathfinderAllowsMovement)
							//{
							//	myLogger.debugLog("waiting for collision updates", "calcAndRotate()");
							//	return;
							//}
							owner.reportState(Navigator.ReportableState.ROTATING);
							goto case NavSettings.Moving.HYBRID;
						case NavSettings.Moving.HYBRID:
							needToRotate_pitch = (float)CMM.pitch;
							needToRotate_yaw = (float)CMM.yaw;
							if (Math.Abs(needToRotate_pitch) < rotComp_minimum && Math.Abs(needToRotate_yaw) < rotComp_minimum)
							{
								//myLogger.debugLog("needToRotate_pitch = " + needToRotate_pitch + ", rotComp_minimum = " + rotComp_minimum + ", needToRotate_yaw = " + needToRotate_yaw + ", rotComp_minimum = " + rotComp_minimum, "calcAndRotate()");
								return;
							}
							myLogger.debugLog("rotate by " + needToRotate_pitch + " & " + needToRotate_yaw, "calcAndRotate()");
							addToRotate((float)CMM.pitch, (float)CMM.yaw);
							CNS.rotateState = NavSettings.Rotating.ROTATING;
							return;
						default:
							return;
					}
				case NavSettings.Rotating.ROTATING:
					//myLogger.debugLog("entered ROTATING, pitch = " + CMM.pitch + ", yaw = " + CMM.yaw, "calcAndRotate()");
					float whichDecel = currentRotateProfile().decelPortion;
					if (needToRotate_pitch > rotComp_minimum && CMM.pitch < needToRotate_pitch * whichDecel)
					{
						myLogger.debugLog("decelerate rotation, first case " + CMM.pitch + " < " + (needToRotate_pitch * whichDecel), "calcAndRotate()");
						stopRotateRoll();
						return;
					}
					if (needToRotate_pitch < -rotComp_minimum && CMM.pitch > needToRotate_pitch * whichDecel)
					{
						myLogger.debugLog("decelerate rotation, second case " + CMM.pitch + " > " + (needToRotate_pitch * whichDecel), "calcAndRotate()");
						stopRotateRoll();
						return;
					}
					if (needToRotate_yaw > rotComp_minimum && CMM.yaw < needToRotate_yaw * whichDecel)
					{
						myLogger.debugLog("decelerate rotation, third case " + CMM.yaw + " < " + (needToRotate_yaw * whichDecel), "calcAndRotate()");
						stopRotateRoll();
						return;
					}
					if (needToRotate_yaw < -rotComp_minimum && CMM.yaw > needToRotate_yaw * whichDecel)
					{
						myLogger.debugLog("decelerate rotation, fourth case " + CMM.yaw + " > " + (needToRotate_yaw * whichDecel), "calcAndRotate()");
						stopRotateRoll();
						return;
					}

					// no need to decel, accelerate! This help keeps ships from becoming stuck and causes low-gyro ship to, ultimately, rotate much faster
					addToRotate((float)CMM.pitch, (float)CMM.yaw);
					myLogger.debugLog("increased rotation power by " + CMM.pitch + ", " + CMM.yaw + ", to " + current_pitch + ", " + current_yaw, "calcAndRotate()");
					return;
				case NavSettings.Rotating.STOP_ROTA:
					//myLogger.debugLog("entered STOP_ROTA, pitch = " + CMM.pitch + ", yaw = " + CMM.yaw, "calcAndRotate()");
					if (isRotating())
						return;
					CNS.rotateState = NavSettings.Rotating.NOT_ROTA;
					myLogger.debugLog("no longer rotating", "calcAndRotate()");

					int overUnder = 0;
					overUnder += testOverUnder((float)CMM.pitch, needToRotate_pitch);
					overUnder += testOverUnder((float)CMM.yaw, needToRotate_yaw);
					if (overUnder != 0)
						currentRotateProfile().adjust(overUnder > 0);

					return;
			}
		}

		/// <summary>
		/// calculates the required roll and executes the roll order
		/// </summary>
		/// <remarks>
		/// does not test state for move or rotate
		/// </remarks>
		public void calcAndRoll(float roll)
		{
			switch (CNS.rollState)
			{
				case NavSettings.Rolling.NOT_ROLL:
					myLogger.debugLog("rollin' rollin' rollin' " + roll, "calcAndRoll()", Logger.severity.DEBUG);
					needToRotate_roll = roll;
					addToRoll(roll);
					CNS.rollState = NavSettings.Rolling.ROLLING;
					owner.reportState(Navigator.ReportableState.ROTATING);
					return;
				case NavSettings.Rolling.ROLLING:
					if (Math.Sign(roll) != Math.Sign(needToRotate_roll) || Math.Abs(roll) < Math.Abs(needToRotate_roll) * stoppedRoll.decelPortion)
					{
						myLogger.debugLog("decelerate roll, roll=" + roll + ", needToRoll=" + needToRotate_roll, "calcAndRoll()", Logger.severity.DEBUG);
						CNS.rollState = NavSettings.Rolling.STOP_ROLL;
						//previous_roll = roll;
						stopRotateRoll();
						return;
					}
					addToRoll(roll);
					myLogger.debugLog("increase roll power by " + roll + ", " + current_roll, "calcAndRoll()");
					return;
				case NavSettings.Rolling.STOP_ROLL:
					if (isRotating())
					{
						//previous_roll = roll;
						return;
					}
					CNS.rollState = NavSettings.Rolling.NOT_ROLL;
					myLogger.debugLog("get off the log", "calcAndRoll()", Logger.severity.DEBUG);

					int overUnder = testOverUnder(roll, needToRotate_roll);
					if (overUnder != 0)
						stoppedRoll.adjust(overUnder > 0);

					return;
			}
		}

		#region Private Methods

		#region Calc & Rotate Sub

		/// <summary>
		/// if moveState is moving or hybrid, inflightRotate. otherwise, stoppedRotate
		/// </summary>
		private RotateProfile currentRotateProfile()
		{
			switch (CNS.moveState)
			{
				case NavSettings.Moving.MOVING:
				case NavSettings.Moving.HYBRID:
					return inflightRotate;
				default:
					return stoppedRotate;
			}
		}

		/// <summary>
		/// tests for over/under roation/roll
		/// </summary>
		/// <param name="currentRotate">current rotate/roll (from target direction)</param>
		/// <param name="needToRotate">rotate/roll when rotation/rolling started</param>
		/// <returns>-1 if under, 1 if over, 0 if very close</returns>
		private int testOverUnder( float currentRotate, float needToRotate)
		{
			if (Math.Abs(currentRotate) > rotComp_minimum && Math.Abs(needToRotate) > rotComp_minimum)
				if (Math.Sign(currentRotate) == Math.Sign(needToRotate))
					return -1; // under rotated
				else
					return 1; // over rotated
			return 0; // rotated accurately
		}

		/// <summary>
		/// is the grid rotating
		/// </summary>
		private bool isRotating()
		{
			//myLogger.debugLog("Angular Velocity = " + owner.myGrid.Physics.AngularVelocity + ", length squared = " + owner.myGrid.Physics.AngularVelocity.LengthSquared(), "isRotating()");
			return owner.myGrid.Physics.AngularVelocity != Vector3.Zero;
		}

		#endregion

		#region Rotate & Roll Methods

		/// <summary>
		/// adds values to current rotation and executes rotation
		/// </summary>
		private void addToRotate(float addPitch, float addYaw)
		{
			CNS.rotateState = NavSettings.Rotating.ROTATING;
			current_pitch += addPitch;
			current_yaw += addYaw;
			performRotateRoll();
		}

		/// <summary>
		/// adds values to current roll and executes roll
		/// </summary>
		private void addToRoll(float addRoll)
		{
			CNS.rotateState = NavSettings.Rotating.ROTATING;
			current_roll += addRoll;
			performRotateRoll();
		}

		/// <summary>
		/// stop rotating and rolling
		/// </summary>
		private void stopRotateRoll()
		{
			CNS.rotateState = NavSettings.Rotating.STOP_ROTA;
			current_pitch = 0;
			current_yaw = 0;
			current_roll = 0;
			performRotateRoll();
		}

		/// <summary>
		/// executes rotation for current_pitch and current_yaw and roll for current_roll by passing to Navigator
		/// </summary>
		private void performRotateRoll()
		{
			owner.currentRotate = new Vector2(current_pitch, current_yaw);
			owner.currentRoll = current_roll;
			owner.moveAndRotate();
		}

		#endregion

		#endregion
	}
}
