#if DEBUG
//#define TRACE
#endif

using System;
using Rynchodon.Autopilot.Data;
using Rynchodon.Autopilot.Pathfinding;
using Rynchodon.Threading;
using Rynchodon.Utility;
using Rynchodon.Utility.Vectors;
using Sandbox.Game.Entities;
using Sandbox.Game.GameSystems;
using Sandbox.ModAPI;
using VRage.Game.ModAPI;
using VRage.ModAPI;
using VRageMath;

namespace Rynchodon.Autopilot.Movement
{
	/// <summary>
	/// Performs the movement calculations and the movements.
	/// </summary>
	/// TODO: mover components, normal ship, missing thrusters, and helicopter
	/// current system is a mess of trying to guess what is going on
	public class Mover
	{
		#region S.E. Constants

		private const float pixelsForMaxRotation = 20;
		private const float RollControlMultiplier = 0.2f;
		private const float MaxInverseTensor = 1f / 125000f;

		#endregion

		/// <summary>Fraction of force used to calculate maximum speed.</summary>
		public const float AvailableForceRatio = 0.5f;
		public const float OverworkedThreshold = 0.75f;
		/// <summary>How far from the destination a ship needs to be to take a curved path instead of a straight one.</summary>
		public const float PlanetMoveDist = 1000f;
		public const float DistanceSpeedFactor = 0.2f;
		private const ulong WriggleAfter = 500ul, StuckAfter = WriggleAfter + 2000ul, MoveAwayAfter = StuckAfter + 100ul;

		public readonly AllNavigationSettings NavSet;

		private IMyCubeGrid m_grid;
		private GyroProfiler m_gyro;

		private Vector3 m_moveForceRatio = Vector3.Zero;
		private Vector3 m_moveAccel;
		private Vector3 m_rotateTargetVelocity = Vector3.Zero;
		private Vector3 m_rotateForceRatio = Vector3.Zero;
		//private Vector3 m_prevMoveControl = Vector3.Zero;
		//private Vector2 m_prevRotateControl = Vector2.Zero;
		//private float m_prevRollControl = 0f;

		private Vector3 m_lastMoveAccel = Vector3.Zero;
		//private float m_bestAngle = float.MaxValue;
		private ulong m_lastMoveAttempt = ulong.MaxValue, m_lastAccel = ulong.MaxValue;

		private bool m_stopped, m_thrustHigh;

		/// <summary>Controlling block for the grid.</summary>
		public readonly ShipControllerBlock Block;
		public ThrustProfiler Thrust { get; private set; }

		public RotateChecker RotateCheck;

		private Logable Log { get { return new Logable(Block?.Controller); } }

		private bool CheckStuck(ulong duration)
		{
			return (Globals.UpdateCount - m_lastMoveAttempt) < 100ul && (Globals.UpdateCount - m_lastAccel) > duration;
		}

		/// <summary>Value is false iff this Mover is making progress.</summary>
		public bool MoveStuck
		{
			get
			{
				return CheckStuck(StuckAfter);
			}
			set
			{
				if (value)
					m_lastAccel = ulong.MinValue;
				else
					m_lastAccel = Globals.UpdateCount;
			}
		}

		public DirectionWorld LinearVelocity
		{
			get { return Block.Physics.LinearVelocity; }
		}

		public DirectionWorld AngularVelocity
		{
			get { return Block.Physics.AngularVelocity; }
		}

		/// <summary>
		/// Creates a Mover for a given ShipControllerBlock and AllNavigationSettings
		/// </summary>
		/// <param name="block">Controlling block for the grid</param>
		/// <param name="NavSet">Navigation settings to use.</param>
		public Mover(ShipControllerBlock block, RotateChecker rotateCheck)
		{
			this.Block = block;
			this.NavSet = new AllNavigationSettings(block.CubeBlock);
			this.RotateCheck = rotateCheck;
			this.NavSet.AfterTaskComplete += NavSet_AfterTaskComplete;

			CheckGrid();
		}

		private void NavSet_AfterTaskComplete()
		{
			MoveStuck = false;
		}

		/// <summary>
		/// Sets target linear force to zero, optionally enables damping.
		/// </summary>
		public void StopMove(bool enableDampeners = true)
		{
			//Log.DebugLog("entered", "StopMove()");

			m_moveForceRatio = m_moveAccel = Vector3.Zero;
			SetDamping(enableDampeners);
		}

		/// <summary>
		/// Sets target angular force to zero.
		/// </summary>
		public void StopRotate()
		{
			//Log.DebugLog("stopping rotation", "StopRotate()");

			m_rotateTargetVelocity = m_rotateForceRatio = Vector3.Zero;
			//m_prevRotateControl = Vector2.Zero;
			//m_prevRollControl = 0f;
		}

