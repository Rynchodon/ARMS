using System;
using System.Collections.Generic;
using Rynchodon.Autopilot.Data;
using Sandbox.Game.Entities;
using Sandbox.ModAPI;
using VRageMath;

namespace Rynchodon.Autopilot.Movement
{
	/// <summary>
	/// Performs the movement calculations and the movements.
	/// </summary>
	public class Mover
	{
		#region S.E. Constants

		private const float pixelsForMaxRotation = 20;
		private const float RollControlMultiplier = 0.2f;

		#endregion

		/// <summary>Controlling block for the grid.</summary>
		public readonly ShipControllerBlock Block;

		private readonly Logger myLogger;
		private readonly AllNavigationSettings NavSet;

		private IMyCubeGrid myGrid;
		private ThrustProfiler myThrust;
		private GyroProfiler myGyro;

		private Vector3 moveForceRatio = Vector3.Zero;
		private Vector3 value_rotateForceRatio = Vector3.Zero;
		private Vector3 rotateForceRatio = Vector3.Zero;

		private ulong updated_prevAngleVel = 0;
		private Vector3 prevAngleVel = Vector3.Zero;
		private Vector3 prevAngleDisp = Vector3.Zero;

		private bool m_stopped;

		public Pathfinder.Pathfinder myPathfinder { get; private set; }

		/// <summary>
		/// Creates a Mover for a given ShipControllerBlock and AllNavigationSettings
		/// </summary>
		/// <param name="block">Controlling block for the grid</param>
		/// <param name="NavSet">Navigation settings to use.</param>
		public Mover(ShipControllerBlock block, AllNavigationSettings NavSet)
		{
			this.myLogger = new Logger("Mover", block.Controller);
			//this.myLogger.MinimumLevel = Logger.severity.DEBUG;
			this.Block = block;
			this.NavSet = NavSet;
		}

		/// <summary>
		/// Sets moveForceRatio to zero, optionally enables damping.
		/// </summary>
		public void StopMove(bool enableDampeners = true)
		{
			moveForceRatio = Vector3.Zero;
			Block.SetDamping(enableDampeners);
		}

		/// <summary>
		/// Sets rotateForceRatio to zero.
		/// </summary>
		public void StopRotate()
		{
			rotateForceRatio = Vector3.Zero;
		}

		public void MoveAndRotateStop()
		{
			if (m_stopped)
				return;

			moveForceRatio = Vector3.Zero;
			rotateForceRatio = Vector3.Zero;
			Block.SetDamping(true);
			MyAPIGateway.Utilities.TryInvokeOnGameThread(() => Block.Controller.MoveAndRotateStopped());
			m_stopped = true;
		}

		public bool CanMoveForward(PseudoBlock block)
		{
			CheckGrid();

			return myThrust.CanMoveAnyDirection();
		}

