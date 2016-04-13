using System;
using Rynchodon.Autopilot.Data;
using Rynchodon.Autopilot.Navigator;
using Rynchodon.Utility.Vectors;
using Sandbox.Game.Entities;
using Sandbox.ModAPI;
using VRage.Game.ModAPI;
using VRage.ModAPI;
using VRageMath;

namespace Rynchodon.Autopilot.Movement
{
	/// <summary>
	/// Performs the movement calculations and the movements.
	/// </summary>
	/// Old calculations for ship controls will stay for now, hopefully Keen will give mods access again.
	public class Mover
	{
		#region S.E. Constants

		private const float pixelsForMaxRotation = 20;
		private const float RollControlMultiplier = 0.2f;

		#endregion

		/// <summary>Fraction of force used to calculate maximum speed.</summary>
		public const float AvailableForceRatio = 0.5f;
		public const float OverworkedThreshold = 0.75f;

		/// <summary>Only update torque accel ratio when updates are at least this close together.</summary>
		private const float MaxUpdateSeconds = Globals.UpdatesPerSecond;

		private const ulong WriggleAfter = 500ul, StuckAfter = WriggleAfter + 2000ul, MoveAwayAfter = StuckAfter + 100ul;

		private readonly Logger m_logger;
		private readonly AllNavigationSettings m_navSet;

		private IMyCubeGrid m_grid;
		private GyroProfiler m_gyro;

		private Vector3 m_moveForceRatio = Vector3.Zero;
		private Vector3 m_rotateTargetVelocity = Vector3.Zero;
		//private Vector3 m_rotateForceRatio = Vector3.Zero;
		private Vector3 m_moveAccel;
		//private Vector3 m_prevMoveControl = Vector3.Zero;
		//private Vector2 m_prevRotateControl = Vector2.Zero;
		//private float m_prevRollControl = 0f;

		private float m_bestDistance = float.MaxValue, m_bestAngle = float.MaxValue;
		private ulong m_lastmove = ulong.MaxValue;

		private bool m_stopped, m_thrustHigh;

		/// <summary>Controlling block for the grid.</summary>
		public readonly ShipControllerBlock Block;
		public ThrustProfiler Thrust { get; private set; }
		public Pathfinder.Pathfinder Pathfinder { get; private set; }

		/// <summary>Value is false iff this Mover is making progress.</summary>
		public bool IsStuck
		{
			get
			{
				return (Globals.UpdateCount - m_lastmove) > StuckAfter;
			}
			set
			{
				if (value)
					m_lastmove = ulong.MinValue;
				else
					m_lastmove = Globals.UpdateCount;
			}
		}

		/// <summary>
		/// Creates a Mover for a given ShipControllerBlock and AllNavigationSettings
		/// </summary>
		/// <param name="block">Controlling block for the grid</param>
		/// <param name="NavSet">Navigation settings to use.</param>
		public Mover(ShipControllerBlock block, AllNavigationSettings NavSet)
		{
			this.m_logger = new Logger("Mover", block.Controller);
			this.Block = block;
			this.m_navSet = NavSet;

			CheckGrid();
		}

		/// <summary>
		/// Stops the thrusters, optionally enables damping.
		/// </summary>
		public void StopMove(bool enableDampeners = true)
		{
			//myLogger.debugLog("entered", "StopMove()");

			m_moveForceRatio = m_moveAccel = Vector3.Zero;
			SetDamping(enableDampeners);
		}

		/// <summary>
		/// Stops the gyros.
		/// </summary>
		public void StopRotate()
		{
			//m_logger.debugLog("stopping rotation", "StopRotate()");

			m_rotateTargetVelocity = Vector3.Zero;
		}

		/// <summary>
		/// Stop movement and rotation of the controller.
		/// </summary>
		/// <param name="enableDampeners">If true, dampeners will be enabled. If false, they will not be toggled.</param>
		public void MoveAndRotateStop(bool enableDampeners = true)
		{
			if (m_stopped)
				return;

			m_logger.debugLog("stopping movement and rotation", "MoveAndRotateStop()");

			m_moveForceRatio = m_moveAccel = Vector3.Zero;
			if (enableDampeners)
				SetDamping(true);

			StopRotate();

			MyAPIGateway.Utilities.TryInvokeOnGameThread(() => {
				//Block.Controller.MoveAndRotateStopped();
				Thrust.ClearOverrides();
				m_gyro.ClearOverrides();
			}, m_logger);

			m_stopped = true;
		}

		/// <summary>
		/// Calculates whether or not the grid has sufficient thrust to accelerate forwards at 1 m/s/s.
		/// </summary>
		/// <returns>True iff there is sufficient thrust available.</returns>
		public bool CanMoveForward()
		{
			CheckGrid();

			return Thrust.GetForceInDirection(Block.CubeBlock.Orientation.Forward, true) > Block.Physics.Mass;
		}

