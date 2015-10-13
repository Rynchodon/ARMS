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
	 */

	/// <summary>
	/// Mines a VoxelMap
	/// Will not insist on rotation control until it is ready to start mining.
	/// </summary>
	public class MinerVoxel : NavigatorMover, INavigatorRotator
	{

		public static bool IsNearVoxel(IMyCubeGrid grid)
		{
			BoundingSphereD surround = new BoundingSphereD(grid.GetCentre(), grid.GetLongestDim());
			List<MyVoxelBase> voxels = new List<MyVoxelBase>();
			MyGamePruningStructure.GetAllVoxelMapsInSphere(ref surround, voxels);
			if (voxels != null)
				foreach (IMyVoxelMap vox in voxels)
				{
					if (vox.GetIntersectionWithSphere(ref surround))
						return true;
				}

			return false;
		}

		private const float FullAmount_Abort = 0.9f, FullAmount_Return = 0.1f;
		private static readonly Vector3 kickOnEscape = new Vector3(0f, 0f, 0.1f);

		private enum State : byte { GetTarget, Approaching, Rotating, MoveTo, Mining, Mining_Escape, Mining_Tunnel }

		private readonly Logger m_logger;
		private readonly OreDetector m_oreDetector;
		private readonly byte[] OreTargets;

		private MultiBlock<MyObjectBuilder_Drill> m_navDrill;
		private readonly List<MyVoxelBase> voxels = new List<MyVoxelBase>();
		private State value_state;
		private Line m_approach;
		private Vector3D m_depositPos;
		private Vector3 m_currentTarget;
		private string m_depostitOre;
		private bool m_lastNearVoxel;
		private ulong m_lastCheck_nearVoxel;

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
							m_navSet.OnTaskPrimaryComplete();
							m_mover.StopMove();
							m_mover.StopRotate();
							return;
						}
						else
						{
							// request ore detector update
							m_logger.debugLog("Requesting ore update", "m_state()");
							m_navSet.OnTaskSecondaryComplete();
							m_oreDetector.OnUpdateComplete.Enqueue(OreDetectorFinished);
							m_oreDetector.UpdateOreLocations();
						}
						break;
					case State.Approaching:
						break;
					case State.Rotating:
						m_currentTarget = m_approach.To;
						m_navSet.Settings_Task_Primary.NavigatorRotator = this;
						break;
					case State.MoveTo:
						EnableDrills(true);
						m_navSet.Settings_Task_Secondary.IgnoreAsteroid = true;
						break;
					case State.Mining:
						Vector3 pos = m_navDrill.WorldPosition;
						m_currentTarget = pos + (m_depositPos - pos) * 2f;
						m_navSet.Settings_Task_Secondary.SpeedTarget = 1f;
						EnableDrills(true);
						break;
					case State.Mining_Escape:
						EnableDrills(false);
						m_currentTarget = m_navDrill.WorldPosition + m_navDrill.WorldMatrix.Backward * 100f;
						break;
					case State.Mining_Tunnel:
						EnableDrills(true);
						m_currentTarget = m_navDrill.WorldPosition + m_navDrill.WorldMatrix.Forward * 100f;
						break;
					default:
						VRage.Exceptions.ThrowIf<NotImplementedException>(true, "State not implemented: " + value);
						break;
				}
				m_logger.debugLog("Current target: " + m_currentTarget, "m_state()");
				m_mover.StopMove();
				m_mover.StopRotate();
				m_navSet.OnTaskTertiaryComplete();
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
			if (detectors != null && detectors.Count > 0)
			{
				if (!OreDetector.TryGetDetector(detectors[0].EntityId, out m_oreDetector))
					m_logger.debugLog("failed to get ore detector from block", "MinerVoxel()", Logger.severity.FATAL);
			}

			m_navSet.Settings_Task_Primary.NavigatorMover = this;
			if (m_navSet.Settings_Current.NavigatorRotator == null)
			{
				m_logger.debugLog("Taking control of rotation immediately", "MinerVoxel()");
				m_navSet.Settings_Task_Primary.NavigatorRotator = this;
			}

			m_state = State.GetTarget;
		}

		public override void Move()
		{
			if (m_state != State.Mining_Escape && m_navDrill.FunctionalBlocks == 0)
			{
				m_logger.debugLog("No drills, must escape!", "Move()");
				m_state = State.Mining_Escape;
			}

			speedAngular = 0.99f * speedAngular + 0.1f * m_mover.Block.Physics.AngularVelocity.LengthSquared();
			speedLinear = 0.99f * speedLinear + 0.1f * m_mover.Block.Physics.LinearVelocity.LengthSquared();

			switch (m_state)
			{
				case State.GetTarget:
					m_logger.debugLog("waiting for target", "Move()");
					m_mover.StopMove();
					return;
				case State.Approaching:
					if (m_navSet.Settings_Current.Distance < 10f)
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
					m_currentTarget = m_approach.ClosestPoint(m_navDrill.WorldPosition);
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
					if (m_navSet.Settings_Current.Distance < 10f)
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
						m_state = State.GetTarget;
						return;
					}
					if (m_navSet.Settings_Current.Distance < 1f)
					{
						m_logger.debugLog("Reached position: " + m_currentTarget, "Move()", Logger.severity.DEBUG);
						m_state = State.Mining_Escape;
						return;
					}

					// trying to prevent bug where ship gets stuck
					if (m_navDrill.Physics.LinearVelocity.LengthSquared() <= 0.01f)
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
						m_state = State.GetTarget;
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
				case State.GetTarget:
				case State.Mining_Escape:
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
				case State.MoveTo:
				case State.Mining:
				case State.Mining_Tunnel:
					// TODO: stop ship from rotating if near destination or reduce roation?
				default:
					break;
			}

			Vector3 direction = m_currentTarget - m_navDrill.WorldPosition;
			m_mover.CalcRotate(m_navDrill, RelativeDirection3F.FromWorld(m_controlBlock.CubeGrid, direction));
		}

		public override void AppendCustomInfo(StringBuilder customInfo)
		{
			if (m_state == State.GetTarget)
			{
				customInfo.AppendLine("Searching for ore");
				return;
			}

			customInfo.AppendLine("Mining State: " + m_state);

			switch (m_state)
			{
				case State.Approaching:
					customInfo.AppendLine("Approaching asteroid");
					customInfo.Append("Distance: ");
					customInfo.Append(PrettySI.makePretty(m_navSet.Settings_Current.Distance));
					customInfo.AppendLine("m");
					break;
				case State.Rotating:
					customInfo.AppendLine("Rotating to face ore deposit");
					customInfo.Append("Angle: ");
					customInfo.AppendLine(PrettySI.makePretty(MathHelper.ToDegrees(m_navSet.Settings_Current.DistanceAngle)));
					break;
				case State.Mining:
					customInfo.AppendLine("Mining ore deposit");

					customInfo.Append("Ore: ");
					customInfo.Append(m_depostitOre);
					customInfo.Append(" at ");
					customInfo.AppendLine(m_depositPos.ToPretty());

					customInfo.Append("Distance: ");
					customInfo.Append(PrettySI.makePretty(m_navSet.Settings_Current.Distance));
					customInfo.AppendLine("m");
					break;
			}
		}

		/// <summary>
		/// <para>In survival, returns fraction of drills filled</para>
		/// <para>In creative, returns content per drill * 0.01</para>
		/// </summary>
		private float DrillFullness()
		{
			MyFixedPoint content = 0, capacity = 0;
			int drillCount = 0;
			var allDrills = CubeGridCache.GetFor(m_controlBlock.CubeGrid).GetBlocksOfType(typeof(MyObjectBuilder_Drill));
			foreach (Ingame.IMyShipDrill drill in allDrills)
			{
				IMyInventory drillInventory = (IMyInventory)Ingame.TerminalBlockExtentions.GetInventory(drill, 0);
				content += drillInventory.CurrentVolume;
				capacity += drillInventory.MaxVolume;
				drillCount++;
			}

			if (MyAPIGateway.Session.CreativeMode)
			{
				//m_logger.debugLog("content = " + content + ", drillCount = " + drillCount, "DrillFullness()");
				return (float)content * 0.01f / drillCount;
			}
			//m_logger.debugLog("content = " + content + ", capacity = " + capacity, "DrillFullness()");
			return (float)content / (float)capacity;
		}

		private void EnableDrills(bool enable)
		{
			if (enable)
				m_logger.debugLog("Enabling drills", "EnableDrills()", Logger.severity.DEBUG);
			else
				m_logger.debugLog("Disabling drills", "EnableDrills()", Logger.severity.DEBUG);

			var allDrills = CubeGridCache.GetFor(m_controlBlock.CubeGrid).GetBlocksOfType(typeof(MyObjectBuilder_Drill));
			MyAPIGateway.Utilities.TryInvokeOnGameThread(() => {
				foreach (IMyShipDrill drill in allDrills)
					if (!drill.Closed)
						drill.RequestEnable(enable);
			}, m_logger);
		}

		private bool GetDeposit()
		{
			Vector3D pos = m_navDrill.WorldPosition;
			IMyVoxelMap foundMap;
			if (m_oreDetector.FindClosestOre(pos, OreTargets, out m_depositPos, out foundMap, out m_depostitOre))
			{
				// from the centre of the voxel, passing through the deposit, find the edge of the AABB
				Vector3D centre = foundMap.GetCentre();
				Vector3D centreOut = m_depositPos - centre;
				centreOut.Normalize();
				Vector3D bodEdgeFinderStart = centre + centreOut * foundMap.WorldAABB.GetLongestDim();
				RayD boxEdgeFinder = new RayD(bodEdgeFinderStart, -centreOut);
				double? boxEdgeDist = foundMap.WorldAABB.Intersects(boxEdgeFinder);
				if (boxEdgeDist == null)
					throw new Exception("Math fail");
				Vector3D boxEdge = bodEdgeFinderStart - centreOut * boxEdgeDist.Value;

				// get the point on the asteroids surface (need this later anyway)
				Vector3 surfacePoint;
				MyAPIGateway.Entities.RayCastVoxel_Safe(boxEdge, m_depositPos, out surfacePoint);
				m_approach = new Line(boxEdge, surfacePoint);

				m_logger.debugLog("centre: " + centre.ToGpsTag("centre")
					+ ", deposit: " + m_depositPos.ToGpsTag("deposit")
					+ ", boxEdge: " + boxEdge.ToGpsTag("boxEdge")
					+ ", m_approach: " + m_approach.From.ToGpsTag("m_approach From")
					+ ", " + m_approach.To.ToGpsTag("m_approach To")
					, "GetDeposit()");
				return true;
			}

			return false;
		}

		private bool IsNearVoxel()
		{
			if (Globals.UpdateCount - m_lastCheck_nearVoxel > 100ul)
			{
				m_lastNearVoxel = IsNearVoxel(m_navDrill.Grid);
				m_lastCheck_nearVoxel = Globals.UpdateCount;
			}
			
			return m_lastNearVoxel;
		}

		private bool IsStuck()
		{
			return speedAngular < 0.01f && speedLinear < 0.01f;
		}

		private void MoveCurrent()
		{
			//m_logger.debugLog("current target: " + m_currentTarget.ToGpsTag("m_currentTarget"), "MoveCurrent()");
			m_mover.CalcMove(m_navDrill, m_currentTarget, Vector3.Zero);
		}

		private void OreDetectorFinished()
		{
			try
			{
				if (GetDeposit())
				{
					m_logger.debugLog("Got a target: " + m_currentTarget, "Move()", Logger.severity.INFO);
					m_state = State.Approaching;
				}
				else
				{
					m_logger.debugLog("No ore target found", "Move()", Logger.severity.INFO);
					m_navSet.OnTaskPrimaryComplete();
				}
			}
			catch (Exception ex)
			{ m_logger.alwaysLog("Exception: " + ex, "OreDetectorFinished()", Logger.severity.ERROR); }
		}

	}
}
