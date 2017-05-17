using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using Rynchodon.Autopilot.Data;
using Rynchodon.Autopilot.Harvest;
using Rynchodon.Autopilot.Pathfinding;
using Rynchodon.Utility;
using Sandbox.Common.ObjectBuilders;
using Sandbox.Definitions;
using Sandbox.Game.Entities;
using Sandbox.Game.Weapons;
using Sandbox.ModAPI;
using VRage.Game.ModAPI;
using VRage.ModAPI;
using VRage.Voxels;
using VRageMath;

namespace Rynchodon.Autopilot.Navigator.Mining
{
	class Miner : AMiner, IDisposable
	{

		public enum Stage : byte { None, GetDeposit, GetApproach, Approach, Mining }
		/// <summary>The maximum number of deposits that Miner will consider.</summary>
		public const int MaxDeposits = 32;

		private static FieldInfo MyShipDrillDefinition__SensorOffset, MyShipDrillDefinition__SensorRadius;

		static Miner()
		{
			Type MyShipDrillDefinition = typeof(MyCubeBlockDefinition).Assembly.GetType("Sandbox.Definitions.MyShipDrillDefinition", true);
			MyShipDrillDefinition__SensorOffset = MyShipDrillDefinition.GetField("SensorOffset", BindingFlags.Instance | BindingFlags.Public);
			if (MyShipDrillDefinition__SensorOffset == null)
				throw new NullReferenceException("MyShipDrillDefinition__SensorOffset");
			MyShipDrillDefinition__SensorRadius = MyShipDrillDefinition.GetField("SensorRadius", BindingFlags.Instance | BindingFlags.Public);
			if (MyShipDrillDefinition__SensorRadius == null)
				throw new NullReferenceException("MyShipDrillDefinition__SensorRadius");
		}

		private readonly byte[] m_oreTargets;

		private string m_oreName;
		private List<DepositFreeSpace> m_approachFinders;
		private Destination[] m_destinations;

		private IMyVoxelBase value_targetVoxel;
		private IMyVoxelBase m_targetVoxel
		{
			get { return value_targetVoxel; }
			set
			{
				if (value_targetVoxel != null)
					ImmortalMiner.UnregisterMiner(m_controlBlock.CubeGrid);
				if (value != null)
					ImmortalMiner.RegisterMiner(m_controlBlock.CubeGrid, value);
				value_targetVoxel = value;
			}
		}

		protected override MyVoxelBase TargetVoxel { get { return (MyVoxelBase)m_targetVoxel; } }

		private Stage value_stage;
		private Stage m_stage
		{
			get { return value_stage; }
			set
			{
				if (value_stage == value)
					return;

				Log.DebugLog("stage changed from " + value_stage + " to " + value, Logger.severity.DEBUG);
				switch (value)
				{
					case Stage.GetDeposit:
						{
							if (Return())
							{
								m_navSet.OnTaskComplete_NavRot();
								return;
							}
							OreDetector.SearchForMaterial(m_controlBlock, m_oreTargets, OnOreSearchComplete);
							Log.DebugLog("Waiting on ore detector", Logger.severity.DEBUG);
							break;
						}
				}

				m_navSet.OnTaskComplete_NavWay();
				value_stage = value;
			}
		}
		private Logable Log
		{ get { return LogableFrom.Pseudo(m_navSet.Settings_Current.NavigationBlock); } }

		public Miner(Pathfinder pathfinder, byte[] oreTargets) : base(pathfinder)
		{
			m_oreTargets = oreTargets;

			CubeGridCache cache = CubeGridCache.GetFor(m_controlBlock.CubeGrid);
			if (cache == null)
				return;

			if (cache.CountByType(typeof(MyObjectBuilder_Drill)) == 0)
			{
				Logger.DebugLog("No drills", Logger.severity.WARNING);
				return;
			}

			// if a drill has been chosen by player, use it
			PseudoBlock navBlock = m_navSet.Settings_Current.NavigationBlock;
			MultiBlock<MyObjectBuilder_Drill> navDrill = navBlock.Block is IMyShipDrill ? new MultiBlock<MyObjectBuilder_Drill>(navBlock.Block) : navDrill = new MultiBlock<MyObjectBuilder_Drill>(() => m_mover.Block.CubeGrid);

			if (navDrill.FunctionalBlocks == 0)
			{
				Logger.DebugLog("no working drills", Logger.severity.WARNING);
				return;
			}

			m_navSet.Settings_Task_NavRot.NavigatorMover = this;
			m_navSet.Settings_Task_NavRot.NavigationBlock = navDrill;

			BoundingSphereD nearby = new BoundingSphereD(navDrill.WorldPosition, m_controlBlock.CubeGrid.LocalVolume.Radius * 2d);
			List<MyVoxelBase> nearbyVoxels = new List<MyVoxelBase>();
			MyGamePruningStructure.GetAllVoxelMapsInSphere(ref nearby, nearbyVoxels);

			foreach (MyVoxelBase voxel in nearbyVoxels)
				if ((voxel is IMyVoxelMap || voxel is MyPlanet) && voxel.ContainsOrIntersects(ref nearby))
				{
					Log.DebugLog("near a voxel, escape first", Logger.severity.DEBUG);
					m_stage = Stage.Mining;
					new EscapeMiner(m_pathfinder, voxel);
					return;
				}

			m_stage = Stage.GetDeposit;
		}