		#region Move

		/// <summary>
		/// Calculates the force necessary to move the grid.
		/// </summary>
		/// <param name="block">To get world position from.</param>
		/// <param name="destPoint">The world position of the destination</param>
		/// <param name="destVelocity">The speed of the destination</param>
		/// <param name="landing">Puts an emphasis on not overshooting the target.</param>
		[Obsolete("Navigators should use double precision")]
		public void CalcMove(PseudoBlock block, Vector3 destPoint, Vector3 destVelocity, bool landing = false)
		{
			CalcMove(block, (Vector3D)destPoint, destVelocity, landing);
		}

		/// <summary>
		/// Calculates the force necessary to move the grid.
		/// </summary>
		/// <param name="block">To get world position from.</param>
		/// <param name="destPoint">The world position of the destination</param>
		/// <param name="destVelocity">The speed of the destination</param>
		/// <param name="landing">Puts an emphasis on not overshooting the target.</param>
		public void CalcMove(PseudoBlock block, Vector3D destPoint, Vector3 destVelocity, bool landing = false)
		{
			const float landingSpeedFactor = 0.2f;

			CheckGrid();

			// using world vectors

			if (m_navSet.Settings_Current.CollisionAvoidance)
			{
				Pathfinder.TestPath(destPoint, landing);
				if (!Pathfinder.CanMove)
				{
					m_logger.debugLog("Pathfinder not allowing movement", "CalcMove()");
					return;
				}
			}

			Thrust.Update();

			Vector3 destDisp = destPoint - block.WorldPosition; // this is why we need double for destPoint
			Vector3 velocity = Block.CubeGrid.Physics.LinearVelocity;

			// switch to using local vectors

			MatrixD positionToLocal = Block.CubeBlock.WorldMatrixNormalizedInv;
			MatrixD directionToLocal = positionToLocal.GetOrientation();

			destVelocity = Vector3.Transform(destVelocity, directionToLocal);
			velocity = Vector3.Transform(velocity, directionToLocal);

			Vector3 targetVelocity;
			float distance;
			if (destDisp != Vector3.Zero)
			{
				destDisp = Vector3.Transform(destDisp, directionToLocal);
				distance = destDisp.Length();

				if (distance + (landing ? landingSpeedFactor : 1f) < m_bestDistance || distance - 10f > m_bestDistance || float.IsNaN(m_navSet.Settings_Current.Distance))
				{
					m_bestDistance = distance;
					m_lastmove = Globals.UpdateCount;
				}

				// while on a planet, take a curved path to target instead of making pathfinder do all the work
				if (SignificantGravity() && distance > 1000f)
				{
					// adjust for curvature of the planet
					Vector3D planetCentre = MyPlanetExtensions.GetClosestPlanet(Block.CubeBlock.GetPosition()).GetCentre();
					float deltaAltitude = (float)(Vector3D.Distance(destPoint, planetCentre) - Vector3D.Distance(Block.CubeBlock.GetPosition(), planetCentre));

					//m_logger.debugLog("displacement: " + destDisp + ", gravity: " + gravityDirection + ", dispReject: " + dispReject + ", delta altitude: " + deltaAltitude +
					//	", altitude adjustment: " + (-gravityDirection * deltaAltitude) + ", new disp: " + (dispReject - gravityDirection * deltaAltitude), "CalcMove()");

					// keep a minimum height from the ground
					float heightAboveSurfaceSquared = (float)Vector3D.DistanceSquared(Block.CubeBlock.GetPosition(), Pathfinder.ClosestSurfacePoint);
					float minimumHeight = Math.Min(1000f, distance * 0.3f);
					deltaAltitude += minimumHeight;
					if (heightAboveSurfaceSquared < minimumHeight * minimumHeight)
					{
						float height = (float)Math.Sqrt(heightAboveSurfaceSquared);
						deltaAltitude += distance * (minimumHeight - height) / height;
					}

					Vector3 gravityDirection = Thrust.WorldGravity.ToBlock(Block.CubeBlock).vector / Thrust.GravityStrength;
					Vector3 dispReject;
					Vector3.Reject(ref destDisp, ref gravityDirection, out dispReject);

					destDisp = dispReject - gravityDirection * deltaAltitude;
				}

				targetVelocity = MaximumVelocity(destDisp);

				// project targetVelocity onto destination direction (take shortest path)
				Vector3 destDir = destDisp / distance;
				targetVelocity = Vector3.Dot(targetVelocity, destDir) * destDir;

				// apply relative speed limit
				float relSpeedLimit = m_navSet.Settings_Current.SpeedMaxRelative;
				if (landing)
				{
					float landingSpeed = Math.Max(distance * landingSpeedFactor, landingSpeedFactor);
					if (relSpeedLimit > landingSpeed)
						relSpeedLimit = landingSpeed;
				}
				if (relSpeedLimit < float.MaxValue)
				{
					float tarSpeedSq_1 = targetVelocity.LengthSquared();
					if (tarSpeedSq_1 > relSpeedLimit * relSpeedLimit)
					{
						targetVelocity *= relSpeedLimit / (float)Math.Sqrt(tarSpeedSq_1);
						//myLogger.debugLog("imposing relative speed limit: " + relSpeedLimit + ", targetVelocity: " + targetVelocity, "CalcMove()");
					}
				}
			}
			else
			{
				targetVelocity = Vector3.Zero;
				distance = 0f;
				m_lastmove = Globals.UpdateCount;
			}

			m_navSet.Settings_Task_NavWay.Distance = distance;
			targetVelocity += destVelocity;

			// apply speed limit
			float tarSpeedSq = targetVelocity.LengthSquared();
			float speedRequest = m_navSet.Settings_Current.SpeedTarget;
			if (tarSpeedSq > speedRequest * speedRequest)
			{
				targetVelocity *= speedRequest / (float)Math.Sqrt(tarSpeedSq);
				//myLogger.debugLog("imposing speed limit: " + speedRequest + ", targetVelocity: " + targetVelocity, "CalcMove()");
			}
			else
			{
				float velocityTowardsDest = velocity.Dot(Vector3.Normalize(destDisp));
				if (velocityTowardsDest * velocityTowardsDest > tarSpeedSq)
					m_navSet.Settings_Task_NavWay.NearingDestination = true;
			}

			m_moveAccel = targetVelocity - velocity;

			DirectionBlock gravBlock = Thrust.WorldGravity.ToBlock(Block.CubeBlock);
			m_moveForceRatio = ToForceRatio(m_moveAccel - gravBlock.vector);

			// dampeners
			bool enableDampeners = false;
			m_thrustHigh = false;

			for (int i = 0; i < 3; i++)
			{
				//const float minForceRatio = 0.1f;
				//const float zeroForceRatio = 0.01f;

				float forceRatio = m_moveForceRatio.GetDim(i);
				
				if (forceRatio < 1f && forceRatio > -1f)
				{
					//if (forceRatio > zeroForceRatio && forceRatio < minForceRatio)
					//	moveForceRatio.SetDim(i, minForceRatio);
					//else if (forceRatio < -zeroForceRatio && forceRatio > -minForceRatio)
					//	moveForceRatio.SetDim(i, -minForceRatio);
					continue;
				}

				m_thrustHigh = true;

				if (float.IsInfinity(forceRatio))
				{
					m_moveForceRatio.SetDim(i, 0f);
					continue;
				}

				// force ratio is > 1 || < -1. If it is useful, use dampeners

				float velDim = velocity.GetDim(i);
				if (velDim < 1f && velDim > -1f)
					continue;

				if (Math.Sign(forceRatio) * Math.Sign(velDim) < 0)
				{
					m_logger.debugLog("damping, i: " + i + ", force ratio: " + forceRatio + ", velocity: " + velDim + ", sign of forceRatio: " + Math.Sign(forceRatio) + ", sign of velocity: " + Math.Sign(velDim), "CalcMove()");
					m_moveForceRatio.SetDim(i, 0);
					enableDampeners = true;
				}
				//else
				//	myLogger.debugLog("not damping, i: " + i + ", force ratio: " + forceRatio + ", velocity: " + velDim + ", sign of forceRatio: " + Math.Sign(forceRatio) + ", sign of velocity: " + Math.Sign(velDim), "CalcMove()");
			}

			SetDamping(enableDampeners);

			m_logger.debugLog(string.Empty
				//+ "block: " + block.Block.getBestName()
				//+ ", dest point: " + destPoint
				//+ ", position: " + block.WorldPosition
				+ "destDisp: " + destDisp
				+ ", destVelocity: " + destVelocity
				+ ", targetVelocity: " + targetVelocity
				+ ", velocity: " + velocity
				+ ", m_moveAccel: " + m_moveAccel
				+ ", moveForceRatio: " + m_moveForceRatio, "CalcMove()");
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

			//myLogger.debugLog("displacement: " + localDisp + ", maximum velocity: " + result, "MaximumVelocity()");

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
				//myLogger.debugLog("No thrust available in direction: " + direct + ", dist: " + dist, "MaximumSpeed()", Logger.severity.DEBUG);
				return dist * 0.1f;
			}
			float accel = -force / Block.Physics.Mass;
			//myLogger.debugLog("direction: " + direct + ", dist: " + dist + ", max accel: " + accel + ", mass: " + Block.Physics.Mass + ", max speed: " + PrettySI.makePretty(Math.Sqrt(-2f * accel * dist)) + " m/s" + ", cap: " + dist * 0.5f + " m/s", "MaximumSpeed()");
			return Math.Min((float)Math.Sqrt(-2f * accel * dist), dist * 0.5f); // capped for the sake of autopilot's reaction time
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

