using System;
using Rynchodon.Autopilot.Data;
using Sandbox.Game.Entities;
using Sandbox.ModAPI;
using VRageMath;

namespace Rynchodon.Autopilot.Movement
{
	public class Mover
	{
		#region S.E. Constants

		private const float pixelsForMaxRotation = 20;
		private const float RollControlMultiplier = 0.2f;

		#endregion

		public readonly ShipControllerBlock Block;

		private readonly Logger myLogger;
		private readonly ThrustProfiler myThrust;
		private readonly GyroProfiler myGyro;
		private readonly AllNavigationSettings NavSet;

		private Vector3 moveForceRatio = Vector3.Zero;
		private Vector3 rotateForceRatio = Vector3.Zero;

		private ulong updated_prevAngleVel = 0;
		private Vector3 prevAngleVel = Vector3.Zero;
		//private Vector3 prevForward = Vector3.Zero;
		private bool Stopping = false;

		public Mover(ShipControllerBlock block, AllNavigationSettings NavSet)
		{
			this.myLogger = new Logger("Mover", block.Controller);
			this.Block = block;
			this.myThrust = new ThrustProfiler(block.Controller.CubeGrid);
			this.myGyro = new GyroProfiler(block.Controller.CubeGrid);
			this.NavSet = NavSet;
			//this.NavigationBlock = this.Block.Controller;
		}

		//// these will be moved to nav settings
		//// there will be two nav blocks, move and rotate
		//public IMyCubeBlock NavigationBlock { get; set; }
		//public IDestination Destination { get; set; }
		//public IDestination RotateDest { get; set; }

		//public void Update()
		//{
		//	if (Destination == null && RotateDest == null)
		//	{
		//		FullStop();
		//		return;
		//	}

		//	if (Destination != null)
		//		CalcMove();
		//	else
		//	{
		//		moveForceRatio = Vector3.Zero;
		//		Block.Controller.SetDamping(true);
		//	}
		//	if (RotateDest != null)
		//		CalcRotate();
		//	else
		//		rotateForceRatio = Vector3.Zero;
		//	MoveAndRotate();
		//}

		//public void StopDest(IDestination dest)
		//{
		//	if (dest == Destination)
		//		Destination = null;
		//	if (dest == RotateDest)
		//		RotateDest = null;
		//}

		//public void StopMove()
		//{ moveForceRatio = Vector3.Zero; }

		//public void StopRotate()
		//{ rotateForceRatio = Vector3.Zero; }

		public void FullStop()
		{
			myLogger.debugLog("entered", "FullStop()");

			if (Stopping)
				return;
			Stopping = true;

			//Destination = null;
			//RotateDest = null;

			moveForceRatio = Vector3.Zero;
			rotateForceRatio = Vector3.Zero;

			Block.Controller.MoveAndRotateStopped();
			Block.Controller.SetDamping(true);
		}

		// genius or madness? time will tell...
		public void CalcMove(IMyCubeBlock NavigationBlock, Vector3 destPoint, Vector3 destVelocity)
		{
			// using world vectors

			Vector3 destDisp = destPoint - NavigationBlock.GetPosition();
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

			Vector3 targetVelocity = MaximumVelocity(destDisp) / 2;

			float tarSpeedSq = targetVelocity.LengthSquared();
			float speedRequest = NavSet.CurrentSettings.SpeedTarget;
			if (tarSpeedSq > speedRequest * speedRequest)
				targetVelocity *= speedRequest / (float)Math.Sqrt(tarSpeedSq);

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
				if (forceRatio < 1f)
					continue;

				if (Math.Sign(forceRatio) * Math.Sign(velocity.GetDim(i)) < 0)
				{
					//myLogger.debugLog("damping, sign of forceRatio: " + Math.Sign(forceRatio) + ", sign of velocity: " + Math.Sign(velocity.GetDim(i)), "CalcMove()");
					moveForceRatio.SetDim(i, 0);
					enableDampeners = true;
				}
				//else
				//	myLogger.debugLog("not damping, sign of forceRatio: " + Math.Sign(forceRatio) + ", sign of velocity: " + Math.Sign(velocity.GetDim(i)), "CalcMove()");
			}

			//if (enableDampeners)
			//	Logger.debugNotify("Damping", 160);

			Block.Controller.SetDamping(enableDampeners);

			//myLogger.debugLog("destDisp: " + destDisp
			//	//+ ", destDir: " + destDir
			//	+ ", destVelocity: " + destVelocity
			//	+ ", relaVelocity: " + relaVelocity
			//	+ ", targetVelocity: " + targetVelocity
			//	//+ ", diffVel: " + diffVel
			//	+ ", accel: " + accel
			//	+ ", moveForceRatio: " + moveForceRatio, "CalcMove()");
		}

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