		/// <summary>
		/// Stop movement and rotation of the controller.
		/// </summary>
		/// <param name="enableDampeners">If true, dampeners will be enabled. If false, they will not be toggled.</param>
		public void MoveAndRotateStop(bool enableDampeners = true)
		{
			if (enableDampeners)
				SetDamping(true);

			if (m_stopped)
			{
				Log.TraceLog("already stopped");
				return;
			}

			Log.DebugLog("stopping movement and rotation, dampeners: " + enableDampeners);

			m_moveForceRatio = m_moveAccel = Vector3.Zero;

			StopRotate();

			MyAPIGateway.Utilities.TryInvokeOnGameThread(() => {
				Block.Controller.MoveAndRotateStopped();
				Thrust.ClearOverrides();
				//m_gyro.ClearOverrides();
			});

			m_stopped = true;
		}

		#region Move

		/// <summary>
		/// Calculates the force necessary to move the grid.
		/// </summary>
		/// <param name="block">To get world position from.</param>
		/// <param name="destPoint">The world position of the destination</param>
		/// <param name="destVelocity">The speed of the destination</param>
		/// <param name="landing">Puts an emphasis on not overshooting the target.</param>
		public void CalcMove(PseudoBlock block, ref Vector3 destDisp, ref Vector3 destVelocity)
		{
			Log.DebugLog("Not on autopilot thread: " + ThreadTracker.ThreadName, Logger.severity.ERROR, condition: !ThreadTracker.ThreadName.StartsWith("Autopilot"));

			CheckGrid();
			m_lastMoveAttempt = Globals.UpdateCount;
			Thrust.Update();

			// using local vectors

			Vector3 velocity = LinearVelocity;

			MatrixD positionToLocal = Block.CubeBlock.WorldMatrixNormalizedInv;
			MatrixD directionToLocal = positionToLocal.GetOrientation();

			destVelocity = Vector3.Transform(destVelocity, directionToLocal);
			velocity = Vector3.Transform(velocity, directionToLocal);

			Vector3 targetVelocity;
			//float distance;
			if (destDisp.LengthSquared() > 0.01f)
			{
				destDisp = Vector3.Transform(destDisp, directionToLocal);
				float distance = destDisp.Length();

				targetVelocity = MaximumVelocity(destDisp);

				// project targetVelocity onto destination direction (take shortest path)
				Vector3 destDir = destDisp / distance;
				targetVelocity = Vector3.Dot(targetVelocity, destDir) * destDir;

				// apply relative speed limit
				float relSpeedLimit = NavSet.Settings_Current.SpeedMaxRelative;
					float landingSpeed = Math.Max(distance * DistanceSpeedFactor, DistanceSpeedFactor);
					if (relSpeedLimit > landingSpeed)
						relSpeedLimit = landingSpeed;
				if (relSpeedLimit < float.MaxValue)
				{
					float tarSpeedSq_1 = targetVelocity.LengthSquared();
					if (tarSpeedSq_1 > relSpeedLimit * relSpeedLimit)
					{
						targetVelocity *= relSpeedLimit / (float)Math.Sqrt(tarSpeedSq_1);
						//Log.DebugLog("imposing relative speed limit: " + relSpeedLimit + ", targetVelocity: " + targetVelocity, "CalcMove()");
					}
				}
			}
			else
			{
				targetVelocity = Vector3.Zero;
			}

			targetVelocity += destVelocity;

			// apply speed limit
			float tarSpeedSq = targetVelocity.LengthSquared();
			float speedRequest = NavSet.Settings_Current.SpeedTarget;
			if (tarSpeedSq > speedRequest * speedRequest)
			{
				targetVelocity *= speedRequest / (float)Math.Sqrt(tarSpeedSq);
				//Log.DebugLog("imposing speed limit: " + speedRequest + ", targetVelocity: " + targetVelocity, "CalcMove()");
			}

			m_moveAccel = targetVelocity - velocity;

			if (m_moveAccel.LengthSquared() < 0.01f)
			{
				Log.DebugLog("Wriggle unstuck autopilot. Near target velocity, move accel: " + m_moveAccel + ". m_lastMoveAccel set to now", condition: (Globals.UpdateCount - m_lastAccel) > WriggleAfter);
				m_lastMoveAccel = m_moveAccel;
				m_lastAccel = Globals.UpdateCount;
			}
			else
			{
				float diffSq; Vector3.DistanceSquared(ref m_moveAccel, ref m_lastMoveAccel, out diffSq);
				if (diffSq > 1f)
				{
					Log.DebugLog("Wriggle unstuck autopilot. Change in move accel from " + m_lastMoveAccel + " to " + m_moveAccel + ". m_lastMoveAccel set to now", condition: (Globals.UpdateCount - m_lastAccel) > WriggleAfter);
					m_lastMoveAccel = m_moveAccel;
					m_lastAccel = Globals.UpdateCount;
				}
			}

			CalcMove(ref velocity);

			Log.TraceLog(string.Empty
				//+ "block: " + block.Block.getBestName()
				//+ ", dest point: " + destPoint
				//+ ", position: " + block.WorldPosition
				+ "destDisp: " + destDisp
				+ ", destVelocity: " + destVelocity
				+ ", targetVelocity: " + targetVelocity
				+ ", velocity: " + velocity
				+ ", m_moveAccel: " + m_moveAccel
				+ ", moveForceRatio: " + m_moveForceRatio);
		}