		/// <summary>
		/// Calculates the force necessary to move the grid.
		/// </summary>
		/// <param name="block">To get world position from.</param>
		/// <param name="destPoint">The world position of the destination</param>
		/// <param name="destVelocity">The speed of the destination</param>
		/// <param name="landing">Puts an emphasis on not overshooting the target.</param>
		public void CalcMove(PseudoBlock block, Vector3 destPoint, Vector3 destVelocity, bool landing = false)
		{
			CheckGrid();

			// using world vectors

			if (NavSet.Settings_Current.CollisionAvoidance)
			{
				myPathfinder.TestPath(destPoint, landing);
				if (!myPathfinder.CanMove)
				{
					myLogger.debugLog("Pathfinder not allowing movement", "CalcMove()");
					return;
				}
			}

			myThrust.UpdateGravity();

			Vector3 destDisp = destPoint - block.WorldPosition;
			Vector3 velocity = Block.CubeGrid.Physics.LinearVelocity;

			// switch to using local vectors

			Matrix positionToLocal = Block.CubeBlock.WorldMatrixNormalizedInv;
			Matrix directionToLocal = positionToLocal.GetOrientation();

			destDisp = Vector3.Transform(destDisp, directionToLocal);
			destVelocity = Vector3.Transform(destVelocity, directionToLocal);
			velocity = Vector3.Transform(velocity, directionToLocal);

			float distance = destDisp.Length();
			NavSet.Settings_Task_NavWay.Distance = distance;

			Vector3 targetVelocity = MaximumVelocity(destDisp) * 0.5f;

			// project targetVelocity onto destination direction (take shortest path)
			Vector3 destDir = destDisp / distance;
			targetVelocity = Vector3.Dot(targetVelocity, destDir) * destDir;

			// apply relative speed limit
			float relSpeedLimit = NavSet.Settings_Current.SpeedMaxRelative;
			if (landing)
			{
				float landingSpeed = distance * 0.5f;
				if (relSpeedLimit > landingSpeed)
					relSpeedLimit = landingSpeed;
			}
			if (relSpeedLimit < float.MaxValue)
			{
				float tarSpeedSq_1 = targetVelocity.LengthSquared();
				if (tarSpeedSq_1 > relSpeedLimit * relSpeedLimit)
				{
					targetVelocity *= relSpeedLimit / (float)Math.Sqrt(tarSpeedSq_1);
					myLogger.debugLog("imposing relative speed limit: " + relSpeedLimit + ", targetVelocity: " + targetVelocity, "CalcMove()");
				}
			}

			targetVelocity += destVelocity;

			// apply speed limit
			float tarSpeedSq = targetVelocity.LengthSquared();
			float speedRequest = NavSet.Settings_Current.SpeedTarget;
			if (tarSpeedSq > speedRequest * speedRequest)
				targetVelocity *= speedRequest / (float)Math.Sqrt(tarSpeedSq);

			Vector3 accel = targetVelocity - velocity;

			moveForceRatio = ToForceRatio(accel);

			// dampeners
			bool enableDampeners = false;
			for (int i = 0; i < 3; i++)
			{
				// if target velocity is close to 0, use dampeners

				float targetDim = targetVelocity.GetDim(i);
				if (targetDim < 0.1f && targetDim > -0.1f)
				{
					//myLogger.debugLog("close to 0, i: " + i + ", targetDim: " + targetDim, "CalcMove()");
					moveForceRatio.SetDim(i, 0);
					enableDampeners = true;
					continue;
				}

				// if there is not enough force available for braking, use dampeners

				float forceRatio = moveForceRatio.GetDim(i);
				if (forceRatio < 1f && forceRatio > -1f)
					continue;

				float velDim = velocity.GetDim(i);
				if (velDim < 1f && velDim > -1f)
					continue;

				if (Math.Sign(forceRatio) * Math.Sign(velDim) < 0)
				{
					myLogger.debugLog("damping, i: " + i + ", force ratio: " + forceRatio + ", velocity: " + velDim + ", sign of forceRatio: " + Math.Sign(forceRatio) + ", sign of velocity: " + Math.Sign(velDim), "CalcMove()");
					moveForceRatio.SetDim(i, 0);
					enableDampeners = true;
				}
				else
					myLogger.debugLog("not damping, i: " + i + ", force ratio: " + forceRatio + ", velocity: " + velDim + ", sign of forceRatio: " + Math.Sign(forceRatio) + ", sign of velocity: " + Math.Sign(velDim), "CalcMove()");
			}

			//if (enableDampeners)
			//	Logger.debugNotify("Damping", 16);

			Block.SetDamping(enableDampeners);

			myLogger.debugLog("destDisp: " + destDisp
				+ ", destDir: " + destDir
				+ ", destVelocity: " + destVelocity
				//+ ", relaVelocity: " + relaVelocity
				+ ", targetVelocity: " + targetVelocity
				//+ ", diffVel: " + diffVel
				+ ", accel: " + accel
				+ ", moveForceRatio: " + moveForceRatio, "CalcMove()");
		}

		/// <summary>
		/// Calculates the maximum velocity that will allow the grid to stop at the destination.
		/// </summary>
		/// <param name="localDisp">The displacement to the destination.</param>
		/// <returns>The maximum velocity that will allow the grid to stop at the destination.</returns>
		private Vector3 MaximumVelocity(Vector3 localDisp)
		{
			Vector3 result = Vector3.Zero;

			if (localDisp.X > 0)
				result.X = MaximumSpeed(localDisp.X, Base6Directions.Direction.Right);
			else if (localDisp.X < 0)
				result.X = -MaximumSpeed(-localDisp.X, Base6Directions.Direction.Left);
			if (localDisp.Y > 0)
				result.Y = MaximumSpeed(localDisp.Y, Base6Directions.Direction.Up);
			else if (localDisp.Y < 0)
				result.Y = -MaximumSpeed(-localDisp.Y, Base6Directions.Direction.Down);
			if (localDisp.Z > 0)
				result.Z = MaximumSpeed(localDisp.Z, Base6Directions.Direction.Backward);
			else if (localDisp.Z < 0)
				result.Z = -MaximumSpeed(-localDisp.Z, Base6Directions.Direction.Forward);

			myLogger.debugLog("local: " + localDisp + ", result: " + result, "MaximumVelocity()");

			return result;
		}