			//myLogger.debugLog("accel: " + localAccel + ", force ratio: " + result + ", mass: " + Block.Physics.Mass, "ToForceRatio()");

			return result;
		}

		#endregion Move

		#region Rotate

		/// <summary>
		/// Rotate to face the best direction for flight.
		/// </summary>
		public void CalcRotate()
		{
			//m_logger.debugLog("entered CalcRotate()", "CalcRotate()");

			// nearing dest check comes first otherwise navigator becomes indecisive
			if (m_navSet.Settings_Current.NearingDestination || (m_navSet.Settings_Current.CollisionAvoidance && !Pathfinder.CanMove))
			{
				m_logger.debugLog(m_navSet.Settings_Current.NearingDestination, "Nearing dest", "CalcRotate()", Logger.severity.TRACE);
				m_logger.debugLog(!m_navSet.Settings_Current.NearingDestination, "Pathfinder blocking", "CalcRotate()", Logger.severity.TRACE);

				CalcRotate_Stop();

				return;
			}

			if (m_thrustHigh && m_moveAccel.LengthSquared() > 0.01f)
			{
				m_thrustHigh = false;
				m_logger.debugLog("thrust high, rotate to accel, m_moveAccel: " + m_moveAccel, "CalcRotate()");

				if (SignificantGravity())
					CalcRotate_InGravity(RelativeDirection3F.FromBlock(Block.CubeBlock, m_moveAccel));
				else
					CalcRotate(Thrust.Standard.LocalMatrix, RelativeDirection3F.FromBlock(Block.CubeBlock, m_moveAccel));

				return;
			}

			CalcRotate_Hover();
		}