		public void CalcRotate(IMyCubeBlock NavigationBlock, RelativeDirection3F Direction)
		{
			//myLogger.debugLog("forward: " + NavigationBlock.WorldMatrix.Forward
			//	+ ", prev: " + prevForward
			//	+ ", diff: " + (NavigationBlock.WorldMatrix.Forward - prevForward)
			//	+ ", diff angle: " + (NavigationBlock.WorldMatrix.Forward.AngleBetween(prevForward))
			//	+ ", velocity: " + Block.Physics.AngularVelocity
			//	+ ", speed: " + Block.Physics.AngularVelocity.Length()
			//	+ ", ratio: " + Block.Physics.AngularVelocity.Length() / (NavigationBlock.WorldMatrix.Forward.AngleBetween(prevForward)), "CalcRotate()");

			Vector3 angularVelocity = -Vector3.Transform(Block.Physics.AngularVelocity, Block.CubeGrid.WorldMatrixNormalizedInv.GetOrientation());
			//myLogger.debugLog("angular: " + angularVelocity, "CalcRotate()");
			float gyroForce = myGyro.TotalGyroForce();

			if (UpdateCount.Count - updated_prevAngleVel == 10ul)
			{
				Vector3 ratio = (angularVelocity - prevAngleVel) / (rotateForceRatio * gyroForce);

				//myLogger.debugLog("accel: " + (angularVelocity - prevAngleVel)
				//	+ ", torque: " + (rotateForceRatio * gyroForce)
				//	+ ", ratio: " + ratio, "CalcRotate()");

				//myLogger.debugLog("ratio: " + ratio, "CalcRotate()");
				myGyro.Update_torqueAccelRatio(rotateForceRatio, ratio);
			}
			//else
			//	myLogger.debugLog("prevAngleVel is old: " + (UpdateCount.Count - updated_prevAngleVel), "CalcRotate()", Logger.severity.INFO);

			// calculate pitch, yaw, roll angles

			Vector3 navDirection = Direction.ToBlock(NavigationBlock);

			Vector3 navRight = NavigationBlock.LocalMatrix.Right;
			Vector3 remFrNR = Base6Directions.GetVector(Block.CubeBlock.LocalMatrix.GetClosestDirection(ref navRight));

			Vector3 navUp = NavigationBlock.LocalMatrix.Up;
			Vector3 remFrNU = Base6Directions.GetVector(Block.CubeBlock.LocalMatrix.GetClosestDirection(ref navUp));

			float right = navDirection.X, down = -navDirection.Y, forward = -navDirection.Z;
			float pitch = (float)Math.Atan2(down, forward), yaw = (float)Math.Atan2(right, forward);

			Vector3 disp = pitch * remFrNR + yaw * remFrNU;

			//rotateForceRatio = mapped;
			//return;

			if (myGyro.torqueAccelRatio == 0)
			{
				// do a test
				//myLogger.debugLog("torqueAccelRatio == 0", "CalcRotate()");

				rotateForceRatio = new Vector3(0, -1, 0);

				updated_prevAngleVel = UpdateCount.Count;
				prevAngleVel = angularVelocity;
				//prevForward = NavigationBlock.WorldMatrix.Forward;
				return;
			}

			Vector3 targetVelocity = MaxAngleVelocity(disp) / 2;
			Vector3 diffVel = targetVelocity - angularVelocity;

			rotateForceRatio = diffVel / (myGyro.torqueAccelRatio * gyroForce);

			//myLogger.debugLog("disp: " + disp + ", targetVelocity: " + targetVelocity + ", diffVel: " + diffVel + ", rotateForceRatio: " + rotateForceRatio, "CalcRotate()");

			// dampeners
			for (int i = 0; i < 3; i++)
			{
				// if targetVelocity is close to 0, use dampeners

				float target = targetVelocity.GetDim(i);
				if (target > -0.1f && target < 0.1f)
				{
					//myLogger.debugLog("target near 0 for " + i, "CalcRotate()");
					rotateForceRatio.SetDim(i, 0f);
					continue;
				}

				// where rotateForceRatio opposes angularVelocity, use dampeners

				float dim = rotateForceRatio.GetDim(i);
				if (Math.Sign(dim) * Math.Sign(angularVelocity.GetDim(i)) < 0)
				//{
				//	myLogger.debugLog("force ratio opposes velocity: " + i + ", " + Math.Sign(dim) + ", " + Math.Sign(angularVelocity.GetDim(i)), "CalcRotate()");
					rotateForceRatio.SetDim(i, 0f);
				//}
				//else
				//	myLogger.debugLog("force ratio is aligned with velocity: " + i + ", " + Math.Sign(dim) + ", " + Math.Sign(angularVelocity.GetDim(i)), "CalcRotate()");
			}

			updated_prevAngleVel = UpdateCount.Count;
			prevAngleVel = angularVelocity;
			//prevForward = NavigationBlock.WorldMatrix.Forward;
		}

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

		/// <param name="accel">negative number, maximum deceleration</param>
		/// <param name="dist">positive number, distance to travel</param>
		private float MaxAngleSpeed(float accel, float dist)
		{
			//myLogger.debugLog("accel: " + accel + ", dist: " + dist, "MaxAngleSpeed()");
			return (float)Math.Sqrt(-2 * accel * dist);
		}

		public void MoveAndRotate()
		{
			myLogger.debugLog("moveAccel: " + moveForceRatio + ", rotateAccel: " + rotateForceRatio, "MoveAndRotate()");

			// if all the force ratio values are 0, Autopilot has to stop the ship, MoveAndRotate will not
			if (moveForceRatio == Vector3.Zero && rotateForceRatio == Vector3.Zero)
			{
				Block.Controller.MoveAndRotateStopped();
				return;
			}

			Stopping = false;

			// clamp values and invert operations MoveAndRotate will perform
			Vector3 moveControl; moveForceRatio.ApplyOperation(dim => MathHelper.Clamp(dim, -1, 1), out moveControl);

			Vector2 rotateControl = Vector2.Zero;
			rotateControl.X = MathHelper.Clamp(rotateForceRatio.X, -1, 1) * pixelsForMaxRotation;
			rotateControl.Y = MathHelper.Clamp(rotateForceRatio.Y, -1, 1) * pixelsForMaxRotation;

			float rollControl = MathHelper.Clamp(rotateForceRatio.Z, -1, 1) / RollControlMultiplier;

			Block.Controller.MoveAndRotate(moveControl, rotateControl, rollControl);
		}

	}
}