		private void CalcMove(ref Vector3 velocity)
		{
			DirectionBlock gravBlock = Thrust.WorldGravity.ToBlock(Block.CubeBlock);
			m_moveForceRatio = ToForceRatio(m_moveAccel - gravBlock.vector);

			// dampeners
			bool enableDampeners = false;
			m_thrustHigh = false;

			for (int index = 0; index < 3; index++)
			{
				//const float minForceRatio = 0.1f;
				//const float zeroForceRatio = 0.01f;

				float velDim = velocity.GetDim(index);

				// dampeners are useful for precise stopping but they do not always work properly
				if (velDim < 1f && velDim > -1f)
				{
					float targetVelDim = m_moveAccel.GetDim(index) + velDim;

					if (targetVelDim < 0.01f && targetVelDim > -0.01f)
					{
						//Log.DebugLog("for dim: " + index + ", target velocity near zero: " + targetVelDim);
						m_moveForceRatio.SetDim(index, 0f);
						enableDampeners = true;
						continue;
					}
				}

				float forceRatio = m_moveForceRatio.GetDim(index);

				if (forceRatio < 1f && forceRatio > -1f)
				{
					//if (forceRatio > zeroForceRatio && forceRatio < minForceRatio)
					//	moveForceRatio.SetDim(i, minForceRatio);
					//else if (forceRatio < -zeroForceRatio && forceRatio > -minForceRatio)
					//	moveForceRatio.SetDim(i, -minForceRatio);
					continue;
				}

				if (forceRatio < -10f || 10f < forceRatio)
					m_thrustHigh = true;

				// force ratio is > 1 || < -1. If it is useful, use dampeners

				if (velDim < 1f && velDim > -1f)
					continue;

				if (Math.Sign(forceRatio) * Math.Sign(velDim) < 0)
				{
					//Log.DebugLog("damping, i: " + index + ", force ratio: " + forceRatio + ", velocity: " + velDim + ", sign of forceRatio: " + Math.Sign(forceRatio) + ", sign of velocity: " + Math.Sign(velDim));
					m_moveForceRatio.SetDim(index, 0);
					enableDampeners = true;
					m_thrustHigh = true;
				}
				//else
				//	Log.DebugLog("not damping, i: " + i + ", force ratio: " + forceRatio + ", velocity: " + velDim + ", sign of forceRatio: " + Math.Sign(forceRatio) + ", sign of velocity: " + Math.Sign(velDim), "CalcMove()");
			}

			SetDamping(enableDampeners);
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
				result.X = MaximumSpeed(localDisp.X, Base6Directions.Direction.Left);
			else if (localDisp.X < 0f)
				result.X = -MaximumSpeed(-localDisp.X, Base6Directions.Direction.Right);
			if (localDisp.Y > 0f)
				result.Y = MaximumSpeed(localDisp.Y, Base6Directions.Direction.Down);
			else if (localDisp.Y < 0f)
				result.Y = -MaximumSpeed(-localDisp.Y, Base6Directions.Direction.Up);
			if (localDisp.Z > 0f)
				result.Z = MaximumSpeed(localDisp.Z, Base6Directions.Direction.Forward);
			else if (localDisp.Z < 0f)
				result.Z = -MaximumSpeed(-localDisp.Z, Base6Directions.Direction.Backward);

			//Log.DebugLog("displacement: " + localDisp + ", maximum velocity: " + result, "MaximumVelocity()");

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
			if (dist < 0.01f)
				return 0f;

			direct = Block.CubeBlock.Orientation.TransformDirection(direct);
			float force = Thrust.GetForceInDirection(direct, true) * AvailableForceRatio;
			if (force < 1f)
			{
				//Log.DebugLog("No thrust available in direction: " + direct + ", dist: " + dist, "MaximumSpeed()", Logger.severity.DEBUG);
				return dist * 0.1f;
			}
			float accel = -force / Block.Physics.Mass;
			//Log.DebugLog("direction: " + direct + ", dist: " + dist + ", max accel: " + accel + ", mass: " + Block.Physics.Mass + ", max speed: " + PrettySI.makePretty(Math.Sqrt(-2f * accel * dist)) + " m/s" + ", cap: " + dist * 0.5f + " m/s", "MaximumSpeed()");
			//return Math.Min((float)Math.Sqrt(-2f * accel * dist), dist * 0.25f); // capped for the sake of autopilot's reaction time
			return (float)Math.Sqrt(-2f * accel * dist);
		}