		/// <summary>
		/// Calculate the best rotation to stop the ship.
		/// </summary>
		/// TODO: if ship cannot rotate quickly, find a reasonable alternative to facing primary
		public void CalcRotate_Stop()
		{
			//m_logger.debugLog("entered CalcRotate_Stop()", "CalcRotate_Stop()");

			if (m_thrustHigh || Block.Physics.LinearVelocity.LengthSquared() > 1f)
			{
				m_thrustHigh = false;
				m_logger.debugLog("rotate to stop", "CalcRotate_Stop()");

				if (SignificantGravity())
					CalcRotate_InGravity(RelativeDirection3F.FromWorld(Block.CubeGrid, -Block.Physics.LinearVelocity));
				else
					CalcRotate(Thrust.Standard.LocalMatrix, RelativeDirection3F.FromWorld(Block.CubeGrid, -Block.Physics.LinearVelocity));

				return;
			}

			CalcRotate_Hover();
		}

		private void CalcRotate_Hover()
		{
			if (SignificantGravity())
			{
				float accel = Thrust.SecondaryForce / Block.Physics.Mass;
				if (accel > Thrust.GravityStrength)
				{
					m_logger.debugLog("facing secondary away from gravity, secondary: " + Thrust.Gravity.LocalMatrix.Forward + ", away from gravity: " + (-Thrust.LocalGravity.vector), "CalcRotate_Hover()");
					CalcRotate(Thrust.Gravity.LocalMatrix, RelativeDirection3F.FromLocal(Block.CubeGrid, -Thrust.LocalGravity.vector), gravityAdjusted: true);
				}
				else
				{
					m_logger.debugLog("facing primary away from gravity, primary: " + Thrust.Standard.LocalMatrix.Forward + ", away from gravity: " + (-Thrust.LocalGravity.vector), "CalcRotate_Hover()");
					CalcRotate(Thrust.Standard.LocalMatrix, RelativeDirection3F.FromLocal(Block.CubeGrid, -Thrust.LocalGravity.vector), gravityAdjusted: true);
				}

				return;
			}

			m_logger.debugLog("stopping rotation", "CalcRotate_Hover()");
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
			//m_logger.debugLog("entered CalcRotate(PseudoBlock block, IMyCubeBlock destBlock, Base6Directions.Direction? forward, Base6Directions.Direction? upward)", "CalcRotate()");
		
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
			//m_logger.debugLog("entered CalcRotate(PseudoBlock block, RelativeDirection3F Direction, RelativeDirection3F UpDirect = null, IMyEntity targetEntity = null)", "CalcRotate()");
		
			CalcRotate(block.LocalMatrix, Direction, UpDirect, targetEntity: targetEntity);
		}