		~Miner()
		{
			Dispose();
		}

		public void Dispose()
		{
			m_targetVoxel = null;
		}

		public override void AppendCustomInfo(StringBuilder customInfo)
		{
			customInfo.AppendLine("Preparing to mine");
			switch (m_stage)
			{
				case Stage.GetDeposit:
					customInfo.AppendLine("Searching for ore");
					break;
				case Stage.GetApproach:
					customInfo.AppendLine("Searching for path to " + m_oreName);
					break;
				case Stage.Approach:
					customInfo.AppendLine("Approaching " + m_oreName);
					break;
				case Stage.Mining:
					customInfo.AppendLine("Mining in progress");
					break;
			}
		}

		public override void Move()
		{
			switch (m_stage)
			{
				case Stage.GetDeposit:
					m_mover.StopMove();
					return;
				case Stage.GetApproach:
					CheckApproaches();
					return;
				case Stage.Approach:
					if (m_navSet.DistanceLessThan(10f))
					{
						CreateMiner();
						return;
					}
					m_pathfinder.MoveTo(destinations: m_destinations);
					return;
				case Stage.Mining:
					Log.DebugLog("Finished mining", Logger.severity.DEBUG);
					m_stage = Stage.GetDeposit;
					return;
			}
		}

		public override void Rotate()
		{
			m_mover.CalcRotate();
		}

		/// <summary>
		/// Determines if the ship is capable of digging a tunnel.
		/// </summary>
		private bool CanTunnel()
		{
			CubeGridCache cache = CubeGridCache.GetFor(m_grid);
			if (cache == null)
				return false;

			BoundingSphere[] sensors = new BoundingSphere[cache.CountByType(typeof(MyObjectBuilder_Drill))];
			int drillIndex = 0;
			foreach (MyShipDrill drill in cache.BlocksOfType(typeof(MyObjectBuilder_Drill)))
			{
				float offset = (float)MyShipDrillDefinition__SensorOffset.GetValue(drill.BlockDefinition);
				float radius = (float)MyShipDrillDefinition__SensorRadius.GetValue(drill.BlockDefinition);
				sensors[drillIndex++] = new BoundingSphere(drill.LocalPosition() + drill.PositionComp.LocalMatrix.Forward * offset, radius + MyVoxelConstants.VOXEL_SIZE_IN_METRES_HALF);
			}

			Vector3 forward = m_navBlock.LocalMatrix.Forward;
			foreach (Vector3I cell in m_grid.FirstBlocks(m_navBlock.LocalMatrix.Backward))
			{
				IMySlimBlock block = m_grid.GetCubeBlock(cell);
				if (!(block.FatBlock is IMyShipDrill))
				{
					Ray ray = new Ray(cell * m_grid.GridSize, forward);

					foreach (BoundingSphere sensor in sensors)
						if (ray.Intersects(sensor).HasValue)
						{
							//Log.DebugLog(block.getBestName() + " is behind a drill");
							goto NextBlock;
						}

					//Log.DebugLog(block.getBestName() + " is not behind any drill");
					return false;
				}

				NextBlock:;
			}

			return true;
		}