		/// <summary>
		/// Calculates the maximum speed that will allow the grid to stop at the destination.
		/// </summary>
		/// <param name="dist">The distance to the destination</param>
		/// <param name="direct">The directional thrusters to use</param>
		/// <returns>The maximum speed that will allow the grid to stop at the destination.</returns>
		private float MaximumSpeed(float dist, Base6Directions.Direction direct)
		{
			// Mover will attempt to stop with normal thrust
			float accel = -myThrust.GetForceInDirection(Base6Directions.GetFlippedDirection(direct)) / Block.Physics.Mass;
			myLogger.debugLog("dist: " + dist + ", accel: " + accel + ", max speed: " + PrettySI.makePretty(Math.Sqrt(-2 * accel * dist)) + "m/s" + ", cap: " + dist * 0.5f + " m/s", "MaximumSpeed()");
			return Math.Min((float)Math.Sqrt(-2 * accel * dist), dist * 0.5f); // capped for the sake of autopilot's reaction time
		}

		/// <summary>
		/// Calculate the force ratio from acceleration.
		/// </summary>
		/// <param name="localAccel">Acceleration</param>
		/// <returns>Force ratio</returns>
		private Vector3 ToForceRatio(Vector3 localAccel)
		{
			Vector3 result = Vector3.Zero;

			if (localAccel.X > 0)
				result.X = localAccel.X * Block.Physics.Mass / myThrust.GetForceInDirection(Base6Directions.Direction.Right);
			else if (localAccel.X < 0)
				result.X = localAccel.X * Block.Physics.Mass / myThrust.GetForceInDirection(Base6Directions.Direction.Left);
			if (localAccel.Y > 0)
				result.Y = localAccel.Y * Block.Physics.Mass / myThrust.GetForceInDirection(Base6Directions.Direction.Up);
			else if (localAccel.Y < 0)
				result.Y = localAccel.Y * Block.Physics.Mass / myThrust.GetForceInDirection(Base6Directions.Direction.Down);
			if (localAccel.Z > 0)
				result.Z = localAccel.Z * Block.Physics.Mass / myThrust.GetForceInDirection(Base6Directions.Direction.Backward);
			else if (localAccel.Z < 0)
				result.Z = localAccel.Z * Block.Physics.Mass / myThrust.GetForceInDirection(Base6Directions.Direction.Forward);

			myLogger.debugLog("local: " + localAccel + ", result: " + result + ", after gravity: " + (result + myThrust.m_gravityReactRatio), "ToForceRatio()");

			result += myThrust.m_gravityReactRatio;
			return result;
		}

		/// <summary>
		/// Match orientation with the target block.
		/// </summary>
		/// <param name="block">The navigation block</param>
		/// <param name="destBlock">The destination block</param>
		/// <param name="forward">The direction of destBlock that will be matched to navigation block's forward</param>
		/// <param name="upward">The direction of destBlock that will be matched to navigation block's upward</param>
		/// <returns>True iff localMatrix is facing the same direction as destBlock's forward</returns>
		public void CalcRotate(PseudoBlock block, IMyCubeBlock destBlock, Base6Directions.Direction? forward, Base6Directions.Direction? upward)
		{
			if (forward == null)
				forward = Base6Directions.Direction.Forward;
			//if (upward == null)
			//	upward = Base6Directions.Direction.Up;

			//myLogger.debugLog(forward.Value == upward.Value || forward.Value == Base6Directions.GetFlippedDirection(upward.Value),
			//	"Invalid orienation: " + forward + ", " + upward, "CalcRotate()", Logger.severity.FATAL);

			RelativeDirection3F faceForward = RelativeDirection3F.FromWorld(block.Grid, destBlock.WorldMatrix.GetDirectionVector(forward.Value));
			RelativeDirection3F faceUpward = upward.HasValue ? RelativeDirection3F.FromWorld(block.Grid, destBlock.WorldMatrix.GetDirectionVector(upward.Value)) : null;

			CalcRotate(block, faceForward, faceUpward);
		}

