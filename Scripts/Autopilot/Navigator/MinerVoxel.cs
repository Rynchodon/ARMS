using System;
using System.Collections.Generic;
using System.Text;
using Rynchodon.Autopilot.Data;
using Rynchodon.Autopilot.Harvest;
using Rynchodon.Autopilot.Movement;
using Sandbox.Common.ObjectBuilders;
using Sandbox.Game.Entities;
using Sandbox.ModAPI;
using VRage;
using VRage.Game.Entity;
using VRage.Game.ModAPI;
using VRage.ModAPI;
using VRageMath;
using Ingame = Sandbox.ModAPI.Ingame;

namespace Rynchodon.Autopilot.Navigator
{

	/// <summary>
	/// Mines an IMyVoxelBase
	/// Will not insist on rotation control until it is ready to start mining.
	/// </summary>
	public class MinerVoxel : NavigatorMover, INavigatorRotator
	{

		private const float FullAmount_Abort = 0.9f, FullAmount_Return = 0.1f;
		private const float MinAccel_Abort = 0.75f, MinAccel_Return = 1f;
		private const string
			ReturnCause_Full = "Drills are full, need to unload",
			ReturnCause_Heavy = "Ship is too massive, need to unload",
			ReturnCause_OverWorked = "Thrusters overworked, need to unload";

		private enum State : byte { GetTarget, Approaching, Rotating, MoveTo, Mining, Mining_Escape, Mining_Tunnel, Move_Away }

		private readonly Logger m_logger;
		private readonly byte[] OreTargets;
		private readonly float m_longestDimension;

		private MultiBlock<MyObjectBuilder_Drill> m_navDrill;
		private State value_state;
		private LineSegmentD m_approach;
		private Vector3D m_depositPos;
		private Vector3D m_currentTarget;
		/// <summary>For planet, we save a surface point and use that to check if we are near the planet.</summary>
		private Vector3D m_surfacePoint;
		private string m_depositOre;
		private ulong m_nextCheck_drillFull;
		private float m_current_drillFull;
		private float m_closestDistToTarget;

		private IMyVoxelBase value_targetVoxel;
		private IMyVoxelBase m_targetVoxel
		{
			get { return value_targetVoxel; }
			set
			{
				if (value_targetVoxel != null)
					DamageHandler.UnregisterMiner(m_controlBlock.CubeGrid);
				if (value != null)
					DamageHandler.RegisterMiner(m_controlBlock.CubeGrid, value);
				value_targetVoxel = value;
			}
		}

		private bool isMiningPlanet
		{
			get { return m_targetVoxel is MyPlanet; }
		}

