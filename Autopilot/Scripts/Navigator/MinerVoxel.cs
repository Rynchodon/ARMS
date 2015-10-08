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
using VRage.Components;
using VRage.ModAPI;
using VRageMath;
using Ingame = Sandbox.ModAPI.Ingame;

namespace Rynchodon.Autopilot.Navigator
{
	/*
	 * TODO:
	 * Escape if inside tunnel while autopilot gains control (regardless of commands)
	 * Antenna relay & multiple ore detectors
	 * Symbols for ores
	 */

	/// <summary>
	/// Mines a VoxelMap
	/// Will not insist on rotation control until it is ready to start mining.
	/// </summary>
	public class MinerVoxel : NavigatorMover, INavigatorRotator
	{

		private class NavigationDrill
		{

			public readonly IMyCubeGrid Grid;

			private readonly Logger m_logger;

			public int FunctionalDrills { get; private set; }
			public Matrix LocalMatrix { get; private set; }
			public MyPhysicsComponentBase Physics { get { return Grid.Physics; } }

			public NavigationDrill(IMyCubeBlock block)
			{
				if (!(block is IMyShipDrill))
					throw new NullReferenceException("block is not a drill");

				this.FunctionalDrills = 1;
				this.LocalMatrix = block.LocalMatrix;
				this.Grid = block.CubeGrid;
			}

			public NavigationDrill(IMyCubeGrid grid)
			{
				this.m_logger = new Logger(GetType().Name, () => grid.DisplayName);
				this.Grid = grid;
				calculateLocalMatrix();
			}

			public Vector3D GetPosition()
			{ return Vector3D.Transform(LocalMatrix.Translation, Grid.WorldMatrix); }

			public MatrixD WorldMatrix()
			{ return LocalMatrix * Grid.WorldMatrix; }

			private void block_OnClose(IMyEntity obj)
			{
				try
				{
					m_logger.debugLog("Closed drill: " + obj.getBestName(), "block_OnClose()");
					calculateLocalMatrix();
				}
				catch (Exception ex)
				{ m_logger.debugLog("Exception: " + ex, "block_OnClose()"); }
			}

			private void calculateLocalMatrix()
			{
				if (Grid.MarkedForClose)
					return;

				var Drills = CubeGridCache.GetFor(Grid).GetBlocksOfType(typeof(MyObjectBuilder_Drill));

				FunctionalDrills = 0;
				LocalMatrix = Matrix.Zero;
				foreach (IMyCubeBlock drill in Drills)
					if (drill.IsFunctional)
					{
						FunctionalDrills++;
						LocalMatrix += drill.LocalMatrix;
						drill.OnClose -= block_OnClose;
						drill.OnClose += block_OnClose;
					}

				if (FunctionalDrills == 0)
					return;

				LocalMatrix = LocalMatrix / FunctionalDrills;
				return;
			}

		}

		private const float FullAmount_Abort = 0.9f, FullAmount_Return = 0.1f;
		private static readonly Vector3 kickOnEscape = new Vector3(0f, 0f, 0.1f);

		private enum State : byte { GetTarget, Approaching, Rotating, MoveTo, Mining, Mining_Escape, Mining_Tunnel }

		private readonly Logger m_logger;
		private readonly OreDetector m_oreDetector;
		private readonly byte[] OreTargets;

		// Not normally changed, but things might break and we still want to escape voxel
		private NavigationDrill m_navDrill;
		private readonly List<MyVoxelBase> voxels = new List<MyVoxelBase>();
		private State value_state;
		private Line m_approach;
		private Vector3D m_depositPos;
		private Vector3 m_currentTarget;
		private string m_depostitOre;

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
							m_mover.FullStop();
							return;
						}
						else
						{
							// request ore detector update
							m_logger.debugLog("Requesting ore update", "m_state()");
							m_oreDetector.OnUpdateComplete.Enqueue(OreDetectorFinished);
							m_oreDetector.UpdateOreLocations();
						}
						break;
					case State.Approaching:
						break;
					case State.Rotating:
						m_currentTarget = m_approach.To;
						m_navSet.Settings_Task_Secondary.NavigatorRotator = this;
						break;
					case State.MoveTo:
						EnableDrills(true);
						break;
					case State.Mining:
						Vector3 pos = m_navDrill.GetPosition();
						m_currentTarget = pos + (m_depositPos - pos) * 2f;