		/// <summary>
		/// Calculates the force necessary to rotate the grid.
		/// </summary>
		/// <param name="Direction">The direction to face the localMatrix in.</param>
		/// <param name="block"></param>
		/// <returns>True iff localMatrix is facing Direction</returns>
		public void CalcRotate(PseudoBlock block, RelativeDirection3F Direction, RelativeDirection3F UpDirect = null)
		{
			Vector3 angleVelocity;
			CalcRotate(block.LocalMatrix, Direction, UpDirect, out angleVelocity);
			updated_prevAngleVel = Globals.UpdateCount;
			prevAngleVel = angleVelocity;

			//myLogger.debugLog("displacement.LengthSquared(): " + displacement.LengthSquared(), "CalcRotate()");
			//return displacement.LengthSquared() < 0.01f;

			//if (NavSet.Settings_Current.CollisionAvoidance)
			//	myPathfinder.TestRotate(displacement);
		}

		/// <summary>
		/// Calculates the force necessary to rotate the grid.
		/// </summary>
		/// <param name="localMatrix">The matrix to rotate to face the direction, use a block's local matrix or result of GetMatrix()</param>
		/// <param name="Direction">The direction to face the localMatrix in.</param>
		/// <param name="angularVelocity">The local angular velocity of the controlling block.</param>
		private void CalcRotate(Matrix localMatrix, RelativeDirection3F Direction, RelativeDirection3F UpDirect, out Vector3 angularVelocity)
		{
			CheckGrid();

			myLogger.debugLog(Direction == null, "Direction == null", "CalcRotate()", Logger.severity.ERROR);

			angularVelocity = -Vector3.Transform(Block.Physics.AngularVelocity, Block.CubeBlock.WorldMatrixNormalizedInv.GetOrientation());

			//myLogger.debugLog("angular: " + angularVelocity, "CalcRotate()");
			float gyroForce = myGyro.TotalGyroForce();

			const ulong MaxUpdateFrequency = ShipController_Autopilot.UpdateFrequency * 2u;
			if (rotateForceRatio != Vector3.Zero)
			{
				ulong timeSinceLast = Globals.UpdateCount - updated_prevAngleVel;
				if (timeSinceLast <= MaxUpdateFrequency)
				{
					Vector3 ratio = (angularVelocity - prevAngleVel) / (rotateForceRatio * gyroForce * (float)timeSinceLast);

					//myLogger.debugLog("rotateForceRatio: " + rotateForceRatio + ", ratio: " + ratio + ", accel: " + (angularVelocity - prevAngleVel) + ", torque: " + (rotateForceRatio * gyroForce), "CalcRotate()");

					myGyro.Update_torqueAccelRatio(rotateForceRatio, ratio);
				}
				else
					myLogger.debugLog("prevAngleVel is old: " + (Globals.UpdateCount - updated_prevAngleVel), "CalcRotate()", Logger.severity.DEBUG);
			}

			localMatrix.M41 = 0; localMatrix.M42 = 0; localMatrix.M43 = 0; localMatrix.M44 = 1;
			Matrix inverted; Matrix.Invert(ref localMatrix, out inverted);

			localMatrix = localMatrix.GetOrientation();
			inverted = inverted.GetOrientation();

			//myLogger.debugLog("local matrix: right: " + localMatrix.Right + ", up: " + localMatrix.Up + ", back: " + localMatrix.Backward + ", trans: " + localMatrix.Translation, "CalcRotate()");
			//myLogger.debugLog("inverted matrix: right: " + inverted.Right + ", up: " + inverted.Up + ", back: " + inverted.Backward + ", trans: " + inverted.Translation, "CalcRotate()");
			//myLogger.debugLog("local matrix: " + localMatrix, "CalcRotate()");
			//myLogger.debugLog("inverted matrix: " + inverted, "CalcRotate()");

			Vector3 localDirect = Direction.ToLocal();
			Vector3 rotBlockDirect; Vector3.Transform(ref localDirect, ref inverted, out rotBlockDirect);

			rotBlockDirect.Normalize();

			float azimuth, elevation; Vector3.GetAzimuthAndElevation(rotBlockDirect, out azimuth, out elevation);

			Vector3 rotaRight = localMatrix.Right;
			Vector3 rotaUp = localMatrix.Up;

			Vector3 NFR_right = Base6Directions.GetVector(Block.CubeBlock.LocalMatrix.GetClosestDirection(ref rotaRight));
			Vector3 NFR_up = Base6Directions.GetVector(Block.CubeBlock.LocalMatrix.GetClosestDirection(ref rotaUp));

			Vector3 displacement = -elevation * NFR_right - azimuth * NFR_up;

			if (UpDirect != null)
			{
				Vector3 upLocal = UpDirect.ToLocal();
				Vector3 upRotBlock; Vector3.Transform(ref upLocal, ref inverted, out upRotBlock);
				float roll; Vector3.Dot(ref upRotBlock, ref Vector3.Right, out roll);

				Vector3 rotaBackward = localMatrix.Backward;
				Vector3 NFR_backward = Base6Directions.GetVector(Block.CubeBlock.LocalMatrix.GetClosestDirection(ref rotaBackward));

				myLogger.debugLog("roll: " + roll + ", displacement: " + displacement + ", NFR_backward: " + NFR_backward + ", change: " + (-roll * NFR_backward), "CalcRotate()");

				displacement += roll * NFR_backward;
			}

			NavSet.Settings_Task_NavWay.DistanceAngle = displacement.Length();

			if (NavSet.Settings_Current.CollisionAvoidance)
			{
				myPathfinder.TestRotate(displacement);
				if (!myPathfinder.CanRotate)
				{
					Logger.debugNotify("Cannot Rotate", 50);
					myLogger.debugLog("Pathfinder not allowing rotation", "CalcRotate()");
					return;
				}
			}

			//myLogger.debugLog("localDirect: " + localDirect + ", rotBlockDirect: " + rotBlockDirect + ", elevation: " + elevation + ", NFR_right: " + NFR_right + ", azimuth: " + azimuth + ", NFR_up: " + NFR_up + ", disp: " + displacement, "CalcRotate()");

			if (myGyro.torqueAccelRatio == 0)
			{
				// do a test
				myLogger.debugLog("torqueAccelRatio == 0", "CalcRotate()");
				rotateForceRatio = new Vector3(0, 1f, 0);
				return;
			}

			Vector3 targetVelocity = MaxAngleVelocity(displacement);

			// Adjust for moving target by measuring changes in displacement. Part of the change in displacement is attributable to ship rotation. 
			const float dispToVel = (float)Globals.UpdatesPerSecond / (float)ShipController_Autopilot.UpdateFrequency;

			Vector3 addVelocity = angularVelocity - (prevAngleDisp - displacement) * dispToVel;
			if (addVelocity.LengthSquared() < 0.01f)
			{
				myLogger.debugLog("Adjust for moving, adding to target velocity: " + addVelocity, "CalcRotate()");
				targetVelocity += addVelocity;
			}
			else
				myLogger.debugLog("Not adjusting for moving, assuming target changed: " + addVelocity, "CalcRotate()");
			prevAngleDisp = displacement;

			Vector3 diffVel = targetVelocity - angularVelocity;

			rotateForceRatio = diffVel / (myGyro.torqueAccelRatio * gyroForce);

			myLogger.debugLog("targetVelocity: " + targetVelocity + ", angularVelocity: " + angularVelocity + ", diffVel: " + diffVel, "CalcRotate()");
			//myLogger.debugLog("diffVel: " + diffVel + ", torque: " + (myGyro.torqueAccelRatio * gyroForce) + ", rotateForceRatio: " + rotateForceRatio, "CalcRotate()");

			// dampeners
			for (int i = 0; i < 3; i++)
			{
				// if targetVelocity is close to 0, use dampeners

				float target = targetVelocity.GetDim(i);
				if (target > -0.01f && target < 0.01f)
				{
					myLogger.debugLog("target near 0 for " + i + ", " + target, "CalcRotate()");
					rotateForceRatio.SetDim(i, 0f);
					continue;
				}

				float velDim = angularVelocity.GetDim(i);
				if (velDim < 0.01f && velDim > -0.01f)
					continue;

				// where rotateForceRatio opposes angularVelocity, use dampeners

				float dim = rotateForceRatio.GetDim(i);
				if (Math.Sign(dim) * Math.Sign(angularVelocity.GetDim(i)) < 0)
					//{
					//	myLogger.debugLog("force ratio(" + dim + ") opposes velocity(" + angularVelocity.GetDim(i) + "), index: " + i + ", " + Math.Sign(dim) + ", " + Math.Sign(angularVelocity.GetDim(i)), "CalcRotate()");
					rotateForceRatio.SetDim(i, 0f);
				//}
				//else
				//	myLogger.debugLog("force ratio is aligned with velocity: " + i + ", " + Math.Sign(dim) + ", " + Math.Sign(angularVelocity.GetDim(i)), "CalcRotate()");
			}
		}

