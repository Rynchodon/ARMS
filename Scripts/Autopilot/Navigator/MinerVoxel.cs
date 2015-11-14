using System;
using System.Collections.Generic;
using System.Text;
using Rynchodon.Autopilot.Data;
using Rynchodon.Autopilot.Harvest;
using Rynchodon.Autopilot.Movement;
using Sandbox.Common.ObjectBuilders;
using Sandbox.Game.Entities;
using Sandbox.Game.Gui;
using Sandbox.ModAPI;
using VRage;
using VRageMath;
using Ingame = Sandbox.ModAPI.Ingame;

namespace Rynchodon.Autopilot.Navigator
{

	/*
	 * TODO:
	 * Escape if inside tunnel while autopilot gains control (regardless of commands)
	 * Antenna relay & multiple ore detectors
	 * Sort IMyVoxelBase by distance before searching for ores
	 */

	/// <summary>
	/// Mines an IMyVoxelBase
	/// Will not insist on rotation control until it is ready to start mining.
	/// </summary>
	public class MinerVoxel : NavigatorMover, INavigatorRotator
	{

		private const float FullAmount_Abort = 0.9f, FullAmount_Return = 0.1f;
		/// <summary>Apply a small amount of movement directly to prevent ship getting stuck.</summary>
		private const bool Unsticker = true;

		public static bool IsNearVoxel(IMyCubeGrid grid, double lengthMulti = 0.5d)
		{
			BoundingSphereD surround = new BoundingSphereD(grid.GetCentre(), grid.GetLongestDim() * lengthMulti);
			List<MyVoxelBase> voxels = new List<MyVoxelBase>();
			MyGamePruningStructure.GetAllVoxelMapsInSphere(ref surround, voxels);
			if (voxels != null)
				foreach (IMyVoxelBase vox in voxels)
				{
					if (vox is IMyVoxelMap)
					{
						if (vox.GetIntersectionWithSphere(ref surround))
							return true;
					}
					else
					{
						MyPlanet planet = vox as MyPlanet;
						if (planet != null && planet.Intersects(surround))
							return true;
					}
				}

			return false;
		}

		private enum State : byte { GetTarget, Approaching, Rotating, MoveTo, Mining, Mining_Escape, Mining_Tunnel, Move_Away }

		private readonly Logger m_logger;
		private readonly OreDetector m_oreDetector;
		private readonly byte[] OreTargets;
		private readonly float m_longestDimension;

		private MultiBlock<MyObjectBuilder_Drill> m_navDrill;
		private State value_state;
		private Line m_approach;
		private Vector3D m_depositPos, m_voxelCentre = Vector3D.NegativeInfinity;
		private Vector3 m_currentTarget;
		private string m_depostitOre;
		private ulong m_nextCheck_drillFull;
		private float m_current_drillFull;

		private float speedLinear = float.MaxValue, speedAngular = float.MaxValue;