		private State m_state
		{
			get
			{ return value_state; }
			set
			{
				m_logger.debugLog("Changing state to " + value);
				value_state = value;
				switch (value)
				{
					case State.GetTarget:
						{
							EnableDrills(false);
							if (DrillFullness() >= FullAmount_Return)
							{
								m_logger.debugLog(ReturnCause_Full);
								m_navSet.OnTaskComplete_NavRot();
								m_mover.StopMove();
								m_mover.StopRotate();
								m_navSet.Settings_Commands.Complaint = ReturnCause_Full;
								return;
							}
							if (GetAcceleration() < MinAccel_Return)
							{
								m_logger.debugLog(ReturnCause_Heavy);
								m_navSet.OnTaskComplete_NavRot();
								m_mover.StopMove();
								m_mover.StopRotate();
								m_navSet.Settings_Commands.Complaint = ReturnCause_Heavy;
								return;
							}
							if (m_mover.ThrustersOverWorked(Mover.OverworkedThreshold - 0.1f))
							{
								m_logger.debugLog(ReturnCause_OverWorked);
								m_navSet.OnTaskComplete_NavRot();
								m_mover.StopMove();
								m_mover.StopRotate();
								m_navSet.Settings_Commands.Complaint = ReturnCause_OverWorked;
								return;
							}
							// request ore detector update
							m_logger.debugLog("Requesting ore update");
							m_navSet.OnTaskComplete_NavMove();
							OreDetector.SearchForMaterial(m_mover.Block, OreTargets, OnOreSearchComplete);
						}
						break;
					case State.Approaching:
						m_currentTarget = m_approach.From;
						break;
					case State.Rotating:
						m_currentTarget = m_depositPos;
						break;
					case State.MoveTo:
						m_currentTarget = m_approach.To;
						m_navSet.Settings_Task_NavMove.IgnoreAsteroid = true;
						break;
					case State.Mining:
						{
							EnableDrills(true);
							Vector3D pos = m_navDrill.WorldPosition;
							m_currentTarget = pos + (m_depositPos - pos) * 2f;
							m_navSet.Settings_Task_NavMove.SpeedTarget = 1f;
							break;
						}
					case State.Mining_Escape:
						EnableDrills(false);
						GetExteriorPoint(m_navDrill.WorldPosition, m_navDrill.WorldMatrix.Forward, m_longestDimension * 2f, point => m_currentTarget = point);
						break;
					case State.Mining_Tunnel:
						if (isMiningPlanet)
						{
							m_logger.debugLog("Cannot tunnel through a planet, care to guess why?");
							m_state = State.Mining_Escape;
							return;
						}
						EnableDrills(true);
						GetExteriorPoint(m_navDrill.WorldPosition, m_navDrill.WorldMatrix.Backward, m_longestDimension * 2f, point => m_currentTarget = point);
						break;
					case State.Move_Away:
						{
							EnableDrills(false);
							m_navSet.Settings_Task_NavMove.SpeedTarget = 10f;
							Vector3D pos = m_navDrill.WorldPosition;
							m_currentTarget = pos + Vector3D.Normalize(pos - m_targetVoxel.GetCentre()) * 100d;
							m_navSet.Settings_Task_NavMove.IgnoreAsteroid = false;
							break;
						}
					default:
						VRage.Exceptions.ThrowIf<NotImplementedException>(true, "State not implemented: " + value);
						break;
				}
				m_logger.debugLog("Current target: " + m_currentTarget + ", current position: " + m_navDrill.WorldPosition);
				m_mover.StopMove();
				m_mover.StopRotate();
				m_mover.IsStuck = false;
				m_navSet.OnTaskComplete_NavWay();
			}
		}

		public MinerVoxel(Mover mover, AllNavigationSettings navSet, byte[] OreTargets)
			: base(mover, navSet)
		{
			this.m_logger = new Logger(GetType().Name, m_controlBlock.CubeBlock, () => m_state.ToString());
			this.OreTargets = OreTargets;

			// get blocks
			var cache = CubeGridCache.GetFor(m_controlBlock.CubeGrid);

			var allDrills = cache.GetBlocksOfType(typeof(MyObjectBuilder_Drill));
			if (allDrills == null || allDrills.Count == 0)
			{
				m_logger.debugLog("No Drills!", Logger.severity.INFO);
				return;
			}

			// if a drill has been chosen by player, use it
			PseudoBlock navBlock = m_navSet.Settings_Current.NavigationBlock;
			if (navBlock.Block is IMyShipDrill)
				m_navDrill = new MultiBlock<MyObjectBuilder_Drill>(navBlock.Block);
			else
				m_navDrill = new MultiBlock<MyObjectBuilder_Drill>(() => m_mover.Block.CubeGrid);

			if (m_navDrill.FunctionalBlocks == 0)
			{
				m_logger.debugLog("no working drills", Logger.severity.INFO);
				return;
			}

			m_longestDimension = m_controlBlock.CubeGrid.GetLongestDim();

			m_navSet.Settings_Task_NavRot.NavigatorMover = this;

			// check for currently touching voxel, usually resume from save
			BoundingSphereD nearby = new BoundingSphereD(m_navDrill.WorldPosition, m_longestDimension * 4d);
			List<MyVoxelBase> nearbyVoxels = new List<MyVoxelBase>();
			MyGamePruningStructure.GetAllVoxelMapsInSphere(ref nearby, nearbyVoxels);

			foreach (MyVoxelBase voxel in nearbyVoxels)
				// skip planet physics, ship should be near planet as well
				if (voxel is IMyVoxelMap || voxel is MyPlanet)
				{
					m_logger.debugLog("near a voxel, escape first", Logger.severity.DEBUG);
					m_targetVoxel = voxel;
					m_state = State.Mining_Escape;
					var setLevel = m_navSet.GetSettingsLevel(AllNavigationSettings.SettingsLevelName.NavMove);
					setLevel.IgnoreAsteroid = true;
					setLevel.SpeedTarget = 1f;
					return;
				}

			m_state = State.GetTarget;
		}

