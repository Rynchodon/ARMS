using System;
using Rynchodon.Autopilot.Data;
using Sandbox.Game.Entities;
using Sandbox.ModAPI;
using VRageMath;

namespace Rynchodon.Autopilot.Movement
{
	public class Mover
	{
		public readonly ShipControllerBlock Block;

		private readonly Logger myLogger;
		private readonly ThrustProfiler myThrust;
		private readonly GyroProfiler myGyro;
		private readonly AllNavigationSettings NavSet;

		private Vector3 moveForceRatio = Vector3.Zero;
		private Vector2 rotateForceRatio = Vector2.Zero;
		private float rollForceRatio = 0f;

		private bool Stopping = false;

		public Mover(ShipControllerBlock block, AllNavigationSettings NavSet)
		{
			this.myLogger = new Logger("Mover", block.Controller);
			this.Block = block;
			this.myThrust = new ThrustProfiler(block.Controller.CubeGrid);
			this.myGyro = new GyroProfiler(block.Controller.CubeGrid);
			this.NavSet = NavSet;
			this.NavigationBlock = this.Block.Controller;
		}

		public IMyCubeBlock NavigationBlock { get; set; }
		public IDestination Destination { get; set; }
		public IDestination RotateDest { get; set; }

		public void Update()
		{
			if (Destination == null && RotateDest == null)
			{
				FullStop();
				return;
			}

			if (Destination != null)
				CalcMove();
			else
			{
				moveForceRatio = Vector3.Zero;
				Block.Controller.SetDamping(true);
			}
			if (RotateDest != null)
				CalcRotate();
			else
				rotateForceRatio = Vector2.Zero;
			MoveAndRotate();
		}

		public void StopDest(IDestination dest)
		{
			if (dest == Destination)
				Destination = null;
			if (dest == RotateDest)
				RotateDest = null;
		}

		/// <summary>
		/// Stops the ship and sets Destination and RotateDest to null.
		/// </summary>
		public void FullStop()
		{
			myLogger.debugLog("entered", "FullStop()");

			if (Stopping)
				return;
			Stopping = true;

			Destination = null;
			RotateDest = null;

			Block.Controller.MoveAndRotateStopped();
			Block.Controller.SetDamping(true);
		}

		// genius or madness? time will tell...
		private void CalcMove()
		{
			// using world vectors

			Vector3 destDisp = Destination.Point - NavigationBlock.GetPosition();
			Vector3 destVelocity = Destination.Velocity;

			Vector3 velocity = NavigationBlock.GetLinearVelocity();

			Vector3 relaVelocity = velocity;
			if (destVelocity.LengthSquared() > 25f)
				relaVelocity -= destVelocity;

			// switch to using local vectors

			Matrix positionToLocal = Block.CubeGrid.WorldMatrixNormalizedInv;
			Matrix directionToLocal = positionToLocal.GetOrientation();

			destDisp = Vector3.Transform(destDisp, directionToLocal);
			relaVelocity = Vector3.Transform(relaVelocity, directionToLocal);
			velocity = Vector3.Transform(velocity, directionToLocal);

			Vector3 targetVelocity = MaximumVelocity(destDisp);

			float speedTarget = NavSet.CurrentSettings.SpeedTarget;
			float tarSpeedSq = targetVelocity.LengthSquared();
			if (tarSpeedSq > speedTarget * speedTarget)
				targetVelocity *= speedTarget / (float)Math.Sqrt(tarSpeedSq);

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

				// if there is not enough force available, use dampeners

				if (moveForceRatio.GetDim(i) < 2f)
					continue;

				float dim = velocity.GetDim(i);
				if (dim > 1f)
				{
					if (dim - targetDim > 1f)
					{
						//myLogger.debugLog("using dampeners, i: " + i + ", dim: " + dim + ", targetDim: " + targetDim, "CalcMove()");
						//Logger.debugNotify("Damping", 150);
						moveForceRatio.SetDim(i, 0);
						enableDampeners = true;
					}
				}
				else if (dim < -1f && dim - targetDim < -1f)
				{
					//myLogger.debugLog("using dampeners, i: " + i + ", dim: " + dim + ", targetDim: " + targetDim, "CalcMove()");
					//Logger.debugNotify("Damping", 150);
					moveForceRatio.SetDim(i, 0);
					enableDampeners = true;
				}
			}

			Block.Controller.SetDamping(enableDampeners);

			for (int i = 0; i < 3; i++)
				moveForceRatio.SetDim(i, MathHelper.Clamp(moveForceRatio.GetDim(i), -1, 1));

			myLogger.debugLog("destDisp: " + destDisp
				//+ ", destDir: " + destDir
				+ ", destVelocity: " + destVelocity
				+ ", relaVelocity: " + relaVelocity
				+ ", targetVelocity: " + targetVelocity
				//+ ", diffVel: " + diffVel
				+ ", accel: " + accel
				+ ", moveForceRatio: " + moveForceRatio, "CalcMove()");
		}

		private Vector3 MaximumVelocity(Vector3 localDisp)
		{
			Vector3 result = Vector3.Zero;

			if (localDisp.X > 0)
				result.X = MaximumSpeed(-localDisp.X, Base6Directions.Direction.Right);
			else if (localDisp.X < 0)
				result.X = -MaximumSpeed(localDisp.X, Base6Directions.Direction.Left);
			if (localDisp.Y > 0)
				result.Y = MaximumSpeed(-localDisp.Y, Base6Directions.Direction.Up);
			else if (localDisp.Y < 0)
				result.Y = -MaximumSpeed(localDisp.Y, Base6Directions.Direction.Down);
			if (localDisp.Z > 0)
				result.Z = MaximumSpeed(-localDisp.Z, Base6Directions.Direction.Backward);
			else if (localDisp.Z < 0)
				result.Z = -MaximumSpeed(localDisp.Z, Base6Directions.Direction.Forward);

			//myLogger.debugLog("local: " + localDisp + ", result: " + result, "MaximumVelocity()");

			return result;
		}

		private float MaximumSpeed(float disp, Base6Directions.Direction direct)
		{
			// v² = u² + 2 a · s
			// v = 0
			// u² = -2 a · s
			float accel = myThrust.GetForceInDirection(direct) / Block.Physics.Mass;
			//myLogger.debugLog("disp: " + disp + ", accel: " + accel + ", max speed: " + PrettySI.makePretty(Math.Sqrt(-2 * accel * disp)), "MaximumSpeed()");
			return (float)Math.Sqrt(-2 * accel * disp);
		}

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

		private void CalcRotate()
		{
		}

		private void MoveAndRotate()
		{
			myLogger.debugLog("moveAccel: " + moveForceRatio + ", rotateAccel: " + rotateForceRatio + ", rollAccel: " + rollForceRatio, "MoveAndRotate()");

			Stopping = false;
			Block.Controller.MoveAndRotate(moveForceRatio, Vector2.Zero, rollForceRatio);
		}

	}
}