		private State m_state
		{
			get
			{ return value_state; }
			set
			{
				m_logger.debugLog("Changing state to " + value, "m_state()");
				value_state = value;
				speedAngular = speedLinear = 10f;
				switch (value)
				{
					case State.GetTarget:
						EnableDrills(false);
						if (DrillFullness() >= FullAmount_Return)
						{
							m_logger.debugLog("Drills are full, time to go home", "m_state()");
							m_navSet.OnTaskComplete_NavRot();
							m_mover.StopMove();
							m_mover.StopRotate();
							return;
						}
						else
						{
							// request ore detector update
							m_logger.debugLog("Requesting ore update", "m_state()");
							m_navSet.OnTaskComplete_NavMove();
							m_oreDetector.OnUpdateComplete.Enqueue(OreDetectorFinished);
							m_oreDetector.UpdateOreLocations();
						}
						break;
					case State.Approaching:
						m_currentTarget = m_approach.From;
						break;
					case State.Rotating:
						m_currentTarget = m_depositPos;
						break;
					case State.MoveTo:
						EnableDrills(true);
						m_navSet.Settings_Task_NavMove.IgnoreAsteroid = true;
						break;
					case State.Mining:
						Vector3 pos = m_navDrill.WorldPosition;
						m_currentTarget = pos + (m_depositPos - pos) * 2f;
						m_navSet.Settings_Task_NavMove.SpeedTarget = 1f;
						break;
					case State.Mining_Escape:
						EnableDrills(false);
						m_currentTarget = m_navDrill.WorldPosition + m_navDrill.WorldMatrix.Backward * 100f;
						break;
					case State.Mining_Tunnel:
						EnableDrills(true);
						m_currentTarget = m_navDrill.WorldPosition + m_navDrill.WorldMatrix.Forward * 100f;
						break;
					case State.Move_Away:
						EnableDrills(false);
						m_navSet.Settings_Task_NavMove.SpeedTarget = 10f;
						break;
					default:
						VRage.Exceptions.ThrowIf<NotImplementedException>(true, "State not implemented: " + value);
						break;
				}
				m_logger.debugLog("Current target: " + m_currentTarget + ", current position: " + m_navDrill.WorldPosition, "m_state()");
				m_mover.StopMove();
				m_mover.StopRotate();
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
				m_logger.debugLog("No Drills!", "MinerVoxel()", Logger.severity.INFO);
				return;
			}
			if (MyAPIGateway.Session.CreativeMode)
				foreach (IMyShipDrill drill in allDrills)
					if (drill.UseConveyorSystem)
						drill.ApplyAction("UseConveyor");

			// if a drill has been chosen by player, use it
			PseudoBlock navBlock = m_navSet.Settings_Current.NavigationBlock;
			if (navBlock.Block is IMyShipDrill)
				m_navDrill = new MultiBlock<MyObjectBuilder_Drill>(navBlock.Block);
			else
				m_navDrill = new MultiBlock<MyObjectBuilder_Drill>(m_mover.Block.CubeGrid);

			if (m_navDrill.FunctionalBlocks == 0)
			{
				m_logger.debugLog("no working drills", "MinerVoxel()", Logger.severity.INFO);
				return;
			}

			var detectors = cache.GetBlocksOfType(typeof(MyObjectBuilder_OreDetector));
			if (detectors != null)
			{
				if (!Registrar.TryGetValue(detectors[0].EntityId, out m_oreDetector))
					m_logger.debugLog("failed to get ore detector from block", "MinerVoxel()", Logger.severity.FATAL);
			}
			else
			{
				m_logger.debugLog("No ore detector, no ore for you", "MinerVoxel()", Logger.severity.INFO);
				return;
			}

			m_longestDimension = m_controlBlock.CubeGrid.GetLongestDim();
			if (m_navSet.Settings_Current.DestinationRadius > m_longestDimension)
			{
				m_logger.debugLog("Reducing DestinationRadius from " + m_navSet.Settings_Current.DestinationRadius + " to " + m_longestDimension, "MinerVoxel()", Logger.severity.DEBUG);
				m_navSet.Settings_Task_NavRot.DestinationRadius = m_longestDimension;
			}

			m_navSet.Settings_Task_NavRot.NavigatorMover = this;
			m_state = State.GetTarget;
		}