		// necessary wrapper for main CalcRotate, should always be called.
		private void CalcRotate(Matrix localMatrix, RelativeDirection3F Direction, RelativeDirection3F UpDirect = null, bool gravityAdjusted = false, IMyEntity targetEntity = null)
		{
			//m_logger.debugLog("entered CalcRotate(Matrix localMatrix, RelativeDirection3F Direction, RelativeDirection3F UpDirect = null, bool levelingOff = false, IMyEntity targetEntity = null)", "CalcRotate()");

			CheckGrid();
			Thrust.Update();

			if (!gravityAdjusted && ThrustersOverWorked())
			{
				CalcRotate_InGravity(Direction);
				return;
			}

			in_CalcRotate(localMatrix, Direction, UpDirect, targetEntity);
		}

		private void CalcRotate_InGravity(RelativeDirection3F direction)
		{
			//m_logger.debugLog("entered CalcRotate_InGravity(RelativeDirection3F direction)", "CalcRotate_InGravity()");

			float secondaryAccel = Thrust.SecondaryForce / Block.Physics.Mass;

			RelativeDirection3F fightGrav = RelativeDirection3F.FromLocal(Block.CubeGrid, -Thrust.LocalGravity.vector);
			if (secondaryAccel > Thrust.GravityStrength)
			{
				// secondary thrusters are strong enough to fight gravity
				if (Vector3.Dot(direction.ToLocalNormalized(), fightGrav.ToLocalNormalized()) > 0f)
				{
					// direction is away from gravity
					m_logger.debugLog("Facing primary towards direction, rolling secondary away from gravity", "CalcRotate_InGravity()");
					CalcRotate(Thrust.Standard.LocalMatrix, direction, fightGrav, gravityAdjusted: true);
					return;
				}
				else
				{
					// direction is towards gravity
					m_logger.debugLog("Facing secondary away from gravity, rolling primary towards direction", "CalcRotate_InGravity()");
					CalcRotate(Thrust.Gravity.LocalMatrix, fightGrav, direction, gravityAdjusted: true);
					return;
				}
			}

			// secondary thrusters are not strong enough to fight gravity

			if (secondaryAccel > 1f)
			{
				m_logger.debugLog("Facing primary towards gravity, rolling secondary towards direction", "CalcRotate_InGravity()");
				CalcRotate(Thrust.Standard.LocalMatrix, fightGrav, direction, gravityAdjusted: true);
				return;
			}

			// helicopter

			float primaryAccel = Math.Max(Thrust.PrimaryForce / Block.Physics.Mass - Thrust.GravityStrength, 1f); // obviously less than actual value but we do not need maximum theoretical acceleration

			DirectionGrid moveAccel = m_moveAccel != Vector3.Zero ? ((DirectionBlock)m_moveAccel).ToGrid(Block.CubeBlock) : ((DirectionWorld)(-Block.Physics.LinearVelocity)).ToGrid(Block.CubeGrid);

			if (moveAccel.vector.LengthSquared() > primaryAccel * primaryAccel)
			{
				//myLogger.debugLog("move accel is over available acceleration: " + moveAccel.vector.Length() + " > " + primaryAccel, "CalcRotate_InGravity()");
				moveAccel = Vector3.Normalize(moveAccel.vector) * primaryAccel;
			}

			m_logger.debugLog("Facing primary away from gravity and towards direction, moveAccel: " + moveAccel + ", fight gravity: " + fightGrav.ToLocal() + ", new direction: " + (moveAccel + fightGrav.ToLocal()), "CalcRotate_InGravity()");

			Vector3 dirV = moveAccel + fightGrav.ToLocal();
			direction = RelativeDirection3F.FromLocal(Block.CubeGrid, dirV);
			CalcRotate(Thrust.Standard.LocalMatrix, direction, gravityAdjusted: true);

			Vector3 fightGravDirection = fightGrav.ToLocal() / Thrust.GravityStrength;

			// determine acceleration needed in forward direction to move to desired altitude or remain at current altitude
			float projectionMagnitude = Vector3.Dot(dirV, fightGravDirection) / Vector3.Dot(Thrust.Standard.LocalMatrix.Forward, fightGravDirection);
			Vector3 projectionDirection = ((DirectionGrid)Thrust.Standard.LocalMatrix.Forward).ToBlock(Block.CubeBlock);

			m_moveForceRatio = projectionDirection * projectionMagnitude * Block.CubeGrid.Physics.Mass / Thrust.PrimaryForce;
			m_logger.debugLog("changed moveForceRatio, projectionMagnitude: " + projectionMagnitude + ", projectionDirection: " + projectionDirection + ", moveForceRatio: " + m_moveForceRatio, "CalcRotate_InGravity()");
		}

