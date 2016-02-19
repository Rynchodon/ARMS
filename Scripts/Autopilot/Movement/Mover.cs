using System;
using System.Collections.Generic;
using Rynchodon.Autopilot.Data;
using Rynchodon.Autopilot.Navigator;
using Sandbox.Game.Entities;
using Sandbox.ModAPI;
using VRage.ModAPI;
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

		private const ulong CalcMoveIdle = 100ul;
		/// <summary>Only update torque accel ratio when updates are at least this close together.</summary>
		private const float MaxUpdateSeconds = Globals.UpdatesPerSecond;
		private const float MinForceRatio = 0.1f;

		/// <summary>Controlling block for the grid.</summary>
		public readonly ShipControllerBlock Block;
		public ThrustProfiler myThrust;

		private readonly Logger myLogger;
		private readonly AllNavigationSettings NavSet;

		private IMyCubeGrid myGrid;
		private GyroProfiler myGyro;

		private Vector3 moveForceRatio = Vector3.Zero;
		private Vector3 value_rotateForceRatio = Vector3.Zero;
		private Vector3 rotateForceRatio = Vector3.Zero;
		private Vector3 m_targetVelocity, m_moveAccel;
		private bool m_moveEnableDampeners;

		private Vector3 prevAngleVel = Vector3.Zero;
		//private Vector3 prevAngleDisp = Vector3.Zero;
		private DateTime updated_prevAngleVel;
		private ulong m_notCalcMove = Globals.UpdateCount + CalcMoveIdle;

		private bool m_stopped, m_overworked;

		public Pathfinder.Pathfinder myPathfinder { get; private set; }
		public Vector3 WorldGravity { get { return myThrust.m_worldGravity; } }

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

			CheckGrid();
		}

		/// <summary>
		/// Sets moveForceRatio to zero, optionally enables damping.
		/// </summary>
		public void StopMove(bool enableDampeners = true)
		{
			moveForceRatio = Vector3.Zero;
			Block.SetDamping(enableDampeners);
			m_moveEnableDampeners = enableDampeners;
		}

		/// <summary>
		/// Sets rotateForceRatio to zero.
		/// </summary>
		public void StopRotate()
		{
			myLogger.debugLog("entered", "StopRotate()");
			if ((myPathfinder.CanRotate || !NavSet.Settings_Current.CollisionAvoidance) && ThrustersOverWorked())
				InGravity_LevelOff();
			else
				rotateForceRatio = Vector3.Zero;
		}

		public void MoveAndRotateStop()
		{
			if (m_stopped)
				return;

			moveForceRatio = Vector3.Zero;
			rotateForceRatio = Vector3.Zero;
			Block.SetDamping(true);
			m_moveEnableDampeners = true;
			MyAPIGateway.Utilities.TryInvokeOnGameThread(() => Block.Controller.MoveAndRotateStopped());
			m_stopped = true;
		}

		/// <summary>
		/// Calculates whether or not the grid has sufficient thrust to accelerate forwards at 1 m/s/s.
		/// </summary>
		/// <returns>True iff there is sufficient thrust available.</returns>
		public bool CanMoveForward()
		{
			CheckGrid();

			return myThrust.GetForceInDirection(Block.CubeBlock.Orientation.Forward, true) > Block.Physics.Mass;
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

			myThrust.Update();

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

			m_targetVelocity = MaximumVelocity(destDisp);

			// project targetVelocity onto destination direction (take shortest path)
			Vector3 destDir = destDisp / distance;
			m_targetVelocity = Vector3.Dot(m_targetVelocity, destDir) * destDir;

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
				float tarSpeedSq_1 = m_targetVelocity.LengthSquared();
				if (tarSpeedSq_1 > relSpeedLimit * relSpeedLimit)
				{
					m_targetVelocity *= relSpeedLimit / (float)Math.Sqrt(tarSpeedSq_1);
					myLogger.debugLog("imposing relative speed limit: " + relSpeedLimit + ", targetVelocity: " + m_targetVelocity, "CalcMove()");
				}
			}

			m_targetVelocity += destVelocity;

			// apply speed limit
			float tarSpeedSq = m_targetVelocity.LengthSquared();
			float speedRequest = NavSet.Settings_Current.SpeedTarget;
			if (tarSpeedSq > speedRequest * speedRequest)
				m_targetVelocity *= speedRequest / (float)Math.Sqrt(tarSpeedSq);
			else
				NavSet.Settings_Task_NavWay.NearingDestination = true;

			m_moveAccel = m_targetVelocity - velocity - myThrust.m_localGravity;

			moveForceRatio = ToForceRatio(m_moveAccel);

			// dampeners
			m_moveEnableDampeners = false;
			bool checkNearZero = m_targetVelocity.LengthSquared() < 1f || m_moveAccel.LengthSquared() > 1f;

			for (int i = 0; i < 3; i++)
			{
				float targetDim = m_targetVelocity.GetDim(i);

				if (checkNearZero)
				{
					// if target velocity is close to 0, use dampeners

					if (targetDim < 0.1f && targetDim > -0.1f)
					{
						myLogger.debugLog("close to 0, i: " + i + ", targetDim: " + targetDim, "CalcMove()");
						moveForceRatio.SetDim(i, 0);
						m_moveEnableDampeners = true;
						continue;
					}
				}

				float forceRatio = moveForceRatio.GetDim(i);
				if (forceRatio < 1f && forceRatio > -1f)
				{
					// minimum force ratio is needed because SE is being strange about gravity
					if (forceRatio < MinForceRatio && forceRatio > -MinForceRatio && myThrust.m_worldGravity.LengthSquared() > 0.1f)
					{
						if (targetDim > 0.1f)
							moveForceRatio.SetDim(i, MinForceRatio);
						else
							moveForceRatio.SetDim(i, -MinForceRatio);
					}
					continue;
				}

				// force ratio is > 1 || < -1. If it is useful, use dampeners

				float velDim = velocity.GetDim(i);
				if (velDim < 1f && velDim > -1f)
					continue;

				if (Math.Sign(forceRatio) * Math.Sign(velDim) < 0)
				{
					myLogger.debugLog("damping, i: " + i + ", force ratio: " + forceRatio + ", velocity: " + velDim + ", sign of forceRatio: " + Math.Sign(forceRatio) + ", sign of velocity: " + Math.Sign(velDim), "CalcMove()");
					moveForceRatio.SetDim(i, 0);
					m_moveEnableDampeners = true;
				}
				else
					myLogger.debugLog("not damping, i: " + i + ", force ratio: " + forceRatio + ", velocity: " + velDim + ", sign of forceRatio: " + Math.Sign(forceRatio) + ", sign of velocity: " + Math.Sign(velDim), "CalcMove()");
			}

			Block.SetDamping(m_moveEnableDampeners);

			if (destDisp.LengthSquared() > 1f)
				m_notCalcMove = Globals.UpdateCount + CalcMoveIdle;

			myLogger.debugLog("destDisp: " + destDisp
				//+ ", destDir: " + destDir
				+ ", destVelocity: " + destVelocity
				//+ ", relaVelocity: " + relaVelocity
				+ ", targetVelocity: " + m_targetVelocity
				+ ", velocity: " + velocity
				//+ ", diffVel: " + diffVel
				+ ", m_moveAccel: " + m_moveAccel
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

			if (localDisp.X > 0f)
				result.X = MaximumSpeed(localDisp.X, Base6Directions.Direction.Right);
			else if (localDisp.X < 0f)
				result.X = -MaximumSpeed(-localDisp.X, Base6Directions.Direction.Left);
			if (localDisp.Y > 0f)
				result.Y = MaximumSpeed(localDisp.Y, Base6Directions.Direction.Up);
			else if (localDisp.Y < 0f)
				result.Y = -MaximumSpeed(-localDisp.Y, Base6Directions.Direction.Down);
			if (localDisp.Z > 0f)
				result.Z = MaximumSpeed(localDisp.Z, Base6Directions.Direction.Backward);
			else if (localDisp.Z < 0f)
				result.Z = -MaximumSpeed(-localDisp.Z, Base6Directions.Direction.Forward);

			myLogger.debugLog("displacement: " + localDisp + ", maximum velocity: " + result, "MaximumVelocity()");

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
			if (dist < 0.1f)
				return 0f;

			direct = Base6Directions.GetClosestDirection(Block.CubeBlock.LocalMatrix.GetDirectionVector(direct));
			float force = myThrust.GetForceInDirection(direct, true);
			if (force < 1f)
			{
				myLogger.debugLog("No thrust available in direction: " + direct, "MaximumSpeed()", Logger.severity.DEBUG);
				return 0f;
			}
			float accel = -force / Block.Physics.Mass * 0.5f;
			myLogger.debugLog("direction: " + direct + ", dist: " + dist + ", max accel: " + accel + ", mass: " + Block.Physics.Mass + ", max speed: " + PrettySI.makePretty(Math.Sqrt(-2f * accel * dist)) + "m/s" + ", cap: " + dist * 2f + " m/s", "MaximumSpeed()");
			return Math.Min((float)Math.Sqrt(-2f * accel * dist), dist * 2f); // capped for the sake of autopilot's reaction time
		}

		/// <summary>
		/// Calculate the force ratio from acceleration.
		/// </summary>
		/// <param name="localAccel">Acceleration</param>
		/// <returns>Force ratio</returns>
		private Vector3 ToForceRatio(Vector3 localAccel)
		{
			Vector3 result = Vector3.Zero;

			if (localAccel.X > 0f)
				result.X = localAccel.X * Block.Physics.Mass / myThrust.GetForceInDirection(Base6Directions.GetClosestDirection(Block.CubeBlock.LocalMatrix.Right));
			else if (localAccel.X < 0f)
				result.X = localAccel.X * Block.Physics.Mass / myThrust.GetForceInDirection(Base6Directions.GetClosestDirection(Block.CubeBlock.LocalMatrix.Left));
			if (localAccel.Y > 0f)
				result.Y = localAccel.Y * Block.Physics.Mass / myThrust.GetForceInDirection(Base6Directions.GetClosestDirection(Block.CubeBlock.LocalMatrix.Up));
			else if (localAccel.Y < 0f)
				result.Y = localAccel.Y * Block.Physics.Mass / myThrust.GetForceInDirection(Base6Directions.GetClosestDirection(Block.CubeBlock.LocalMatrix.Down));
			if (localAccel.Z > 0f)
				result.Z = localAccel.Z * Block.Physics.Mass / myThrust.GetForceInDirection(Base6Directions.GetClosestDirection(Block.CubeBlock.LocalMatrix.Backward));
			else if (localAccel.Z < 0f)
				result.Z = localAccel.Z * Block.Physics.Mass / myThrust.GetForceInDirection(Base6Directions.GetClosestDirection(Block.CubeBlock.LocalMatrix.Forward));

			myLogger.debugLog("accel: " + localAccel + ", force ratio: " + result + ", mass: " + Block.Physics.Mass, "ToForceRatio()");

			return result;
		}

		/// <summary>
		/// If the ship is in gravity, calculates the roll to level off.
		/// </summary>
		/// <returns>True iff the ship is in gravity.</returns>
		private bool InGravity_LevelOff()
		{
			if (myThrust.m_localGravity == Vector3.Zero)
				return false;

			myLogger.debugLog("Rotating to fight gravity", "InGravity_LevelOff()", Logger.severity.TRACE);
			//Logger.notify("Rotating to fight gravity", 50);
			Matrix localMatrix = Block.CubeBlock.LocalMatrix;
			localMatrix.Forward = Block.CubeBlock.LocalMatrix.Up;
			localMatrix.Up = Block.CubeBlock.LocalMatrix.Backward;
			RelativeDirection3F Direction = RelativeDirection3F.FromLocal(Block.CubeGrid, -myThrust.m_localGravity);
			RelativeDirection3F UpDirect = null;

			CalcRotate(localMatrix, Direction, UpDirect, true);
			return true;
		}

		/// <summary>
		/// Rotate to face the best direction for flight.
		/// </summary>
		public void CalcRotate()
		{
			myLogger.debugLog("m_moveEnableDampeners: " + m_moveEnableDampeners + ", m_moveAccel: " + m_moveAccel, "CalcRotate()");
			if (m_moveEnableDampeners || m_moveAccel.LengthSquared() > 100f)
				CalcRotate(Block.Pseudo, RelativeDirection3F.FromLocal(Block.CubeGrid, m_moveAccel));
			else
				if (!InGravity_LevelOff())
					if (NavSet.Settings_Current.NearingDestination)
						// rotate to stop
						CalcRotate(Block.Pseudo, RelativeDirection3F.FromWorld(Block.CubeGrid, -Block.Physics.LinearVelocity));
					else
						StopRotate();
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

			RelativeDirection3F faceForward = RelativeDirection3F.FromWorld(block.Grid, destBlock.WorldMatrix.GetDirectionVector(forward.Value));
			RelativeDirection3F faceUpward = upward.HasValue ? RelativeDirection3F.FromWorld(block.Grid, destBlock.WorldMatrix.GetDirectionVector(upward.Value)) : null;

			CalcRotate(block.LocalMatrix, faceForward, faceUpward);
		}

		/// <summary>
		/// Calculates the force necessary to rotate the grid.
		/// </summary>
		/// <param name="Direction">The direction to face the localMatrix in.</param>
		/// <param name="block"></param>
		/// <returns>True iff localMatrix is facing Direction</returns>
		public void CalcRotate(PseudoBlock block, RelativeDirection3F Direction, RelativeDirection3F UpDirect = null, IMyEntity targetEntity = null)
		{
			CalcRotate(block.LocalMatrix, Direction, UpDirect, targetEntity: targetEntity);
		}

		// necessary wrapper for main CalcRotate, should always be called.
		private void CalcRotate(Matrix localMatrix, RelativeDirection3F Direction, RelativeDirection3F UpDirect, bool levelingOff = false, IMyEntity targetEntity = null)
		{
			CheckGrid();
			myThrust.Update();

			if (!levelingOff)
			{
				if (ThrustersOverWorked())
				{
					myLogger.debugLog("Thrusters overworked; overruling navigator, need to level off", "CalcRotate()");
					if (InGravity_LevelOff())
						return;
					else
						myLogger.alwaysLog("Failed gravity check!", "CalcRotate()", Logger.severity.FATAL);
				}
				if (NavSet.Settings_Current.CollisionAvoidance && !myPathfinder.CanMove && InGravity_LevelOff())
				{
					myLogger.debugLog("Pathfinder preventing movement; level off", "CalcRotate()");
					return;
				}
			}

			Vector3 angleVelocity;
			CalcRotate(localMatrix, Direction, UpDirect, out angleVelocity, targetEntity);
			prevAngleVel = angleVelocity;
		}

		/// <summary>
		/// Calculates the force necessary to rotate the grid.
		/// </summary>
		/// <param name="localMatrix">The matrix to rotate to face the direction, use a block's local matrix or result of GetMatrix()</param>
		/// <param name="Direction">The direction to face the localMatrix in.</param>
		/// <param name="angularVelocity">The local angular velocity of the controlling block.</param>
		private void CalcRotate(Matrix localMatrix, RelativeDirection3F Direction, RelativeDirection3F UpDirect, out Vector3 angularVelocity, IMyEntity targetEntity)
		{
			myLogger.debugLog(Direction == null, "Direction == null", "CalcRotate()", Logger.severity.ERROR);

			angularVelocity = -Vector3.Transform(Block.Physics.AngularVelocity, Block.CubeBlock.WorldMatrixNormalizedInv.GetOrientation());

			//myLogger.debugLog("angular: " + angularVelocity, "CalcRotate()");
			float gyroForce = myGyro.TotalGyroForce();

			float secondsSinceLast = (float)(DateTime.UtcNow - updated_prevAngleVel).TotalSeconds;
			updated_prevAngleVel = DateTime.UtcNow;

			if (rotateForceRatio != Vector3.Zero)
			{
				if (secondsSinceLast <= MaxUpdateSeconds)
				{
					Vector3 ratio = (angularVelocity - prevAngleVel) / (rotateForceRatio * gyroForce * secondsSinceLast);

					//myLogger.debugLog("rotateForceRatio: " + rotateForceRatio + ", ratio: " + ratio + ", accel: " + (angularVelocity - prevAngleVel) + ", torque: " + (rotateForceRatio * gyroForce), "CalcRotate()");

					myGyro.Update_torqueAccelRatio(rotateForceRatio, ratio);
				}
				else
					myLogger.debugLog("prevAngleVel is old: " + secondsSinceLast, "CalcRotate()", Logger.severity.DEBUG);
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

				myLogger.debugLog("roll: " + roll + ", displacement: " + displacement + ", NFR_backward: " + NFR_backward + ", change: " + (roll * NFR_backward), "CalcRotate()");

				displacement += roll * NFR_backward;
			}

			float distanceAngle = displacement.Length();
			NavSet.Settings_Task_NavWay.DistanceAngle = distanceAngle;

			// don't rotate if displacement is small, reduces shake
			if (distanceAngle < 0.025f)
			{
				StopRotate();
				return;
			}

			if (NavSet.Settings_Current.CollisionAvoidance)
			{
				myPathfinder.TestRotate(displacement);
				if (!myPathfinder.CanRotate)
				{
					// if cannot rotate and not calculating move, move away from obstruction
					if (myPathfinder.RotateObstruction != null && Globals.UpdateCount >= m_notCalcMove)
					{
						Vector3 position  = Block.CubeBlock.GetPosition();
						Vector3 away = position - myPathfinder.RotateObstruction.GetCentre();
						away.Normalize();
						myLogger.debugLog("Cannot rotate and not calculating move, creating GOLIS to move away from obstruction", "CalcRotate()", Logger.severity.INFO);
						new GOLIS(this, NavSet, position + away * (10f + NavSet.Settings_Current.DestinationRadius), true);
					}
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

			Vector3 targetVelocity = MaxAngleVelocity(displacement, secondsSinceLast);

			if (targetEntity != null)
			{
				Vector3 relativeLinearVelocity = targetEntity.GetLinearVelocity() - Block.Physics.LinearVelocity;
				float distance = Vector3.Distance(targetEntity.GetCentre(), Block.CubeBlock.GetPosition());
				//Vector3 tangentialVelocity = Vector3.Reject(relativeLinearVelocity, targetEntity.GetCentre() - Block.CubeBlock.GetPosition());
				//Vector3 localTangVel = Vector3.Transform(tangentialVelocity, Block.CubeBlock.WorldMatrixNormalizedInv.GetOrientation());

				//myLogger.debugLog("relativeLinearVelocity: " + relativeLinearVelocity + ", tangentialVelocity: " + tangentialVelocity + ", localTangVel: " + localTangVel, "CalcRotate()");

				float RLV_pitch = Vector3.Dot(relativeLinearVelocity, Block.CubeBlock.WorldMatrix.Up);
				float RLV_yaw = Vector3.Dot(relativeLinearVelocity, Block.CubeBlock.WorldMatrix.Left);
				float angl_pitch = (float)Math.Atan2(RLV_pitch, distance);
				float angl_yaw = (float)Math.Atan2(RLV_yaw, distance);

				myLogger.debugLog("relativeLinearVelocity: " + relativeLinearVelocity + ", RLV_yaw: " + RLV_yaw + ", RLV_pitch: " + RLV_pitch + ", angl_yaw: " + angl_yaw + ", angl_pitch: " + angl_pitch, "CalcRotate()");

				targetVelocity += new Vector3(angl_pitch, angl_yaw, 0f);
			}

			Vector3 accel = (targetVelocity - angularVelocity) / secondsSinceLast;

			rotateForceRatio = accel / (myGyro.torqueAccelRatio * secondsSinceLast * gyroForce);

			myLogger.debugLog("targetVelocity: " + targetVelocity + ", angularVelocity: " + angularVelocity + ", accel: " + accel, "CalcRotate()");
			myLogger.debugLog("accel: " + accel + ", torque: " + (myGyro.torqueAccelRatio * secondsSinceLast * gyroForce) + ", rotateForceRatio: " + rotateForceRatio, "CalcRotate()");

			// dampeners
			for (int i = 0; i < 3; i++)
			{
				// if targetVelocity is close to 0, use dampeners

				float target = targetVelocity.GetDim(i);
				if (target > -0.01f && target < 0.01f)
				{
					//myLogger.debugLog("target near 0 for " + i + ", " + target, "CalcRotate()");
					rotateForceRatio.SetDim(i, 0f);
					continue;
				}
			}
		}

		/// <summary>
		/// Calulates the maximum angular velocity to stop at the destination.
		/// </summary>
		/// <param name="disp">Displacement to the destination.</param>
		/// <returns>The maximum angular velocity to stop at the destination.</returns>
		private Vector3 MaxAngleVelocity(Vector3 disp, float secondsSinceLast)
		{
			Vector3 result = Vector3.Zero;

			// S.E. provides damping for angular motion, we will ignore this
			float accel = -myGyro.torqueAccelRatio * secondsSinceLast * myGyro.TotalGyroForce();

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

			//myLogger.debugLog("moveForceRatio: " + moveForceRatio + ", rotateForceRatio: " + rotateForceRatio + ", move length: " + moveForceRatio.Length(), "MoveAndRotate()");
			MyShipController controller = Block.Controller;

			// if all the force ratio values are 0, Autopilot has to stop the ship, MoveAndRotate will not
			if (moveForceRatio == Vector3.Zero && rotateForceRatio == Vector3.Zero)
			{
				myLogger.debugLog("Stopping the ship", "MoveAndRotate()"); 
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

			Vector3 unstick;
			if (controller.CubeGrid.GetLinearVelocity().LengthSquared() < 0.01f && moveControl.LengthSquared() > 0.01f)
			{
				Vector3 direction = Vector3.Transform(m_targetVelocity, controller.WorldMatrix);
				direction.Normalize();
				unstick = direction * 0.1f;
				myLogger.debugLog("Velocity: " + controller.CubeGrid.GetLinearVelocity() + ", Unstick: " + unstick, "MoveAndRotate()");
			}
			else
				unstick = Vector3.Zero;

			m_stopped = false;
			//myLogger.debugLog("Queueing Move and Rotate, move: " + moveControl + ", rotate: " + rotateControl + ", roll: " + rollControl + ", unstick: " + unstick, "MoveAndRotate()");
			MyAPIGateway.Utilities.TryInvokeOnGameThread(() => {
				myLogger.debugLog("Applying Move and Rotate, move: " + moveControl + ", rotate: " + rotateControl + ", roll: " + rollControl + ", unstick: " + unstick, "MoveAndRotate()");
				controller.MoveAndRotate(moveControl, rotateControl, rollControl);
				controller.CubeGrid.Physics.LinearVelocity += unstick;
			}, myLogger);
		}

		private void CheckGrid()
		{
			if (myGrid != Block.CubeGrid)
			{
				myLogger.debugLog(myGrid != null, "Grid Changed! from " + myGrid.getBestName() + " to " + Block.CubeGrid.getBestName(), "CheckGrid()", Logger.severity.INFO);
				myGrid = Block.CubeGrid;
				this.myThrust = new ThrustProfiler(myGrid);
				this.myGyro = new GyroProfiler(myGrid);
				this.myPathfinder = new Pathfinder.Pathfinder(myGrid, NavSet, this);
			}
		}

		public bool ThrustersOverWorked(float ratio = 0.9f)
		{
			myLogger.debugLog(myThrust == null, "myThrust == null", "ThrustersOverWorked()", Logger.severity.FATAL);
			if (m_overworked)
				ratio += 0.05f;
			m_overworked = myThrust.m_gravityReactRatio.AbsMax() >= ratio;
			return m_overworked;
		}

	}
}