		private void OnOreSearchComplete(bool success, IMyVoxelBase foundVoxel, string oreName, IEnumerable<Vector3D> positions)
		{
			Log.DebugLog("success: " + success + ", foundVoxel: " + foundVoxel + ", oreName: " + oreName, Logger.severity.DEBUG);

			if (!success)
			{
				Log.DebugLog("No ore target found", Logger.severity.INFO);
				m_navSet.OnTaskComplete_NavRot();
				m_navSet.Settings_Commands.Complaint = InfoString.StringId.NoOreFound;
				return;
			}

			m_targetVoxel = foundVoxel;
			m_oreName = oreName;

			if (m_approachFinders != null)
				m_approachFinders.Clear();
			else
				ResourcePool.Get(out m_approachFinders);

			Vector3D centre = TargetVoxel.GetCentre();
			double minSpace = 4d * m_grid.LocalVolume.Radius;

			foreach (Vector3D pos in positions)
			{
				Vector3D deposit = pos;

				if (deposit == centre)
					deposit += 1;

				m_approachFinders.Add(new DepositFreeSpace(TargetVoxel, ref deposit, minSpace));

				if (m_approachFinders.Count == MaxDeposits)
					break;
			}

			Log.DebugLog("Created " + m_approachFinders.Count + " finders for " + oreName);
			m_stage = Stage.GetApproach;
		}

		private void CheckApproaches()
		{
			foreach (DepositFreeSpace approachFinder in m_approachFinders)
				if (!approachFinder.Completed)
					return;

			Log.DebugLog(m_approachFinders.Count + " approach finders completed", Logger.severity.DEBUG);

			// first try to get a deposit that is near surface, failing that grab any deposit

			List<DepositFreeSpace> removeList = new List<DepositFreeSpace>(m_approachFinders.Count);
			List<Destination> destinations = new List<Destination>(m_approachFinders.Count);
			foreach (DepositFreeSpace approachFinder in m_approachFinders)
				if (approachFinder.NearSurface)
				{
					Log.DebugLog("near surface: " + approachFinder.FreePosition);
					destinations.Add(Destination.FromWorld(m_targetVoxel, approachFinder.FreePosition));
				}
				else
				{
					Log.DebugLog("not near surface: " + approachFinder.FreePosition);
					removeList.Add(approachFinder);
				}
			if (destinations.Count == 0)
			{
				Log.DebugLog("No ore is near surface.", Logger.severity.DEBUG);
				m_destinations = new Destination[m_approachFinders.Count];
				int index = 0;
				foreach (DepositFreeSpace approachFinder in m_approachFinders)
					m_destinations[index++] = Destination.FromWorld(m_targetVoxel, approachFinder.FreePosition);
			}
			else
			{
				foreach (DepositFreeSpace remove in removeList)
					m_approachFinders.Remove(remove);
				m_destinations = destinations.ToArray();
			}
			m_stage = Stage.Approach;
		}

		private void CreateMiner()
		{
			Log.DebugLog("Approached, start mining", Logger.severity.DEBUG);

			DepositFreeSpace closestApproach = null;
			double closestDistSq = double.MaxValue;
			Vector3D currentPosition = m_navBlock.WorldPosition;
			foreach (DepositFreeSpace approachFinder in m_approachFinders)
			{
				double distSq = Vector3D.DistanceSquared(currentPosition, approachFinder.FreePosition);
				if (distSq < closestDistSq)
				{
					closestDistSq = distSq;
					closestApproach = approachFinder;
				}
			}

			bool nearSurface = closestApproach.NearSurface;
			m_approachFinders.Clear();
			ResourcePool.Return(m_approachFinders);
			m_approachFinders = null;

			m_navSet.OnTaskComplete_NavWay();
			if (!nearSurface && CanTunnel())
				new TunnelMiner(m_pathfinder, Destination.FromWorld(m_targetVoxel, closestApproach.Deposit), m_oreName);
			else
				new SurfaceMiner(m_pathfinder, Destination.FromWorld(m_targetVoxel, closestApproach.Deposit), m_oreName);

			m_stage = Stage.Mining;
		}

		private bool Return()
		{
			if (DrillFullness() > FullAmount_Return)
			{
				Log.DebugLog("Drills are full", Logger.severity.DEBUG);
				m_navSet.Settings_Commands.Complaint |= InfoString.StringId.ReturnCause_Full;
				return true;
			}
			else if (!SufficientAcceleration(MinAccel_Return))
			{
				Log.DebugLog("Not enough acceleration", Logger.severity.DEBUG);
				m_navSet.Settings_Commands.Complaint |= InfoString.StringId.ReturnCause_Heavy;
				return true;
			}
			else if (m_mover.ThrustersOverWorked())
			{
				Log.DebugLog("Thrusters overworked", Logger.severity.DEBUG);
				m_navSet.Settings_Commands.Complaint |= InfoString.StringId.ReturnCause_OverWorked;
				return true;
			}
			return false;
		}

	}
}
