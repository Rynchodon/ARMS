using System;
using Rynchodon.Autopilot.Data;
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
		private readonly ThrustProfiler myThrust;
		private readonly GyroProfiler myGyro;
		private readonly AllNavigationSettings NavSet;

		private Vector3 moveForceRatio = Vector3.Zero;
		private Vector3 value_rotateForceRatio = Vector3.Zero;
		private Vector3 rotateForceRatio = Vector3.Zero;

		private ulong updated_prevAngleVel = 0;
		private Vector3 prevAngleVel = Vector3.Zero;

		/// <summary>
		/// Creates a Mover for a given ShipControllerBlock and AllNavigationSettings
		/// </summary>
		/// <param name="block">Controlling block for the grid</param>
		/// <param name="NavSet">Navigation settings to use.</param>
		public Mover(ShipControllerBlock block, AllNavigationSettings NavSet)
		{
			this.myLogger = new Logger("Mover", block.Controller);
			this.Block = block;
			this.myThrust = new ThrustProfiler(block.Controller.CubeGrid);
			this.myGyro = new GyroProfiler(block.Controller.CubeGrid);
			this.NavSet = NavSet;
		}

		/// <summary>
		/// Stops the grid, including rotation, immediately.
		/// </summary>
		public void FullStop()
		{
			myLogger.debugLog("stopping", "FullStop()");

			moveForceRatio = Vector3.Zero;
			rotateForceRatio = Vector3.Zero;

			Block.Controller.MoveAndRotateStopped();
			Block.Controller.SetDamping(true);
		}

		/// <summary>
		/// Sets this Mover to stop when MoveAndRotate() is invoked.
		/// </summary>
		public void StopMove()
		{
			moveForceRatio = Vector3.Zero;
		}

		/// <summary>
		/// Sets this Mover to stop rotating when MoveAndRotate() is invoked.
		/// </summary>
		public void StopRotate()
		{
			rotateForceRatio = Vector3.Zero;
		}

		// genius or madness? time will tell...
		/// <summary>
		/// Calculates the force necessary to move the grid.
		/// </summary>
		/// <param name="NavigationBlock">The block to bring to the destination</param>
		/// <param name="destPoint">The world position of the destination</param>
		/// <param name="destVelocity">The speed of the destination</param>
		public void CalcMove(IMyCubeBlock NavigationBlock, Vector3 destPoint, Vector3 destVelocity)
		{
			if (!MyAPIGateway.Multiplayer.IsServer)
				return;

			// using world vectors

			Vector3 destDisp = destPoint - NavigationBlock.GetPosition();
			Vector3 velocity = NavigationBlock.GetLinearVelocity();

			Vector3 relaVelocity = velocity;
			if (destVelocity.LengthSquared() > 25f)
				relaVelocity -= destVelocity;

			// switch to using local vectors

			Matrix positionToLocal = Block.CubeBlock.WorldMatrixNormalizedInv;
			Matrix directionToLocal = positionToLocal.GetOrientation();

			destDisp = Vector3.Transform(destDisp, directionToLocal);
			relaVelocity = Vector3.Transform(relaVelocity, directionToLocal);
			velocity = Vector3.Transform(velocity, directionToLocal);

			Vector3 targetVelocity = MaximumVelocity(destDisp);

			float tarSpeedSq = targetVelocity.LengthSquared();
			float speedRequest = NavSet.CurrentSettings.SpeedTarget;
			if (tarSpeedSq > speedRequest * speedRequest)
				targetVelocity *= speedRequest / (float)Math.Sqrt(tarSpeedSq);

			// set accel to differnce between velocites, reach target in 1s.
			Vector3 accel = targetVelocity - relaVelocity;

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

			if (enableDampeners)
				Logger.debugNotify("Damping", 16);

			Block.Controller.SetDamping(enableDampeners);

			myLogger.debugLog("destDisp: " + destDisp
				//+ ", destDir: " + destDir
				+ ", destVelocity: " + destVelocity
				+ ", relaVelocity: " + relaVelocity
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

			//myLogger.debugLog("local: " + localDisp + ", result: " + result, "MaximumVelocity()");

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
			// v� = u� + 2 a � s
			// v = 0
			// u� = -2 a � s
			// Mover will attempt to stop with normal thrust
			float accel = -myThrust.GetForceInDirection(direct) / Block.Physics.Mass;
			//myLogger.debugLog("dist: " + dist + ", accel: " + accel + ", max speed: " + PrettySI.makePretty(Math.Sqrt(-2 * accel * dist)), "MaximumSpeed()");
			return (float)Math.Sqrt(-2 * accel * dist);
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

			//myLogger.debugLog("local: " + localAccel + ", result: " + result, "ToForceRatio()");

			return result;
		}

		/// <summary>
		/// Get the matrix that CalcRotate() will need for a given block and orientation.
		/// </summary>
		/// <param name="block">The block to calculate the matrix from.</param>
		/// <param name="forward">The direction the block should face towards the target.</param>
		/// <param name="up">A direction perpendicular to forward.</param>
		/// <returns>The matrix to be used for CalcRotate()</returns>
		public Matrix GetMatrix(IMyCubeBlock block, Base6Directions.Direction forward = Base6Directions.Direction.Forward, Base6Directions.Direction up = Base6Directions.Direction.Up)
		{
			Matrix result = Matrix.Zero;

			result.Forward = block.LocalMatrix.GetDirectionVector(forward);
			result.Up = block.LocalMatrix.GetDirectionVector(up);
			result.Right = block.LocalMatrix.GetDirectionVector(Base6Directions.GetCross(forward, up));
			//result.M44 = 1f;

			return result;
		}

		/// <summary>
		/// Calculates the force necessary to rotate the grid.
		/// </summary>
		/// <param name="Direction">The direction to face the localMatrix in.</param>
		/// <param name="localMatrix">The matrix to rotate to face the direction, use a block's local matrix or result of GetMatrix()</param>
		/// <returns>True iff localMatrix is facing Direction</returns>
		public bool CalcRotate(RelativeDirection3F Direction, Matrix localMatrix)
		{
			Vector3 angleVelocity, displacement;
			CalcRotate(Direction, localMatrix, out angleVelocity, out displacement);
			updated_prevAngleVel = UpdateCount.Count;
			prevAngleVel = angleVelocity;

			myLogger.debugLog("displacement.LengthSquared(): " + displacement.LengthSquared(), "CalcRotate()");
			return displacement.LengthSquared() < 0.01f;
		}

		/// <summary>
		/// Calculates the force necessary to rotate the grid.
		/// </summary>
		/// <param name="Direction">The direction to face the localMatrix in.</param>
		/// <param name="localMatrix">The matrix to rotate to face the direction, use a block's local matrix or result of GetMatrix()</param>
		/// <param name="angularVelocity">The local angular velocity of the controlling block.</param>
		/// <param name="displacement">Angular distance between localMatrix and Direction</param>
		private void CalcRotate(RelativeDirection3F Direction, Matrix localMatrix, out Vector3 angularVelocity, out Vector3 displacement)
		{
			angularVelocity = -Vector3.Transform(Block.Physics.AngularVelocity, Block.CubeBlock.WorldMatrixNormalizedInv.GetOrientation());
			//myLogger.debugLog("angular: " + angularVelocity, "CalcRotate()");
			float gyroForce = myGyro.TotalGyroForce();

			if (UpdateCount.Count - updated_prevAngleVel == 1ul)
			{
				Vector3 ratio = (angularVelocity - prevAngleVel) / (rotateForceRatio * gyroForce);

				//myLogger.debugLog("rotateForceRatio: " + rotateForceRatio + ", ratio: " + ratio + ", accel: " + (angularVelocity - prevAngleVel) + ", torque: " + (rotateForceRatio * gyroForce), "CalcRotate()");

				myGyro.Update_torqueAccelRatio(rotateForceRatio, ratio);
			}
			else
				myLogger.debugLog("prevAngleVel is old: " + (UpdateCount.Count - updated_prevAngleVel), "CalcRotate()", Logger.severity.INFO);

			localMatrix.M41 = 0; localMatrix.M42 = 0; localMatrix.M43 = 0; localMatrix.M44 = 1;
			Matrix inverted;  Matrix.Invert(ref localMatrix, out inverted);

			localMatrix = localMatrix.GetOrientation();
			inverted = inverted.GetOrientation();

			//myLogger.debugLog("local matrix: right: " + localMatrix.Right + ", up: " + localMatrix.Up + ", back: " + localMatrix.Backward + ", trans: " + localMatrix.Translation, "CalcRotate()");
			//myLogger.debugLog("inverted matrix: right: " + inverted.Right + ", up: " + inverted.Up + ", back: " + inverted.Backward + ", trans: " + inverted.Translation, "CalcRotate()");

			Vector3 localDirect = Direction.ToLocal();
			Vector3 rotBlockDirect; Vector3.Transform(ref localDirect, ref inverted, out rotBlockDirect);

			rotBlockDirect.Normalize();

			float azimuth, elevation; Vector3.GetAzimuthAndElevation(rotBlockDirect, out azimuth, out elevation);

			Vector3 rotaRight = localMatrix.Right;
			Vector3 rotaUp = localMatrix.Up;

			Vector3 NFR_right = Base6Directions.GetVector(Block.CubeBlock.LocalMatrix.GetClosestDirection(ref rotaRight));
			Vector3 NFR_up = Base6Directions.GetVector(Block.CubeBlock.LocalMatrix.GetClosestDirection(ref rotaUp));

			displacement = -elevation * NFR_right + -azimuth * NFR_up;

			myLogger.debugLog("localDirect: " + localDirect + ", rotBlockDirect: " + rotBlockDirect + ", elevation: " + elevation + ", NFR_right: " + NFR_right + ", azimuth: " + azimuth + ", NFR_up: " + NFR_up + ", disp: " + displacement, "CalcRotate()");

			if (!MyAPIGateway.Multiplayer.IsServer)
				return;

			if (myGyro.torqueAccelRatio == 0)
			{
				// do a test
				myLogger.debugLog("torqueAccelRatio == 0", "CalcRotate()");
				rotateForceRatio = new Vector3(0, 1f, 0);
				return;
			}

			Vector3 targetVelocity = MaxAngleVelocity(displacement);
			Vector3 diffVel = targetVelocity - angularVelocity;

			rotateForceRatio = diffVel / (myGyro.torqueAccelRatio * gyroForce);

			//myLogger.debugLog("targetVelocity: " + targetVelocity + ", angularVelocity: " + angularVelocity + ", diffVel: " + diffVel, "CalcRotate()");
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
				{
					myLogger.debugLog("force ratio(" + dim + ") opposes velocity(" + angularVelocity.GetDim(i) + "), index: " + i + ", " + Math.Sign(dim) + ", " + Math.Sign(angularVelocity.GetDim(i)), "CalcRotate()");
					rotateForceRatio.SetDim(i, 0f);
				}
				else
					myLogger.debugLog("force ratio is aligned with velocity: " + i + ", " + Math.Sign(dim) + ", " + Math.Sign(angularVelocity.GetDim(i)), "CalcRotate()");
			}

			return;
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

			for (int i = 0; i < 3; i++)
			{
				float dim = disp.GetDim(i);
				if (dim > 0)
					result.SetDim(i, MaxAngleSpeed(accel, dim));
				else if (dim < 0)
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
			return (float)Math.Sqrt(-2 * accel * dist);
		}

		/// <summary>
		/// Apply the calculated force ratios to the controller.
		/// </summary>
		public void MoveAndRotate()
		{
			if (!MyAPIGateway.Multiplayer.IsServer)
				return;

			myLogger.debugLog("moveForceRatio: " + moveForceRatio + ", rotateForceRatio: " + rotateForceRatio, "MoveAndRotate()");

			// if all the force ratio values are 0, Autopilot has to stop the ship, MoveAndRotate will not
			if ( moveForceRatio == Vector3.Zero && rotateForceRatio == Vector3.Zero)
			{
				Block.Controller.MoveAndRotateStopped();
				return;
			}

			//Stopping = false;

			// clamp values and invert operations MoveAndRotate will perform
			Vector3 moveControl; moveForceRatio.ApplyOperation(dim => MathHelper.Clamp(dim, -1, 1), out moveControl);

			Vector2 rotateControl = Vector2.Zero;
			rotateControl.X = MathHelper.Clamp(rotateForceRatio.X, -1, 1) * pixelsForMaxRotation;
			rotateControl.Y = MathHelper.Clamp(rotateForceRatio.Y, -1, 1) * pixelsForMaxRotation;

			float rollControl = MathHelper.Clamp(rotateForceRatio.Z, -1, 1) / RollControlMultiplier;

			myLogger.debugLog("moveControl: " + moveControl + ", rotateControl: " + rotateControl + ", rollControl: " + rollControl, "MoveAndRotate()");

			Block.Controller.MoveAndRotate(moveControl, rotateControl, rollControl);
		}

	}
}