		/// <summary>
		/// Calculate the force ratio from acceleration.
		/// </summary>
		/// <param name="blockAccel">Acceleration</param>
		/// <returns>Force ratio</returns>
		private Vector3 ToForceRatio(Vector3 blockAccel)
		{
			Vector3 result = Vector3.Zero;

			if (blockAccel.X > 0f)
				result.X = blockAccel.X * Block.Physics.Mass / Thrust.GetForceInDirection(Base6Directions.GetFlippedDirection(Block.CubeBlock.Orientation.Left));
			else if (blockAccel.X < 0f)
				result.X = blockAccel.X * Block.Physics.Mass / Thrust.GetForceInDirection(Block.CubeBlock.Orientation.Left);
			if (blockAccel.Y > 0f)
				result.Y = blockAccel.Y * Block.Physics.Mass / Thrust.GetForceInDirection(Block.CubeBlock.Orientation.Up);
			else if (blockAccel.Y < 0f)
				result.Y = blockAccel.Y * Block.Physics.Mass / Thrust.GetForceInDirection(Base6Directions.GetFlippedDirection(Block.CubeBlock.Orientation.Up));
			if (blockAccel.Z > 0f)
				result.Z = blockAccel.Z * Block.Physics.Mass / Thrust.GetForceInDirection(Base6Directions.GetFlippedDirection(Block.CubeBlock.Orientation.Forward));
			else if (blockAccel.Z < 0f)
				result.Z = blockAccel.Z * Block.Physics.Mass / Thrust.GetForceInDirection(Block.CubeBlock.Orientation.Forward);

			//Log.DebugLog("accel: " + localAccel + ", force ratio: " + result + ", mass: " + Block.Physics.Mass, "ToForceRatio()");

			return result;
		}

		#endregion Move

		#region Rotate

		/// <summary>
		/// Rotate to face the best direction for flight.
		/// </summary>
		public void CalcRotate()
		{
			//Log.DebugLog("entered CalcRotate()", "CalcRotate()");

			//Vector3 pathMoveResult = m_newPathfinder.m_targetDirection;

			//if (!pathMoveResult.IsValid())
			//{
			//	CalcRotate_Stop();
			//	return;
			//}

			// if the ship is limited, it must always face accel direction
			if (!Thrust.CanMoveAnyDirection() && m_moveAccel != Vector3.Zero)
			{
				//Log.DebugLog("limited thrust");
				CalcRotate_Accel();
				return;
			}

			//if (NavSet.Settings_Current.NearingDestination)
			//{
			//	CalcRotate_Stop();
			//	return;
			//}

			if (m_thrustHigh && m_moveAccel != Vector3.Zero)
			{
				m_thrustHigh = false;
				//Log.DebugLog("accel high");
				CalcRotate_Accel();
				return;
			}

			CalcRotate_Hover();
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
			//Log.DebugLog("entered CalcRotate(PseudoBlock block, IMyCubeBlock destBlock, Base6Directions.Direction? forward, Base6Directions.Direction? upward)", "CalcRotate()");

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
			//Log.DebugLog("entered CalcRotate(PseudoBlock block, RelativeDirection3F Direction, RelativeDirection3F UpDirect = null, IMyEntity targetEntity = null)", "CalcRotate()");

			CalcRotate(block.LocalMatrix, Direction, UpDirect, targetEntity: targetEntity);
		}

		/// <summary>
		/// Rotate to face the acceleration direction. If acceleration is zero, invokes CalcRotate_Stop.
		/// </summary>
		public void CalcRotate_Accel()
		{
			if (m_moveAccel == Vector3.Zero)
			{
				CalcRotate_Stop();
				return;
			}
		
			if (SignificantGravity())
				CalcRotate_InGravity(RelativeDirection3F.FromBlock(Block.CubeBlock, m_moveAccel));
			else
				CalcRotate(Thrust.Standard.LocalMatrix, RelativeDirection3F.FromBlock(Block.CubeBlock, m_moveAccel));
		}

		/// <summary>
		/// Calculate the best rotation to stop the ship.
		/// </summary>
		/// TODO: if ship cannot rotate quickly, find a reasonable alternative to facing primary
		public void CalcRotate_Stop()
		{
			//Log.DebugLog("entered CalcRotate_Stop()", "CalcRotate_Stop()");

			Vector3 linearVelocity = LinearVelocity;
			if (!Thrust.CanMoveAnyDirection() || linearVelocity.LengthSquared() > 1f)
			{
				//Log.DebugLog("rotate to stop");

				if (SignificantGravity())
					CalcRotate_InGravity(RelativeDirection3F.FromWorld(Block.CubeGrid, -linearVelocity));
				else
					CalcRotate(Thrust.Standard.LocalMatrix, RelativeDirection3F.FromWorld(Block.CubeGrid, -linearVelocity));

				return;
			}

			CalcRotate_Hover();
		}