		~MinerVoxel()
		{
			m_targetVoxel = null;
		}

		public override void Move()
		{
			if (m_state != State.Mining_Escape && m_navDrill.FunctionalBlocks == 0)
			{
				m_logger.debugLog("No drills, must escape!");
				m_state = State.Mining_Escape;
			}

			switch (m_state)
			{
				case State.GetTarget:
					m_mover.StopMove();
					return;
				case State.Approaching:
					// measure distance from line, but move to a point
					// ok to adjust m_approach itself but we cannot move to closest point on line
					if (m_approach.DistanceSquared(m_navDrill.WorldPosition) < m_longestDimension * m_longestDimension)
					{
						m_logger.debugLog("Finished approach", Logger.severity.DEBUG);
						m_state = State.Rotating;
						return;
					}
					if (m_mover.IsStuck)
					{
						m_logger.debugLog("Stuck", Logger.severity.DEBUG);
						m_state = State.Mining_Escape;
						return;
					}
					break;
				case State.Rotating:
					m_mover.StopMove();
					if (m_mover.IsStuck)
					{
						m_logger.debugLog("Stuck", Logger.severity.DEBUG);
						m_state = State.Mining_Escape;
						return;
					}
					return;
				case State.MoveTo:
					if (m_navSet.Settings_Current.Distance < m_longestDimension)
					{
						m_logger.debugLog("Reached target voxel", Logger.severity.DEBUG);
						m_state = State.Mining;
						return;
					}
					if (m_mover.IsStuck)
					{
						m_logger.debugLog("Stuck", Logger.severity.DEBUG);
						m_state = State.Mining_Escape;
						return;
					}
					break;
				case State.Mining:
					// do not check for inside asteroid as we may not have reached it yet and target is inside asteroid
					if (DrillFullness() > FullAmount_Abort)
					{
						m_logger.debugLog("Drills are full, aborting", Logger.severity.DEBUG);
						m_state = State.Mining_Escape;
						return;
					}
					if (GetAcceleration() < MinAccel_Abort)
					{
						m_logger.debugLog("Ship is heavy, aborting", Logger.severity.DEBUG);
						m_state = State.Mining_Escape;
						return;
					}
					if (m_mover.ThrustersOverWorked())
					{
						m_logger.debugLog("Thrusters overworked, aborting", Logger.severity.DEBUG);
						m_state = State.Mining_Escape;
						return;
					}
					if (m_navSet.Settings_Current.Distance < 1f)
					{
						m_logger.debugLog("Reached position: " + m_currentTarget, Logger.severity.DEBUG);
						m_state = State.Mining_Escape;
						return;
					}

					if (m_mover.IsStuck)
					{
						m_logger.debugLog("Stuck", Logger.severity.DEBUG);
						m_state = State.Mining_Escape;
						return;
					}

					break;
				case State.Mining_Escape:
					if (!IsNearVoxel(2d))
					{
						m_logger.debugLog("left voxel");
						m_state = State.Move_Away;
						return;
					}

					if (m_mover.IsStuck)
					{
						m_logger.debugLog("Stuck");
						m_state = State.Mining_Tunnel;
						return;
					}

					if (m_navSet.Settings_Current.Distance < 1f)
					{
						Vector3 pos = m_navDrill.WorldPosition;
						m_currentTarget = m_navDrill.WorldPosition + m_navDrill.WorldMatrix.Backward * 100d;
					}

					break;
				case State.Mining_Tunnel:
					if (!IsNearVoxel(2d))
					{
						m_logger.debugLog("left voxel");
						m_state = State.Move_Away;
						return;
					}

					if (m_mover.IsStuck)
					{
						m_logger.debugLog("Stuck", Logger.severity.DEBUG);
						m_state = State.Mining_Escape;
						return;
					}

					if (m_navSet.Settings_Current.Distance < 1f)
					{
						Vector3 pos = m_navDrill.WorldPosition;
						m_currentTarget = m_navDrill.WorldPosition + m_navDrill.WorldMatrix.Forward * 100d;
					}

					break;
				case State.Move_Away:
					if (m_targetVoxel == null)
					{
						m_logger.debugLog("no target voxel");
						m_state = State.GetTarget;
						return;
					}
					if (!IsNearVoxel(4d))
					{
						m_logger.debugLog("far enough away");
						m_state = State.GetTarget;
						return;
					}
					if (m_mover.IsStuck)
					{
						m_logger.debugLog("Stuck");
						m_state = State.Mining_Tunnel;
						return;
					}

					if (m_navSet.Settings_Current.Distance < 1f)
					{
						Vector3 pos = m_navDrill.WorldPosition;
						m_currentTarget = pos + Vector3.Normalize(pos - m_targetVoxel.GetCentre()) * 100f;
					}

					break;
				default:
					VRage.Exceptions.ThrowIf<NotImplementedException>(true, "State: " + m_state);
					break;
			}
			MoveCurrent();
		}

