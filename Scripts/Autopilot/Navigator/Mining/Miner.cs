using System;
using System.Collections.Generic;
using System.Text;
using Rynchodon.Autopilot.Data;
using Rynchodon.Autopilot.Harvest;
using Rynchodon.Autopilot.Pathfinding;
using Sandbox.Common.ObjectBuilders;
using Sandbox.Game.Entities;
using Sandbox.ModAPI;
using VRage.Game.ModAPI;
using VRage.ModAPI;
using VRageMath;

namespace Rynchodon.Autopilot.Navigator.Mining
{
	class Miner : NavigatorMover, INavigatorRotator
	{
		private readonly Logger m_logger;
		private readonly byte[] m_oreTargets;

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

			m_logger = new Logger(navDrill);
			m_navSet.Settings_Task_NavRot.NavigatorMover = this;
			m_navSet.Settings_Task_NavRot.NavigationBlock = navDrill;

			BoundingSphereD nearby = new BoundingSphereD(navDrill.WorldPosition, m_controlBlock.CubeGrid.LocalVolume.Radius * 4d);
			List<MyVoxelBase> nearbyVoxels = new List<MyVoxelBase>();
			MyGamePruningStructure.GetAllVoxelMapsInSphere(ref nearby, nearbyVoxels);

			foreach (MyVoxelBase voxel in nearbyVoxels)
				if ((voxel is IMyVoxelMap || voxel is MyPlanet) && voxel.Intersects(ref nearby))
				{
					m_logger.debugLog("near a voxel, escape first", Logger.severity.DEBUG);
					new TunnelMiner(m_pathfinder, new Destination(voxel, ref Vector3D.Zero), TunnelMiner.Stage.Backout);
				}
		}

		public override void AppendCustomInfo(StringBuilder customInfo)
		{
			//throw new NotImplementedException();
		}

		public override void Move()
		{
			// get a bunch of targets
			//OreDetector.SearchForMaterial(m_mover.Block, 


			// move to closest one
			// create surface miner or tunneler
		}

		public void Rotate()
		{
			m_mover.CalcRotate();
		}

		/// <summary>
		/// Determines if the ship is capable of digging a tunnel.
		/// </summary>
		private bool CanTunnel()
		{

		}
	}
}