		/// <summary>
		/// When in space, stops rotation. When in gravity, rotate to hover.
		/// </summary>
		private void CalcRotate_Hover()
		{
			if (SignificantGravity())
			{
				float accel = Thrust.SecondaryForce / Block.Physics.Mass;
				if (accel > Thrust.GravityStrength)
				{
					Log.DebugLog("facing secondary away from gravity, secondary: " + Thrust.Gravity.LocalMatrix.Forward + ", away from gravity: " + (-Thrust.LocalGravity.vector));
					CalcRotate(Thrust.Gravity.LocalMatrix, RelativeDirection3F.FromLocal(Block.CubeGrid, -Thrust.LocalGravity.vector), gravityAdjusted: true);
				}
				else
				{
					Log.DebugLog("facing primary away from gravity, primary: " + Thrust.Standard.LocalMatrix.Forward + ", away from gravity: " + (-Thrust.LocalGravity.vector));
					CalcRotate(Thrust.Standard.LocalMatrix, RelativeDirection3F.FromLocal(Block.CubeGrid, -Thrust.LocalGravity.vector), gravityAdjusted: true);
				}

				return;
			}

			//Log.DebugLog("stopping rotation");
			StopRotate();
		}

		private void CalcRotate_InGravity(RelativeDirection3F direction)
		{
			//Log.DebugLog("entered CalcRotate_InGravity(RelativeDirection3F direction)", "CalcRotate_InGravity()");

			float secondaryAccel = Thrust.SecondaryForce / Block.Physics.Mass;

			RelativeDirection3F fightGrav = RelativeDirection3F.FromLocal(Block.CubeGrid, -Thrust.LocalGravity.vector);
			if (secondaryAccel > Thrust.GravityStrength)
			{
				// secondary thrusters are strong enough to fight gravity
				if (Vector3.Dot(direction.ToLocalNormalized(), fightGrav.ToLocalNormalized()) > 0f)
				{
					// direction is away from gravity
					Log.DebugLog("Facing primary towards direction(" + direction.ToLocalNormalized() + "), rolling secondary away from gravity(" + fightGrav.ToLocalNormalized() + ")");
					CalcRotate(Thrust.Standard.LocalMatrix, direction, fightGrav, gravityAdjusted: true);
					return;
				}
				else
				{
					// direction is towards gravity
					Log.DebugLog("Facing secondary away from gravity(" + fightGrav.ToLocalNormalized() + "), rolling primary towards direction(" + direction.ToLocalNormalized() + ")");
					CalcRotate(Thrust.Gravity.LocalMatrix, fightGrav, direction, gravityAdjusted: true);
					return;
				}
			}

			// secondary thrusters are not strong enough to fight gravity

			if (secondaryAccel > 1f)
			{
				Log.DebugLog("Facing primary towards gravity(" + fightGrav.ToLocalNormalized() + "), rolling secondary towards direction(" + direction.ToLocalNormalized() + ")");
				CalcRotate(Thrust.Standard.LocalMatrix, fightGrav, direction, gravityAdjusted: true);
				return;
			}

			// helicopter

			float primaryAccel = Math.Max(Thrust.PrimaryForce / Block.Physics.Mass - Thrust.GravityStrength, 1f); // obviously less than actual value but we do not need maximum theoretical acceleration

			DirectionGrid moveAccel = m_moveAccel != Vector3.Zero ? ((DirectionBlock)m_moveAccel).ToGrid(Block.CubeBlock) : LinearVelocity.ToGrid(Block.CubeGrid) * -0.5f;

			if (moveAccel.vector.LengthSquared() > primaryAccel * primaryAccel)
			{
				//Log.DebugLog("move accel is over available acceleration: " + moveAccel.vector.Length() + " > " + primaryAccel, "CalcRotate_InGravity()");
				moveAccel = Vector3.Normalize(moveAccel.vector) * primaryAccel;
			}

			//Log.DebugLog("Facing primary away from gravity and towards direction, moveAccel: " + moveAccel + ", fight gravity: " + fightGrav.ToLocal() + ", direction: " + direction.ToLocalNormalized() + ", new direction: " + (moveAccel + fightGrav.ToLocal()));

			Vector3 dirV = moveAccel + fightGrav.ToLocal();
			direction = RelativeDirection3F.FromLocal(Block.CubeGrid, dirV);
			CalcRotate(Thrust.Standard.LocalMatrix, direction, gravityAdjusted: true);

			Vector3 fightGravDirection = fightGrav.ToLocal() / Thrust.GravityStrength;

			// determine acceleration needed in forward direction to move to desired altitude or remain at current altitude
			float projectionMagnitude = Vector3.Dot(dirV, fightGravDirection) / Vector3.Dot(Thrust.Standard.LocalMatrix.Forward, fightGravDirection);
			Vector3 projectionDirection = ((DirectionGrid)Thrust.Standard.LocalMatrix.Forward).ToBlock(Block.CubeBlock);

			m_moveForceRatio = projectionDirection * projectionMagnitude * Block.CubeGrid.Physics.Mass / Thrust.PrimaryForce;
			Log.DebugLog("changed moveForceRatio, projectionMagnitude: " + projectionMagnitude + ", projectionDirection: " + projectionDirection + ", moveForceRatio: " + m_moveForceRatio);
		}