		/// <summary>
		/// Calculates the force necessary to rotate the grid. Two degrees of freedom are used to rotate forward toward Direction; the remaining degree is used to face upward towards UpDirect.
		/// </summary>
		/// <param name="localMatrix">The matrix to rotate to face the direction, use a block's local matrix or result of GetMatrix()</param>
		/// <param name="Direction">The direction to face the localMatrix in.</param>
		private void in_CalcRotate(Matrix localMatrix, RelativeDirection3F Direction, RelativeDirection3F UpDirect, IMyEntity targetEntity)
		{
			m_logger.debugLog(Direction == null, "Direction == null", "CalcRotate()", Logger.severity.ERROR);

			m_gyro.Update();
			float minimumMoment = m_gyro.InvertedInertiaMoment.Min();
			if (minimumMoment <= 0f)
			{
				// == 0f, not calculated yet. < 0f, we have math failure
				StopRotate();
				m_logger.debugLog(minimumMoment < 0f, "minimumMoment < 0f", "CalcRotate()", Logger.severity.FATAL);
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

				//m_logger.debugLog("upLocal: " + upLocal + ", upRotBlock: " + upRotBlock + ", roll: " + roll + ", displacement: " + displacement + ", NFR_backward: " + NFR_backward + ", change: " + roll * NFR_backward, "CalcRotate()");

				displacement += roll * NFR_backward;
			}

			if (m_navSet.Settings_Current.CollisionAvoidance)
			{
				Pathfinder.TestRotate(displacement);
				if (!Pathfinder.CanRotate)
				{
					m_logger.debugLog("Pathfinder not allowing rotation", "CalcRotate()");
					return;
				}
			}

			float distanceAngle = displacement.Length();
			if (distanceAngle < m_bestAngle || float.IsNaN(m_navSet.Settings_Current.DistanceAngle))
			{
				m_bestAngle = distanceAngle;
				m_lastmove = Globals.UpdateCount;
			}
			m_navSet.Settings_Task_NavWay.DistanceAngle = distanceAngle;

			//myLogger.debugLog("localDirect: " + localDirect + ", rotBlockDirect: " + rotBlockDirect + ", elevation: " + elevation + ", NFR_right: " + NFR_right + ", azimuth: " + azimuth + ", NFR_up: " + NFR_up + ", disp: " + displacement, "CalcRotate()");

			m_rotateTargetVelocity = MaxAngleVelocity(displacement, minimumMoment, targetEntity != null);

			// adjustment to face a moving entity
			if (targetEntity != null)
			{
				Vector3 relativeLinearVelocity = targetEntity.GetLinearVelocity() - Block.Physics.LinearVelocity - Block.Physics.LinearAcceleration * 0.1f;
				float distance = Vector3.Distance(targetEntity.GetCentre(), Block.CubeBlock.GetPosition());

				//myLogger.debugLog("relativeLinearVelocity: " + relativeLinearVelocity + ", tangentialVelocity: " + tangentialVelocity + ", localTangVel: " + localTangVel, "CalcRotate()");

				float RLV_pitch = Vector3.Dot(relativeLinearVelocity, Block.CubeBlock.WorldMatrix.Down);
				float RLV_yaw = Vector3.Dot(relativeLinearVelocity, Block.CubeBlock.WorldMatrix.Right);
				float angl_pitch = (float)Math.Atan2(RLV_pitch, distance);
				float angl_yaw = (float)Math.Atan2(RLV_yaw, distance);

				m_logger.debugLog("relativeLinearVelocity: " + relativeLinearVelocity + ", RLV_yaw: " + RLV_yaw + ", RLV_pitch: " + RLV_pitch + ", angl_yaw: " + angl_yaw + ", angl_pitch: " + angl_pitch + ", total adjustment: " + (NFR_right * angl_pitch + NFR_up * angl_yaw), "CalcRotate()");

				m_rotateTargetVelocity += NFR_right * angl_pitch + NFR_up * angl_yaw;
			}
			m_logger.debugLog("targetVelocity: " + m_rotateTargetVelocity, "CalcRotate()");

			//Vector3 angularVelocity = ((DirectionWorld)(-Block.Physics.AngularVelocity)).ToBlock(Block.CubeBlock);
			//m_rotateForceRatio = (m_rotateTargetVelocity - angularVelocity) / (minimumMoment * m_gyro.GyroForce);

			//m_logger.debugLog("targetVelocity: " + m_rotateTargetVelocity + ", angularVelocity: " + angularVelocity + ", accel: " + (m_rotateTargetVelocity - angularVelocity), "CalcRotate()");
			//m_logger.debugLog("minimumMoment: " + minimumMoment + ", force: " + m_gyro.GyroForce + ", rotateForceRatio: " + m_rotateForceRatio, "CalcRotate()");

			// dampeners
			for (int i = 0; i < 3; i++)
			{
				// if targetVelocity is close to 0, use dampeners

				float target = m_rotateTargetVelocity.GetDim(i);
				if (target > -0.01f && target < 0.01f)
				{
					m_logger.debugLog("target near 0 for " + i + ", " + target, "CalcRotate()");
					m_rotateTargetVelocity.SetDim(i, 0f);
					//m_rotateForceRatio.SetDim(i, 0f);
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

			for (int i = 0; i < 3; i++)
			{
				float dim = disp.GetDim(i);
				if (dim > 0)
					result.SetDim(i, MaxAngleSpeed(accel, dim, fast));
				else if (dim < 0)
					result.SetDim(i, -MaxAngleSpeed(accel, -dim, fast));
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
			//m_logger.debugLog("accel: " + accel + ", dist: " + dist, "MaxAngleSpeed()");
			float actual = (float)Math.Sqrt(-2 * accel * dist);
			if (fast)
				return Math.Min(actual, dist * 30f);
			return Math.Min(actual, dist * 2f);
		}

		#endregion Rotate

		/// <summary>
		/// Apply the calculated force ratios to the controller.
		/// </summary>
		public void MoveAndRotate()
		{
			CheckGrid();

			if (m_navSet.Settings_Current.CollisionAvoidance)
			{
				if (!Pathfinder.CanMove)
				{
					//myLogger.debugLog("Pathfinder not allowing movement", "MoveAndRotate()");
					StopMove();
				}
				if (!Pathfinder.CanRotate)
				{
					//myLogger.debugLog("Pathfinder not allowing rotation", "MoveAndRotate()");
					StopRotate();
				}
			}

			//myLogger.debugLog("moveForceRatio: " + moveForceRatio + ", rotateForceRatio: " + rotateForceRatio + ", move length: " + moveForceRatio.Length(), "MoveAndRotate()");
			MyShipController controller = Block.Controller;

			IMyEntity obstruction = Pathfinder.MoveObstruction ?? Pathfinder.RotateObstruction;
			ulong upWoMove = Globals.UpdateCount - m_lastmove;

			if (m_navSet.Settings_Current.PathfinderCanChangeCourse && upWoMove > MoveAwayAfter)
			{
				// if cannot rotate and not calculating move, move away from obstruction
				if (obstruction != null)
				{
					obstruction = obstruction.GetTopMostParent();
					Vector3 displacement = Block.CubeBlock.GetPosition() - obstruction.GetCentre();
					Vector3 away;
					Vector3.Normalize(ref displacement, out away);
					m_logger.debugLog("Stuck, creating Waypoint to move away from obstruction", "MoveAndRotate()", Logger.severity.INFO);
					new Waypoint(this, m_navSet, AllNavigationSettings.SettingsLevelName.NavWay, obstruction, displacement + away * (10f + m_navSet.Settings_Current.DestinationRadius));
					m_lastmove = Globals.UpdateCount;
				}
			}

			// if all the force ratio values are 0, Autopilot has to stop the ship, MoveAndRotate will not
			if (m_moveForceRatio == Vector3.Zero && m_rotateTargetVelocity == Vector3.Zero)
			{
				m_logger.debugLog("Stopping the ship, move: " + m_moveForceRatio + ", rotate: " + m_rotateTargetVelocity, "MoveAndRotate()");
				// should not toggle dampeners, grid may just have landed
				MoveAndRotateStop(false);
				return;
			}
			else if (obstruction == null && upWoMove > WriggleAfter && Block.Physics.LinearVelocity.LengthSquared() < 1f && m_navSet.Settings_Current.Distance > 1f)
			{
				// if pathfinder is clear and we are not moving, wriggle
				float wriggle = (upWoMove - WriggleAfter) * 0.0001f;

				m_logger.debugLog("wriggle: " + wriggle + ", updates w/o moving: " + upWoMove, "MoveAndRotate()");

				//m_rotateForceRatio.X += (0.5f - (float)Globals.Random.NextDouble()) * wriggle;
				//m_rotateForceRatio.Y += (0.5f - (float)Globals.Random.NextDouble()) * wriggle;
				//m_rotateForceRatio.Z += (0.5f - (float)Globals.Random.NextDouble()) * wriggle;

				m_rotateTargetVelocity.X += (MathHelper.Pi - MathHelper.TwoPi * (float)Globals.Random.NextDouble()) * wriggle;
				m_rotateTargetVelocity.Y += (MathHelper.Pi - MathHelper.TwoPi * (float)Globals.Random.NextDouble()) * wriggle;
				m_rotateTargetVelocity.Z += (MathHelper.Pi - MathHelper.TwoPi * (float)Globals.Random.NextDouble()) * wriggle;

				// increase force
				m_moveForceRatio *= 1f + (upWoMove - WriggleAfter) * 0.01f;
			}

			//// clamp values and invert operations MoveAndRotate will perform
			Vector3 moveControl; m_moveForceRatio.ApplyOperation(dim => MathHelper.Clamp(dim, -1, 1), out moveControl);
			//m_logger.debugLog("m_moveForceRatio: " + m_moveForceRatio + ", moveControl: " + moveControl, "MoveAndRotate()");

			//Vector2 rotateControl = Vector2.Zero;
			//rotateControl.X = MathHelper.Clamp(m_rotateForceRatio.X, -1, 1) * pixelsForMaxRotation;
			//rotateControl.Y = MathHelper.Clamp(m_rotateForceRatio.Y, -1, 1) * pixelsForMaxRotation;

			//float rollControl = MathHelper.Clamp(m_rotateForceRatio.Z, -1, 1) / RollControlMultiplier;

			// gyros use opposite values from MoveAndRotate
			Vector3 rotateRollControl; m_rotateTargetVelocity.ApplyOperation(dim => MathHelper.Clamp(-dim, -MathHelper.TwoPi, MathHelper.TwoPi), out rotateRollControl);

			//m_logger.debugLog("m_rotateForceRatio: " + m_rotateForceRatio + ", rotateControl: " + rotateControl + ", rollControl: " + rollControl, "MoveAndRotate()");

			m_stopped = false;
			//myLogger.debugLog("Queueing Move and Rotate, move: " + moveControl + ", rotate: " + rotateControl + ", roll: " + rollControl + ", unstick: " + unstick, "MoveAndRotate()");
			MyAPIGateway.Utilities.TryInvokeOnGameThread(() => {
				//m_logger.debugLog("Applying Move and Rotate, move: " + moveControl + ", rotate: " + rotateControl + ", roll: " + rollControl, "MoveAndRotate()");

				//controller.MoveAndRotate(moveControl - m_prevMoveControl, rotateControl - m_prevRotateControl, rollControl - m_prevRollControl);
				//m_prevMoveControl = moveControl;
				//m_prevRotateControl = rotateControl;
				//m_prevRollControl = rollControl;

				m_logger.debugLog("Applying Move and Rotate, move: " + moveControl + ", rotate: " + m_rotateTargetVelocity, "MoveAndRotate()");

				DirectionGrid gridMove = ((DirectionBlock)moveControl).ToGrid(Block.CubeBlock);
				Thrust.SetOverrides(ref gridMove);
				DirectionWorld gridRotate = ((DirectionBlock)(rotateRollControl)).ToWorld(Block.CubeBlock);
				m_gyro.SetOverrides(ref gridRotate);
			}, m_logger);
		}

		private void CheckGrid()
		{
			if (m_grid != Block.CubeGrid)
			{
				m_logger.debugLog(m_grid != null, "Grid Changed! from " + m_grid.getBestName() + " to " + Block.CubeGrid.getBestName(), "CheckGrid()", Logger.severity.INFO);
				m_grid = Block.CubeGrid;
				this.Thrust = new ThrustProfiler(Block.CubeBlock);
				this.m_gyro = new GyroProfiler(m_grid);
				this.Pathfinder = new Pathfinder.Pathfinder(m_grid, m_navSet, this);
			}
		}

		/// <summary>
		/// Ship is in danger of being brought down by gravity.
		/// </summary>
		public bool ThrustersOverWorked(float threshold = OverworkedThreshold)
		{
			m_logger.debugLog(Thrust == null, "myThrust == null", "ThrustersOverWorked()", Logger.severity.FATAL);
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
			if (Block.Controller.ControlThrusters != enable)
			{
				m_logger.debugLog("setting control, ControlThrusters: " + Block.Controller.ControlThrusters + ", enable: " + enable, "SetDamping()");
				MyAPIGateway.Utilities.TryInvokeOnGameThread(() => {
					if (!enable)
					{
						//Block.Controller.MoveAndRotateStopped();
						Thrust.ClearOverrides();
						m_gyro.ClearOverrides();
					}
					if (Block.Controller.ControlThrusters != enable)
						// SwitchThrusts() only works for jetpacks
						Block.CubeBlock.ApplyAction("ControlThrusters");
				}, m_logger);
			}
		}

		public void SetDamping(bool enable)
		{
			Sandbox.Game.Entities.IMyControllableEntity control = Block.Controller as Sandbox.Game.Entities.IMyControllableEntity;
			if (control.EnabledDamping != enable)
			{
				m_logger.debugLog("setting damp, EnabledDamping: " + control.EnabledDamping + ", enable: " + enable, "SetDamping()");
				MyAPIGateway.Utilities.TryInvokeOnGameThread(() => {
					if (control.EnabledDamping != enable)
						control.SwitchDamping();
				}, m_logger);
			}
		}

	}
}