						m_navSet.Settings_Task_Secondary.IgnoreAsteroid = true;
						m_navSet.Settings_Task_Secondary.SpeedTarget = 1f;

						EnableDrills(true);
						break;
					case State.Mining_Escape:
						//EnableDrills(false);
						m_currentTarget = m_navDrill.GetPosition() + m_navDrill.WorldMatrix().Backward * 100f;
						break;
					case State.Mining_Tunnel:
						EnableDrills(true);
						m_currentTarget = m_navDrill.GetPosition() + m_navDrill.WorldMatrix().Forward * 100f;
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
			IMyCubeBlock navBlock = m_navSet.CurrentSettings.NavigationBlock;
			if (navBlock is IMyShipDrill)
				m_navDrill = new NavigationDrill(navBlock);
			else
				m_navDrill = new NavigationDrill(m_mover.Block.CubeGrid);

			if (m_navDrill.FunctionalDrills == 0)
			{
				m_logger.debugLog("no working drills", "MinerVoxel()", Logger.severity.INFO);
				return;
			}

			var detectors = cache.GetBlocksOfType(typeof(MyObjectBuilder_OreDetector));
			if (detectors != null && detectors.Count > 0)
				m_oreDetector = OreDetector.registry[detectors[0].EntityId];

			m_navSet.Settings_Task_Primary.NavigatorMover = this;
			if (m_navSet.Settings_Task_Secondary.NavigatorRotator == null)
			{
				m_logger.debugLog("Taking control of rotation immediately", "MinerVoxel()");
				m_navSet.Settings_Task_Secondary.NavigatorRotator = this;
			}