		// necessary wrapper for main CalcRotate, should always be called.
		private void CalcRotate(Matrix localMatrix, RelativeDirection3F Direction, RelativeDirection3F UpDirect = null, bool gravityAdjusted = false, IMyEntity targetEntity = null)
		{
			//Log.DebugLog("entered CalcRotate(Matrix localMatrix, RelativeDirection3F Direction, RelativeDirection3F UpDirect = null, bool levelingOff = false, IMyEntity targetEntity = null)", "CalcRotate()");

			CheckGrid();
			Thrust.Update();

			if (!gravityAdjusted && ThrustersOverWorked())
			{
				CalcRotate_InGravity(Direction);
				return;
			}

			in_CalcRotate(localMatrix, Direction, UpDirect, targetEntity);
		}

		/// <summary>
		/// Calculates the force necessary to rotate the grid. Two degrees of freedom are used to rotate forward toward Direction; the remaining degree is used to face upward towards UpDirect.
		/// </summary>
		/// <param name="localMatrix">The matrix to rotate to face the direction, use a block's local matrix or result of GetMatrix()</param>
		/// <param name="Direction">The direction to face the localMatrix in.</param>
		private void in_CalcRotate(Matrix localMatrix, RelativeDirection3F Direction, RelativeDirection3F UpDirect, IMyEntity targetEntity)
		{
			Log.DebugLog("Direction == null", Logger.severity.ERROR, condition: Direction == null);

			m_gyro.Update();
			float minimumMoment = Math.Min(m_gyro.InvertedInertiaMoment.Min(), MaxInverseTensor);
			if (minimumMoment <= 0f)
			{
				// == 0f, not calculated yet. < 0f, we have math failure
				StopRotate();
				Log.DebugLog("minimumMoment < 0f", Logger.severity.FATAL, condition: minimumMoment < 0f);
				return;
			}

			localMatrix.M41 = 0; localMatrix.M42 = 0; localMatrix.M43 = 0; localMatrix.M44 = 1;
			Matrix inverted; Matrix.Invert(ref localMatrix, out inverted);

			localMatrix = localMatrix.GetOrientation();
			inverted = inverted.GetOrientation();

			Vector3 localDirect = Direction.ToLocalNormalized();
			Vector3 rotBlockDirect; Vector3.Transform(ref localDirect, ref inverted, out rotBlockDirect);

			float azimuth, elevation; Vector3.GetAzimuthAndElevation(rotBlockDirect, out azimuth, out elevation);

			Vector3 rotaRight = localMatrix.Right;
			Vector3 rotaUp = localMatrix.Up;

			Vector3 NFR_right = Base6Directions.GetVector(Block.CubeBlock.LocalMatrix.GetClosestDirection(ref rotaRight));
			Vector3 NFR_up = Base6Directions.GetVector(Block.CubeBlock.LocalMatrix.GetClosestDirection(ref rotaUp));

			Vector3 displacement = -elevation * NFR_right - azimuth * NFR_up;

			if (UpDirect != null)
			{
				Vector3 upLocal = UpDirect.ToLocalNormalized();
				Vector3 upRotBlock; Vector3.Transform(ref upLocal, ref inverted, out upRotBlock);
				upRotBlock.Z = 0f;
				upRotBlock.Normalize();
				float roll = Math.Sign(upRotBlock.X) * (float)Math.Acos(MathHelper.Clamp(upRotBlock.Y, -1f, 1f));

				Vector3 rotaBackward = localMatrix.Backward;
				Vector3 NFR_backward = Base6Directions.GetVector(Block.CubeBlock.LocalMatrix.GetClosestDirection(ref rotaBackward));

				//Log.DebugLog("upLocal: " + upLocal + ", upRotBlock: " + upRotBlock + ", roll: " + roll + ", displacement: " + displacement + ", NFR_backward: " + NFR_backward + ", change: " + roll * NFR_backward, "in_CalcRotate()");

				displacement += roll * NFR_backward;
			}

			m_lastMoveAttempt = Globals.UpdateCount;
			RotateCheck.TestRotate(displacement);

			float distanceAngle = displacement.Length();
			//if (distanceAngle < m_bestAngle || float.IsNaN(NavSet.Settings_Current.DistanceAngle))
			//{
			//	m_bestAngle = distanceAngle;
			//	if (RotateCheck.ObstructingEntity == null)
			//		m_lastAccel = Globals.UpdateCount;
			//}
			NavSet.Settings_Task_NavWay.DistanceAngle = distanceAngle;

			//Log.DebugLog("localDirect: " + localDirect + ", rotBlockDirect: " + rotBlockDirect + ", elevation: " + elevation + ", NFR_right: " + NFR_right + ", azimuth: " + azimuth + ", NFR_up: " + NFR_up + ", disp: " + displacement, "in_CalcRotate()");

			m_rotateTargetVelocity = MaxAngleVelocity(displacement, minimumMoment, targetEntity != null);

			// adjustment to face a moving entity
			if (targetEntity != null)
			{
				Vector3 relativeLinearVelocity = (targetEntity.GetLinearVelocity() - LinearVelocity) * 1.1f;
				float distance = Vector3.Distance(targetEntity.GetCentre(), Block.CubeBlock.GetPosition());

				//Log.DebugLog("relativeLinearVelocity: " + relativeLinearVelocity + ", tangentialVelocity: " + tangentialVelocity + ", localTangVel: " + localTangVel, "in_CalcRotate()");

				float RLV_pitch = Vector3.Dot(relativeLinearVelocity, Block.CubeBlock.WorldMatrix.Down);
				float RLV_yaw = Vector3.Dot(relativeLinearVelocity, Block.CubeBlock.WorldMatrix.Right);
				float angl_pitch = (float)Math.Atan2(RLV_pitch, distance);
				float angl_yaw = (float)Math.Atan2(RLV_yaw, distance);

				Log.DebugLog("relativeLinearVelocity: " + relativeLinearVelocity + ", RLV_yaw: " + RLV_yaw + ", RLV_pitch: " + RLV_pitch + ", angl_yaw: " + angl_yaw + ", angl_pitch: " + angl_pitch + ", total adjustment: " + (NFR_right * angl_pitch + NFR_up * angl_yaw));

				m_rotateTargetVelocity += NFR_right * angl_pitch + NFR_up * angl_yaw;
			}
			//Log.DebugLog("targetVelocity: " + m_rotateTargetVelocity, "in_CalcRotate()");

			if (RotateCheck.ObstructingEntity != null)
			{
				float maxVel = (float)Math.Atan2(1d, Block.CubeGrid.LocalVolume.Radius);
				float lenSq = m_rotateTargetVelocity.LengthSquared();
				if (lenSq > maxVel)
				{
					Log.DebugLog("Reducing target velocity from " + Math.Sqrt(lenSq) + " to " + maxVel);
					Vector3 normVel; Vector3.Divide(ref m_rotateTargetVelocity, (float)Math.Sqrt(lenSq), out normVel);
					Vector3.Multiply(ref normVel, maxVel, out m_rotateTargetVelocity);
				}
			}

			// angular velocity is reversed
			Vector3 angularVelocity = AngularVelocity.ToBlock(Block.CubeBlock);// ((DirectionWorld)(-Block.Physics.AngularVelocity)).ToBlock(Block.CubeBlock);
			m_rotateForceRatio = (m_rotateTargetVelocity + angularVelocity) / (minimumMoment * m_gyro.GyroForce);

			//Log.DebugLog("targetVelocity: " + m_rotateTargetVelocity + ", angularVelocity: " + angularVelocity + ", accel: " + (m_rotateTargetVelocity + angularVelocity));
			//Log.DebugLog("minimumMoment: " + minimumMoment + ", force: " + m_gyro.GyroForce + ", rotateForceRatio: " + m_rotateForceRatio);

			// dampeners
			for (int index = 0; index < 3; index++)
			{
				// if targetVelocity is close to 0, use dampeners

				float target = m_rotateTargetVelocity.GetDim(index);
				if (target > -0.01f && target < 0.01f)
				{
					//Log.DebugLog("target near 0 for " + i + ", " + target, "in_CalcRotate()");
					m_rotateTargetVelocity.SetDim(index, 0f);
					m_rotateForceRatio.SetDim(index, 0f);
					continue;
				}

			}
		}