		public override void Move()
		{
			if (m_state != State.Mining_Escape && m_navDrill.FunctionalBlocks == 0)
			{
				m_logger.debugLog("No drills, must escape!", "Move()");
				m_state = State.Mining_Escape;
			}

			speedAngular = 0.95f * speedAngular + 0.1f * m_mover.Block.Physics.AngularVelocity.LengthSquared();
			speedLinear = 0.95f * speedLinear + 0.1f * m_mover.Block.Physics.LinearVelocity.LengthSquared();

			switch (m_state)
			{
				case State.GetTarget:
					m_mover.StopMove();
					return;
				case State.Approaching:
					// measure distance from line, but move to a point
					Vector3 closestPoint = m_approach.ClosestPoint(m_navDrill.WorldPosition);
					if (Vector3.DistanceSquared(closestPoint, m_navDrill.WorldPosition) < m_longestDimension)
					{
						m_logger.debugLog("Finished approach", "Move()", Logger.severity.DEBUG);
						m_state = State.Rotating;
						return;
					}
					if (IsStuck())
					{
						m_state = State.Mining_Escape;
						return;
					}
					break;
				case State.Rotating:
					m_mover.StopMove();
					if (IsStuck())
					{
						m_state = State.Mining_Escape;
						return;
					}
					return;
				case State.MoveTo:
					if (m_navSet.Settings_Current.Distance < m_longestDimension)
					{
						m_logger.debugLog("Reached asteroid", "Move()", Logger.severity.DEBUG);
						m_state = State.Mining;
						return;
					}
					if (IsStuck())
					{
						m_state = State.Mining_Escape;
						return;
					}
					if (IsNearVoxel())
						m_navSet.Settings_Task_NavMove.SpeedTarget = 1f;
					break;
				case State.Mining:
					// do not check for inside asteroid as we may not have reached it yet and target is inside asteroid
					if (DrillFullness() > FullAmount_Abort)
					{
						m_logger.debugLog("Drills are full, aborting", "Move()", Logger.severity.DEBUG);
						m_state = State.Mining_Escape;
						return;
					}
					if (m_navSet.Settings_Current.Distance < 1f)
					{
						m_logger.debugLog("Reached position: " + m_currentTarget, "Move()", Logger.severity.DEBUG);
						m_state = State.Mining_Escape;
						return;
					}

					if (Unsticker && m_navDrill.Physics.LinearVelocity.LengthSquared() <= 0.01f)
						m_navDrill.Physics.LinearVelocity += m_navDrill.WorldMatrix.Forward * 0.1;

					if (IsStuck())
					{
						m_state = State.Mining_Escape;
						return;
					}
					break;
				case State.Mining_Escape:
					if (!IsNearVoxel())
					{
						m_logger.debugLog("left asteroid", "Move()");
						m_state = State.Move_Away;
						return;
					}
					if (m_navSet.Settings_Current.Distance < 1f)
					{
						m_logger.debugLog("Reached position: " + m_currentTarget, "Move()", Logger.severity.DEBUG);
						m_state = State.Mining_Escape;
						return;
					}

					if (Unsticker && m_navDrill.Physics.LinearVelocity.LengthSquared() <= 0.01f)
						m_navDrill.Physics.LinearVelocity += m_navDrill.WorldMatrix.Backward * 0.1;

					if (IsStuck())
					{
						m_logger.debugLog("Stuck", "Move()");
						Logger.debugNotify("Stuck", 16);
						m_state = State.Mining_Tunnel;
						return;
					}
					break;
				case State.Mining_Tunnel:
					if (!IsNearVoxel())
					{
						m_logger.debugLog("left asteroid", "Mine()");
						m_state = State.Move_Away;
						return;
					}
					if (m_navSet.Settings_Current.Distance < 1f)
					{
						m_logger.debugLog("Reached position: " + m_currentTarget, "Move()", Logger.severity.DEBUG);
						m_state = State.Mining_Tunnel;
						return;
					}
					if (IsStuck())
					{
						m_state = State.Mining_Escape;
						return;
					}
					break;
				case State.Move_Away:
					if (!m_voxelCentre.IsValid())
					{
						m_logger.debugLog("no asteroid centre", "Move()");
						m_state = State.GetTarget;
						return;
					}
					if (!IsNearVoxel(1d))
					{
						m_logger.debugLog("far enough away", "Move()");
						m_state = State.GetTarget;
						return;
					}
					if (IsStuck())
					{
						m_logger.debugLog("Stuck", "Move()");
						Logger.debugNotify("Stuck", 16);
						m_state = State.Mining_Tunnel;
						return;
					}
					m_currentTarget = m_navDrill.WorldPosition * 2 - m_voxelCentre;
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
			if (m_navDrill.FunctionalBlocks == 0)
				return;

			switch (m_state)
			{
				case State.Approaching:
					if (m_navSet.Settings_Current.Distance < m_longestDimension)
					{
						m_mover.StopRotate();
						return;
					}
					break;
				case State.GetTarget:
				case State.Mining_Escape:
				case State.Move_Away:
					m_mover.StopRotate();
					return;
				case State.Rotating:
					if (m_navSet.DirectionMatched())
					{
						m_logger.debugLog("Finished rotating", "Rotate()", Logger.severity.INFO);
						m_state = State.MoveTo;
						m_mover.StopRotate();
						return;
					}
					break;
				default:
					break;
			}

			if (m_state != State.Rotating && m_navSet.Settings_Current.Distance < 3f)
			{
				m_mover.StopRotate();
				return;
			}

			Vector3 direction = m_currentTarget - m_navDrill.WorldPosition;
			//m_logger.debugLog("rotating to face " + m_currentTarget, "Rotate()");
			if (m_state == State.Approaching)
				m_mover.CalcRotate(m_controlBlock.Pseudo, RelativeDirection3F.FromWorld(m_controlBlock.CubeGrid, direction));
			else
				m_mover.CalcRotate(m_navDrill, RelativeDirection3F.FromWorld(m_controlBlock.CubeGrid, direction));
		}

		public override void AppendCustomInfo(StringBuilder customInfo)
		{
			if (m_state == State.GetTarget)
			{
				customInfo.AppendLine("Searching for ore");
				return;
			}

			customInfo.Append("Mining ");
			customInfo.AppendLine(m_depostitOre);

			switch (m_state)
			{
				case State.Approaching:
					customInfo.AppendLine("Approaching asteroid");
					break;
				case State.Rotating:
					customInfo.AppendLine("Rotating to face deposit");
					customInfo.Append("Angle: ");
					customInfo.AppendLine(MathHelper.ToDegrees(m_navSet.Settings_Current.DistanceAngle).ToString());
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
					customInfo.AppendLine("Leaving asteroid");
					break;
				case State.Mining_Tunnel:
					customInfo.AppendLine("Tunneling");
					break;
				case State.Move_Away:
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
				m_logger.debugLog("Failed to get cache", "DrillFullness()", Logger.severity.INFO);
				return float.MaxValue;
			}
			var allDrills = cache.GetBlocksOfType(typeof(MyObjectBuilder_Drill));
			if (allDrills == null)
			{
				m_logger.debugLog("Failed to get block list", "DrillFullness()", Logger.severity.INFO);
				return float.MaxValue;
			}

			foreach (Ingame.IMyShipDrill drill in allDrills)
			{
				IMyInventory drillInventory = (IMyInventory)Ingame.TerminalBlockExtentions.GetInventory(drill, 0);
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

		private void EnableDrills(bool enable)
		{
			if (enable)
				m_logger.debugLog("Enabling drills", "EnableDrills()", Logger.severity.DEBUG);
			else
				m_logger.debugLog("Disabling drills", "EnableDrills()", Logger.severity.DEBUG);

			var cache = CubeGridCache.GetFor(m_controlBlock.CubeGrid);
			if (cache == null)
			{
				m_logger.debugLog("Failed to get cache", "EnableDrills()", Logger.severity.INFO);
				return;
			}
			var allDrills = cache.GetBlocksOfType(typeof(MyObjectBuilder_Drill));
			if (allDrills == null)
			{
				m_logger.debugLog("Failed to get block list", "EnableDrills()", Logger.severity.INFO);
				return;
			}

			MyAPIGateway.Utilities.TryInvokeOnGameThread(() => {
				foreach (IMyShipDrill drill in allDrills)
					if (!drill.Closed)
						drill.RequestEnable(enable);
			}, m_logger);
		}

		private void GetDeposit()
		{
			Vector3D pos = m_navDrill.WorldPosition;
			IMyVoxelBase foundMap;
			if (m_oreDetector.FindClosestOre(pos, OreTargets, out m_depositPos, out foundMap, out m_depostitOre))
			{
				// from the centre of the voxel, passing through the deposit, find the edge of the AABB
				m_voxelCentre = foundMap.GetCentre();
				Vector3D centreOut = m_depositPos - m_voxelCentre;
				centreOut.Normalize();
				Vector3D bodEdgeFinderStart = m_voxelCentre + centreOut * foundMap.WorldAABB.GetLongestDim();
				RayD boxEdgeFinder = new RayD(bodEdgeFinderStart, -centreOut);
				double? boxEdgeDist = foundMap.WorldAABB.Intersects(boxEdgeFinder);
				if (boxEdgeDist == null)
					throw new Exception("Math fail");
				Vector3D boxEdge = bodEdgeFinderStart - centreOut * boxEdgeDist.Value;

				// was getting memory access violation, so not using MainLock.RayCastVoxel_Safe()
				MyAPIGateway.Utilities.TryInvokeOnGameThread(() => {
					Vector3 surfacePoint;
					if (foundMap is IMyVoxelMap)
						MyAPIGateway.Entities.IsInsideVoxel(boxEdge, m_voxelCentre, out surfacePoint);
					else
					{
						MyPlanet planet = foundMap as MyPlanet;
						surfacePoint = planet.GetClosestSurfacePointGlobal(ref m_depositPos);
						m_logger.debugLog("Mining target is a planet, from nav drill position: " + m_navDrill.WorldPosition + ", surface is " + surfacePoint, "GetDeposit()");
					}
					m_approach = new Line(surfacePoint + centreOut * m_controlBlock.CubeGrid.GetLongestDim() * 2, surfacePoint);

					m_logger.debugLog("centre: " + m_voxelCentre.ToGpsTag("centre")
						+ ", deposit: " + m_depositPos.ToGpsTag("deposit")
						+ ", boxEdge: " + boxEdge.ToGpsTag("boxEdge")
						+ ", m_approach: " + m_approach.From.ToGpsTag("m_approach From")
						+ ", " + m_approach.To.ToGpsTag("m_approach To")
						+ ", surfacePoint: " + surfacePoint
						, "GetDeposit()");

					m_state = State.Approaching;
				}, m_logger);

				return;
			}

			m_logger.debugLog("No ore target found", "GetDeposit()", Logger.severity.INFO);
			m_navSet.OnTaskComplete_NavRot();
		}

		private bool IsNearVoxel(double lengthMulti = 0.5d)
		{ return IsNearVoxel(m_navDrill.Grid, lengthMulti); }

		private bool IsStuck()
		{
			if (speedAngular < 0.01f && speedLinear < 0.01f)
			{
				m_logger.debugLog("Got stuck", "IsStuck()", Logger.severity.DEBUG);
				return true;
			}
			return false;
		}

		private void MoveCurrent()
		{ m_mover.CalcMove(m_navDrill, m_currentTarget, Vector3.Zero); }

		private void OreDetectorFinished()
		{
			try
			{ GetDeposit(); }
			catch (Exception ex)
			{ m_logger.alwaysLog("Exception: " + ex, "OreDetectorFinished()", Logger.severity.ERROR); }
		}

	}
}