			m_state = State.GetTarget;
		}

		public override void Move()
		{
			if (m_state != State.Mining_Escape && m_navDrill.FunctionalDrills == 0)
			{
				m_logger.debugLog("No drills, must escape!", "Move()");
				m_state = State.Mining_Escape;
			}

			speedAngular = 0.99f * speedAngular + 0.1f * m_mover.Block.Physics.AngularVelocity.LengthSquared();
			speedLinear = 0.99f * speedLinear + 0.1f * m_mover.Block.Physics.LinearVelocity.LengthSquared();

			switch (m_state)
			{
				case State.GetTarget:
					m_mover.StopMove();
					return;
				case State.Approaching:
					if (m_navSet.CurrentSettings.Distance < 10f)
					{
						m_logger.debugLog("Finished approach", "Move()", Logger.severity.DEBUG);
						m_state = State.Rotating;
						return;
					}
					m_currentTarget = m_approach.ClosestPoint(m_navDrill.GetPosition());
					break;
				case State.Rotating:
					m_mover.StopMove();
					return;
				case State.MoveTo:
					if (m_navSet.CurrentSettings.Distance < 10f)
					{
						m_logger.debugLog("Reached asteroid", "Move()", Logger.severity.DEBUG);
						m_state = State.Mining;
						return;
					}
					break;
				case State.Mining:
					// do not check for inside asteroid as we may not have reached it yet and target is inside asteroid
					if (IsStuck())
					{
						m_state = State.Mining_Escape;
						return;
					}
					if (DrillFullness() > FullAmount_Abort)
					{
						m_logger.debugLog("Drills are full, aborting", "Move()", Logger.severity.DEBUG);
						m_state = State.Mining_Escape;
						return;
					}
					if (m_navSet.CurrentSettings.Distance < 1f)
					{
						m_logger.debugLog("Reached position: " + m_currentTarget, "Move()", Logger.severity.DEBUG);
						m_state = State.Mining_Escape;
						return;
					}
					break;
				case State.Mining_Escape:
					if (!IsInsideAsteroid())
					{
						m_logger.debugLog("left asteroid", "Mine()");
						m_state = State.GetTarget;
						return;
					}
					if (IsStuck())
					{
						m_state = State.Mining_Tunnel;
						return;
					}
					if (m_navSet.CurrentSettings.Distance < 1f)
					{
						m_logger.debugLog("Reached position: " + m_currentTarget, "Move()", Logger.severity.DEBUG);
						m_state = State.Mining_Escape;
						return;
					}
					break;
				case State.Mining_Tunnel:
					if (!IsInsideAsteroid())
					{
						m_logger.debugLog("left asteroid", "Mine()");
						m_state = State.GetTarget;
						return;
					}
					if (IsStuck())
					{
						m_state = State.Mining_Escape;
						return;
					}
					if (m_navSet.CurrentSettings.Distance < 1f)
					{
						m_logger.debugLog("Reached position: " + m_currentTarget, "Move()", Logger.severity.DEBUG);
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

		public void Rotate()
		{
			if (m_navDrill.FunctionalDrills == 0)
				return;

			switch (m_state)
			{
				case State.GetTarget:
				case State.Mining_Escape:
					return;
			}

			Vector3 direction = m_currentTarget - m_navDrill.GetPosition();
			if (m_mover.CalcRotate(RelativeDirection3F.FromWorld(m_controlBlock.CubeGrid, direction), m_navDrill.LocalMatrix))
				if (m_state == State.Rotating)
				{
					m_logger.debugLog("Finished rotating", "Rotate()", Logger.severity.INFO);
					m_state = State.MoveTo;
					return;
				}
		}

		public override void AppendCustomInfo(StringBuilder customInfo)
		{
			if (m_state == State.GetTarget)
			{
				customInfo.AppendLine("Searching for ore");
				return;
			}

			customInfo.AppendLine("State: " + m_state);

			switch (m_state)
			{
				case State.Approaching:
					customInfo.AppendLine("Approaching asteroid");
					customInfo.Append("Distance: ");
					customInfo.Append(PrettySI.makePretty(m_navSet.CurrentSettings.Distance));
					customInfo.AppendLine("m");
					break;
				case State.Rotating:
					customInfo.AppendLine("Rotating to face ore deposit");
					customInfo.Append("Angle: ");
					customInfo.AppendLine(PrettySI.makePretty(MathHelper.ToDegrees(m_navSet.CurrentSettings.DistanceAngle)));
					break;
				case State.Mining:
					customInfo.AppendLine("Mining ore deposit");

					customInfo.Append("Ore: ");
					customInfo.Append(m_depostitOre);
					customInfo.Append(" at ");
					customInfo.AppendLine(m_depositPos.ToPretty());

					customInfo.Append("Distance: ");
					customInfo.Append(PrettySI.makePretty(m_navSet.CurrentSettings.Distance));
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
			if (!MyAPIGateway.Multiplayer.IsServer)
				return;

			var allDrills = CubeGridCache.GetFor(m_controlBlock.CubeGrid).GetBlocksOfType(typeof(MyObjectBuilder_Drill));
			foreach (IMyShipDrill drill in allDrills)
				if (!drill.Closed)
					drill.RequestEnable(enable);
		}

		private bool GetDeposit()
		{
			Vector3D pos = m_navDrill.GetPosition();
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

		private bool IsInsideAsteroid()
		{
			BoundingSphereD surround = new BoundingSphereD(m_navDrill.Grid.GetCentre(), m_navDrill.Grid.GetLongestDim());
			voxels.Clear();
			MyGamePruningStructure.GetAllVoxelMapsInSphere(ref surround, voxels);
			if (voxels != null)
				foreach (IMyVoxelMap vox in voxels)
				{
					if (vox.GetIntersectionWithSphere(ref surround))
						return true;
				}

			return false;
		}

		private bool IsStuck()
		{
			return speedAngular < 0.01f && speedLinear < 0.01f;
		}

		private void MoveCurrent()
		{
			m_logger.debugLog("current target: " + m_currentTarget.ToGpsTag("m_currentTarget"), "MoveCurrent()");
			m_mover.CalcMove(m_navDrill.GetPosition(), m_currentTarget, Vector3.Zero);
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