		/// <summary>
		/// Calulates the maximum angular velocity to stop at the destination.
		/// </summary>
		/// <param name="disp">Displacement to the destination.</param>
		/// <returns>The maximum angular velocity to stop at the destination.</returns>
		private Vector3 MaxAngleVelocity(Vector3 disp, float minimumMoment, bool fast)
		{
			Vector3 result = Vector3.Zero;

			// S.E. provides damping for angular motion, we will ignore this
			float accel = -minimumMoment * m_gyro.GyroForce;

			for (int index = 0; index < 3; index++)
			{
				float dim = disp.GetDim(index);
				if (dim > 0)
					result.SetDim(index, MaxAngleSpeed(accel, dim, fast));
				else if (dim < 0)
					result.SetDim(index, -MaxAngleSpeed(accel, -dim, fast));
			}

			return result;
		}

		/// <summary>
		/// Calculates the maximum angular speed to stop at the destination.
		/// </summary>
		/// <param name="accel">negative number, maximum deceleration</param>
		/// <param name="dist">positive number, distance to travel</param>
		/// <returns>The maximum angular speed to stop at the destination</returns>
		private float MaxAngleSpeed(float accel, float dist, bool fast)
		{
			//Log.DebugLog("accel: " + accel + ", dist: " + dist, "MaxAngleSpeed()");
			float actual = (float)Math.Sqrt(-2 * accel * dist);
			if (fast)
				return Math.Min(actual, dist * 10f);
			return Math.Min(actual, dist * 2f);
		}

		#endregion Rotate