		private static readonly Random myRand = new Random();

		public void Rotate()
		{
			if (isMiningPlanet)
			{
				switch (m_state)
				{
					case State.Approaching:
						m_mover.CalcRotate();
						return;
					case State.Rotating:
						if (m_navSet.DirectionMatched())
						{
							m_logger.debugLog("Finished rotating", Logger.severity.INFO);
							m_state = State.MoveTo;
							m_mover.StopRotate();
							return;
						}
						break;
				}
				m_mover.Thrust.Update();
				m_mover.CalcRotate(m_navDrill, RelativeDirection3F.FromWorld(m_controlBlock.CubeGrid, m_mover.Thrust.WorldGravity.vector));
				return;
			}

			switch (m_state)
			{
				case State.Approaching:
					if (m_navSet.DistanceLessThan(m_longestDimension))
					{
						m_logger.debugLog("closer to destination than longest dim");
						m_mover.StopRotate();
					}
					else
						m_mover.CalcRotate();
					return;
				case State.GetTarget:
				case State.Mining_Escape:
				case State.Move_Away:
					m_logger.debugLog("no rotation");
					m_mover.StopRotate();
					return;
				case State.MoveTo:
				case State.Mining:
				case State.Mining_Tunnel:
					if (m_navSet.DistanceLessThan(3f))
					{
						m_mover.StopRotate();
						return;
					}
					break;
				case State.Rotating:
					if (m_navSet.DirectionMatched())
					{
						m_logger.debugLog("Finished rotating", Logger.severity.INFO);
						m_state = State.MoveTo;
						m_mover.StopRotate();
						return;
					}
					break;
				default:
					throw new Exception("case not implemented: " + m_state);
			}

			if (m_navDrill.FunctionalBlocks == 0)
			{
				m_logger.debugLog("no functional blocks, cannot rotate");
				m_mover.StopRotate();
			}
			else
			{
				m_logger.debugLog("rotate to face " + m_currentTarget);
				m_mover.CalcRotate(m_navDrill, RelativeDirection3F.FromWorld(m_controlBlock.CubeGrid, m_currentTarget - m_navDrill.WorldPosition));
			}
		}

		public override void AppendCustomInfo(StringBuilder customInfo)
		{
			if (m_state == State.GetTarget)
			{
				customInfo.AppendLine("Searching for ore");
				return;
			}

			customInfo.Append("Mining ");
			customInfo.AppendLine(m_depositOre);

			switch (m_state)
			{
				case State.Approaching:
					if (isMiningPlanet)
						customInfo.AppendLine("Approaching planet");
					else
						customInfo.AppendLine("Approaching asteroid");
					break;
				case State.Rotating:
					customInfo.AppendLine("Rotating to face deposit");
					customInfo.Append("Angle: ");
					customInfo.AppendLine(PrettySI.toSigFigs(MathHelper.ToDegrees(m_navSet.Settings_Current.DistanceAngle)) + '°');
					break;
				case State.MoveTo:
					customInfo.Append("Moving to ");
					customInfo.AppendLine(m_currentTarget.ToPretty());
					break;
				case State.Mining:
					customInfo.AppendLine("Mining deposit at ");
					customInfo.AppendLine(m_depositPos.ToPretty());
					break;
				case State.Mining_Escape:
					if (isMiningPlanet)
						customInfo.AppendLine("Leaving planet");
					else
						customInfo.AppendLine("Leaving asteroid");
					break;
				case State.Mining_Tunnel:
					customInfo.AppendLine("Tunneling");
					break;
				case State.Move_Away:
					if (isMiningPlanet)
						customInfo.AppendLine("Moving away from planet");
					else
						customInfo.AppendLine("Moving away from asteroid");
					break;
			}
		}