		/// <summary>
		/// Calulates the maximum angular velocity to stop at the destination.
		/// </summary>
		/// <param name="disp">Displacement to the destination.</param>
		/// <returns>The maximum angular velocity to stop at the destination.</returns>
		private Vector3 MaxAngleVelocity(Vector3 disp)
		{
			Vector3 result = Vector3.Zero;

			// S.E. provides damping for angular motion, we will ignore this
			float accel = -myGyro.torqueAccelRatio * myGyro.TotalGyroForce();

			//myLogger.debugLog("torqueAccelRatio: " + myGyro.torqueAccelRatio + ", TotalGyroForce: " + myGyro.TotalGyroForce() + ", accel: " + accel, "MaxAngleVelocity()");

			//myLogger.debugLog("speed cap: " + speedCap, "MaxAngleVelocity()");

			for (int i = 0; i < 3; i++)
			{
				float dim = disp.GetDim(i);
				if (dim > 0)
					//result.SetDim(i, Math.Min(MaxAngleSpeed(accel, dim), speedCap));
					result.SetDim(i, MaxAngleSpeed(accel, dim));
				else if (dim < 0)
					//result.SetDim(i, -Math.Min(MaxAngleSpeed(accel, -dim), speedCap));
					result.SetDim(i, -MaxAngleSpeed(accel, -dim));
			}

			return result;
		}