		/// <summary>
		/// Apply the calculated force ratios to the controller.
		/// </summary>
		public void MoveAndRotate()
		{
			CheckGrid();

			//Log.DebugLog("moveForceRatio: " + moveForceRatio + ", rotateForceRatio: " + rotateForceRatio + ", move length: " + moveForceRatio.Length(), "MoveAndRotate()");

			// if all the force ratio values are 0, Autopilot has to stop the ship, MoveAndRotate will not
			if (m_moveForceRatio == Vector3.Zero && m_rotateTargetVelocity == Vector3.Zero)
			{
				//Log.DebugLog("Stopping the ship, move: " + m_moveForceRatio + ", rotate: " + m_rotateTargetVelocity);
				// should not toggle dampeners, grid may just have landed
				MoveAndRotateStop(false);
				return;
			}

			if (m_moveForceRatio != Vector3.Zero && CheckStuck(WriggleAfter))
			{
				ulong upWoMove = Globals.UpdateCount - m_lastAccel;

				if (m_rotateForceRatio != Vector3.Zero)
				{
					// wriggle
					float wriggle = (upWoMove - WriggleAfter) * 0.0001f;

					//Log.DebugLog("wriggle: " + wriggle + ", updates w/o moving: " + upWoMove);

					m_rotateForceRatio.X += (0.5f - (float)Globals.Random.NextDouble()) * wriggle;
					m_rotateForceRatio.Y += (0.5f - (float)Globals.Random.NextDouble()) * wriggle;
					m_rotateForceRatio.Z += (0.5f - (float)Globals.Random.NextDouble()) * wriggle;
				}

				// increase force
				m_moveForceRatio *= 1f + (upWoMove - WriggleAfter) * 0.1f;
			}

			// clamp values
			Vector3 moveControl;
			moveControl.X = MathHelper.Clamp(m_moveForceRatio.X, -1f, 1f);
			moveControl.Y = MathHelper.Clamp(m_moveForceRatio.Y, -1f, 1f);
			moveControl.Z = MathHelper.Clamp(m_moveForceRatio.Z, -1f, 1f);

			Vector3 rotateControl = -m_rotateForceRatio; // control torque is opposite of move indicator
			Vector3.ClampToSphere(ref rotateControl, 1f);

			m_stopped = false;
			MyShipController controller = Block.Controller;
			MyAPIGateway.Utilities.TryInvokeOnGameThread(() => {

				//Log.DebugLog("rotate control: " + rotateControl + ", previous: " + m_prevRotateControl + ", delta: " + (rotateControl - m_prevRotateControl), "MoveAndRotate()");

				if (Block.Controller.GridGyroSystem != null)
				{
					DirectionGrid gridRotate = ((DirectionBlock)rotateControl).ToGrid(Block.CubeBlock);
					Block.Controller.GridGyroSystem.ControlTorque = gridRotate;
				}
				else
					Log.DebugLog("No gyro system");

				MyEntityThrustComponent thrustComponent = Block.CubeGrid.Components.Get<MyEntityThrustComponent>();
				if (thrustComponent != null)
				{
					DirectionGrid gridMove = ((DirectionBlock)moveControl).ToGrid(Block.CubeBlock);
					thrustComponent.ControlThrust = gridMove;
				}
				else
					Log.DebugLog("No thrust component");
			});
		}

		private void CheckGrid()
		{
			if (m_grid != Block.CubeGrid)
			{
				Log.DebugLog("Grid Changed! from " + m_grid.getBestName() + " to " + Block.CubeGrid.getBestName(), Logger.severity.INFO, condition: m_grid != null);
				m_grid = Block.CubeGrid;
				this.Thrust = new ThrustProfiler(Block.CubeBlock);
				this.m_gyro = new GyroProfiler(m_grid);
			}
		}

		/// <summary>
		/// Ship is in danger of being brought down by gravity.
		/// </summary>
		public bool ThrustersOverWorked(float threshold = OverworkedThreshold)
		{
			Log.DebugLog("myThrust == null", Logger.severity.FATAL, condition: Thrust == null);
			return Thrust.GravityReactRatio.vector.AbsMax() >= threshold;
		}

		/// <summary>
		/// Ship may want to adjust for gravity.
		/// </summary>
		public bool SignificantGravity()
		{
			return Thrust.GravityStrength > 1f;
		}

		public void SetControl(bool enable)
		{
			if (Block.AutopilotControl != enable)
			{
				Log.DebugLog("setting control, AutopilotControl: " + Block.AutopilotControl + ", enable: " + enable);
				MyAPIGateway.Utilities.TryInvokeOnGameThread(() => {
					if (!enable)
					{
						Block.Controller.MoveAndRotateStopped();
						Thrust.ClearOverrides();
						//m_gyro.ClearOverrides();
					}
					Block.AutopilotControl = enable;
				});
			}
		}

		public void SetDamping(bool enable)
		{
			Sandbox.Game.Entities.IMyControllableEntity control = Block.Controller as Sandbox.Game.Entities.IMyControllableEntity;
			if (control.EnabledDamping != enable)
			{
				Log.TraceLog("setting damp, EnabledDamping: " + control.EnabledDamping + ", enable: " + enable);
				MyAPIGateway.Utilities.TryInvokeOnGameThread(() => {
					if (control.EnabledDamping != enable)
						control.SwitchDamping();
				});
			}
		}

	}
}