		/// <summary>
		/// <para>In survival, returns fraction of drills filled</para>
		/// <para>In creative, returns content per drill * 0.01</para>
		/// </summary>
		private float DrillFullness()
		{
			if (Globals.UpdateCount < m_nextCheck_drillFull)
				return m_current_drillFull;
			m_nextCheck_drillFull = Globals.UpdateCount + 100ul;

			MyFixedPoint content = 0, capacity = 0;
			int drillCount = 0;

			var cache = CubeGridCache.GetFor(m_controlBlock.CubeGrid);
			if (cache == null)
			{
				m_logger.debugLog("Failed to get cache", Logger.severity.INFO);
				return float.MaxValue;
			}
			var allDrills = cache.GetBlocksOfType(typeof(MyObjectBuilder_Drill));
			if (allDrills == null)
			{
				m_logger.debugLog("Failed to get block list", Logger.severity.INFO);
				return float.MaxValue;
			}

			foreach (Ingame.IMyShipDrill drill in allDrills)
			{
				MyInventoryBase drillInventory = ((MyEntity)drill).GetInventoryBase(0);

				content += drillInventory.CurrentVolume;
				capacity += drillInventory.MaxVolume;
				drillCount++;
			}

			if (MyAPIGateway.Session.CreativeMode)
				m_current_drillFull = (float)content * 0.01f / drillCount;
			else
				m_current_drillFull = (float)content / (float)capacity;

			return m_current_drillFull;
		}

		/// <summary>
		/// Finds the maximum forward and backwards accelerations and returns the lesser of the two.
		/// </summary>
		/// <returns>The lesser of maximum forward and backwards accelerations.</returns>
		private float GetAcceleration()
		{
			m_mover.Thrust.Update();
			float forwardForce = m_mover.Thrust.GetForceInDirection(Base6Directions.GetClosestDirection(m_navDrill.LocalMatrix.Forward), true);
			float backwardForce = m_mover.Thrust.GetForceInDirection(Base6Directions.GetClosestDirection(m_navDrill.LocalMatrix.Backward), true);

			return Math.Min(forwardForce, backwardForce) / m_navDrill.Physics.Mass;
		}

		private void EnableDrills(bool enable)
		{
			if (enable)
				m_logger.debugLog("Enabling drills", Logger.severity.DEBUG);
			else
				m_logger.debugLog("Disabling drills", Logger.severity.DEBUG);

			var cache = CubeGridCache.GetFor(m_controlBlock.CubeGrid);
			if (cache == null)
			{
				m_logger.debugLog("Failed to get cache", Logger.severity.INFO);
				return;
			}
			var allDrills = cache.GetBlocksOfType(typeof(MyObjectBuilder_Drill));
			if (allDrills == null)
			{
				m_logger.debugLog("Failed to get block list", Logger.severity.INFO);
				return;
			}

			MyAPIGateway.Utilities.TryInvokeOnGameThread(() => {
				foreach (IMyShipDrill drill in allDrills)
					if (!drill.Closed)
						drill.RequestEnable(enable);
			}, m_logger);
		}

		private void OnOreSearchComplete(bool success, Vector3D orePosition, IMyVoxelBase foundMap, string oreName)
		{
			if (!success)
			{
				m_logger.debugLog("No ore target found", Logger.severity.INFO);
				m_navSet.OnTaskComplete_NavRot();
				m_navSet.Settings_Commands.Complaint = "No ore found";
				return;
			}

			m_targetVoxel = foundMap;
			m_depositPos = orePosition;
			m_depositOre = oreName;

			Vector3 toCentre = Vector3.Normalize(m_targetVoxel.GetCentre() - m_depositPos);
			GetExteriorPoint(m_depositPos, toCentre, m_longestDimension, exterior => {
				m_approach = new LineSegmentD(exterior - toCentre * m_longestDimension * 5f, exterior);
				m_state = State.Approaching;
				m_logger.debugLog("approach: " + m_approach.From.ToGpsTag("From") + ", " + m_approach.To.ToGpsTag("To"));
			});
		}