		/// <summary>
		/// Calculates the maximum angular speed to stop at the destination.
		/// </summary>
		/// <param name="accel">negative number, maximum deceleration</param>
		/// <param name="dist">positive number, distance to travel</param>
		/// <returns>The maximum angular speed to stop the destination</returns>
		private float MaxAngleSpeed(float accel, float dist)
		{
			//myLogger.debugLog("accel: " + accel + ", dist: " + dist, "MaxAngleSpeed()");
			return Math.Min((float)Math.Sqrt(-2 * accel * dist), dist); // capped for the sake of autopilot's reaction time
		}

		/// <summary>
		/// Apply the calculated force ratios to the controller.
		/// </summary>
		public void MoveAndRotate()
		{
			CheckGrid();

			if (NavSet.Settings_Current.CollisionAvoidance)
			{
				if (!myPathfinder.CanMove)
				{
					myLogger.debugLog("Pathfinder not allowing movement", "MoveAndRotate()");
					StopMove();
				}
				if (!myPathfinder.CanRotate)
				{
					myLogger.debugLog("Pathfinder not allowing rotation", "MoveAndRotate()");
					StopRotate();
				}
			}

			myLogger.debugLog("moveForceRatio: " + moveForceRatio + ", rotateForceRatio: " + rotateForceRatio + ", move length: " + moveForceRatio.Length(), "MoveAndRotate()");
			MyShipController controller = Block.Controller;

			// if all the force ratio values are 0, Autopilot has to stop the ship, MoveAndRotate will not
			if (moveForceRatio == Vector3.Zero && rotateForceRatio == Vector3.Zero)
			{
				MoveAndRotateStop();
				return;
			}

			// clamp values and invert operations MoveAndRotate will perform
			Vector3 moveControl; moveForceRatio.ApplyOperation(dim => MathHelper.Clamp(dim, -1, 1), out moveControl);

			Vector2 rotateControl = Vector2.Zero;
			rotateControl.X = MathHelper.Clamp(rotateForceRatio.X, -1, 1) * pixelsForMaxRotation;
			rotateControl.Y = MathHelper.Clamp(rotateForceRatio.Y, -1, 1) * pixelsForMaxRotation;

			float rollControl = MathHelper.Clamp(rotateForceRatio.Z, -1, 1) / RollControlMultiplier;

			//myLogger.debugLog("moveControl: " + moveControl + ", rotateControl: " + rotateControl + ", rollControl: " + rollControl, "MoveAndRotate()");

			m_stopped = false;
			MyAPIGateway.Utilities.TryInvokeOnGameThread(() => controller.MoveAndRotate(moveControl, rotateControl, rollControl), myLogger);
		}

		private void CheckGrid()
		{
			if (myGrid != Block.CubeGrid)
			{
				myLogger.debugLog(myGrid != null, "Grid Changed!", "CheckGrid()", Logger.severity.INFO);
				myGrid = Block.CubeGrid;
				this.myThrust = new ThrustProfiler(myGrid);
				this.myGyro = new GyroProfiler(myGrid);
				this.myPathfinder = new Pathfinder.Pathfinder(myGrid, NavSet, this);
			}
		}

	}
}