		private void GetExteriorPoint(Vector3D startPoint, Vector3 direction, float buffer, Action<Vector3D> callback)
		{
			if (m_targetVoxel is IMyVoxelMap)
				callback(GetExteriorPoint_Asteroid(startPoint, direction, buffer));
			else
				GetExteriorPoint_Planet(startPoint, direction, buffer, callback);
		}

		/// <summary>
		/// Gets a point outside of an asteroid.
		/// </summary>
		/// <param name="startPoint">Where to start the search from, must be inside WorldAABB, can be inside or outside asteroid.</param>
		/// <param name="direction">Direction from outside asteroid towards inside asteroid</param>
		/// <param name="buffer">Minimum distance between the voxel and exterior point</param>
		private Vector3D GetExteriorPoint_Asteroid(Vector3D startPoint, Vector3 direction, float buffer)
		{
			IMyVoxelMap voxel = m_targetVoxel as IMyVoxelMap;
			if (voxel == null)
			{
				m_logger.alwaysLog("m_targetVoxel is not IMyVoxelMap: " + m_targetVoxel.getBestName(), Logger.severity.FATAL);
				throw new InvalidOperationException("m_targetVoxel is not IMyVoxelMap");
			}

			Vector3 v = direction * m_targetVoxel.LocalAABB.GetLongestDim();
			Capsule surfaceFinder = new Capsule(startPoint - v, startPoint + v, buffer);
			Vector3? obstruction;
			if (surfaceFinder.Intersects(voxel, out obstruction))
				return obstruction.Value;
			else
			{
				m_logger.debugLog("Failed to intersect asteroid, using surfaceFinder.P0", Logger.severity.WARNING);
				return surfaceFinder.P0;
			}
		}

		/// <summary>
		/// Gets a point outside of a planet.
		/// </summary>
		/// <param name="startPoint">Where to start the search from, can be inside or outside planet.</param>
		/// <param name="direction">Direction from outside of planet to inside planet.</param>
		/// <param name="buffer">Minimum distance between planet surface and exterior point</param>
		/// <param name="callback">Will be invoked on game thread with result</param>
		private void GetExteriorPoint_Planet(Vector3D startPoint, Vector3 direction, float buffer, Action<Vector3D> callback)
		{
			MyPlanet planet = m_targetVoxel as MyPlanet;
			if (planet == null)
			{
				m_logger.alwaysLog("m_targetVoxel is not MyPlanet: " + m_targetVoxel.getBestName(), Logger.severity.FATAL);
				throw new InvalidOperationException("m_targetVoxel is not MyPlanet");
			}

			MyAPIGateway.Utilities.TryInvokeOnGameThread(() => {
				m_surfacePoint = planet.GetClosestSurfacePointGlobal(ref startPoint);
				Vector3D exteriorPoint = m_surfacePoint - direction * buffer;
				callback(exteriorPoint);
			}, m_logger);
		}

		private bool IsNearVoxel(double lengthMulti = 1d)
		{
			if (m_targetVoxel is IMyVoxelMap)
			{
				BoundingSphereD surround = new BoundingSphereD(m_navDrill.Grid.GetCentre(), m_longestDimension * lengthMulti);
				return m_targetVoxel.GetIntersectionWithSphere(ref surround);
			}
			else
			{
				Vector3D planetCentre = m_targetVoxel.GetCentre();
				return Vector3D.Distance(m_navDrill.WorldPosition, planetCentre) - m_longestDimension * lengthMulti < Vector3D.Distance(m_surfacePoint, planetCentre);
			}
		}

		private void MoveCurrent()
		{
			m_logger.debugLog("current target: " + m_currentTarget);
			m_mover.CalcMove(m_navDrill, m_currentTarget, Vector3.Zero, m_state == State.MoveTo);
		}

	}
}
