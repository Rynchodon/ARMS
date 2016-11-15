using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using Rynchodon.Autopilot.Data;
using Rynchodon.Autopilot.Harvest;
using Rynchodon.Autopilot.Pathfinding;
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

		public enum Stage : byte { None, GetDeposit, Approach, Mining }

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

		private readonly Logger m_logger;
		private readonly byte[] m_oreTargets;
		private Vector3D m_approachPosition;
		private AMinerComponent m_miner;

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

				m_logger.debugLog("stage changed from " + value_stage + " to " + value, Logger.severity.DEBUG);
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
							m_miner = null;
							m_logger.debugLog("Waiting on ore detector", Logger.severity.DEBUG);
							break;
						}
				}

				m_navSet.OnTaskComplete_NavWay();
				value_stage = value;
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

			BoundingSphereD nearby = new BoundingSphereD(navDrill.WorldPosition, m_controlBlock.CubeGrid.LocalVolume.Radius * 2d);
			List<MyVoxelBase> nearbyVoxels = new List<MyVoxelBase>();
			MyGamePruningStructure.GetAllVoxelMapsInSphere(ref nearby, nearbyVoxels);

			foreach (MyVoxelBase voxel in nearbyVoxels)
				if ((voxel is IMyVoxelMap || voxel is MyPlanet) && voxel.Intersects(ref nearby))
				{
					m_logger.debugLog("near a voxel, escape first", Logger.severity.DEBUG);
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
				case Stage.Approach:
					customInfo.Append("Moving to ");
					customInfo.AppendLine(m_approachPosition.ToPretty());
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
				case Stage.Approach:
					if (m_navSet.DistanceLessThan(1f))
					{
						m_logger.debugLog("Approached, start mining", Logger.severity.DEBUG);
						m_navSet.OnTaskComplete_NavWay();
						m_miner.Start();
						m_stage = Stage.Mining;
						return;
					}
					Destination dest = Destination.FromWorld(m_targetVoxel, ref m_approachPosition);
					m_pathfinder.MoveTo(destinations: dest);
					return;
				case Stage.Mining:
					m_logger.debugLog("Finished mining", Logger.severity.DEBUG);
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
							//m_logger.debugLog(block.getBestName() + " is behind a drill");
							goto NextBlock;
						}

					//m_logger.debugLog(block.getBestName() + " is not behind any drill");
					return false;
				}

				NextBlock:;
			}

			return true;
		}

		private void OnOreSearchComplete(bool success, Vector3D orePosition, IMyVoxelBase foundVoxel, string oreName)
		{
			m_logger.debugLog("success: " + success + ", orePosition: " + orePosition + ", foundVoxel: " + foundVoxel + ", oreName: " + oreName, Logger.severity.DEBUG);

			if (!success)
			{
				m_logger.debugLog("No ore target found", Logger.severity.INFO);
				m_navSet.OnTaskComplete_NavRot();
				m_navSet.Settings_Commands.Complaint = InfoString.StringId.NoOreFound;
				return;
			}

			m_targetVoxel = foundVoxel;
			SetApproachPoint(ref orePosition);

			if (CanTunnel()) // and ore is not near surface
				m_miner = new TunnelMiner(m_pathfinder, Destination.FromWorld(m_targetVoxel, ref orePosition), oreName);
			else
				m_miner = new SurfaceMiner(m_pathfinder, Destination.FromWorld(m_targetVoxel, ref orePosition), oreName);

			m_stage = Stage.Approach;
		}

		private void SetApproachPoint(ref Vector3D orePosition)
		{
			// TODO: get a point inside asteroid

			CapsuleD capsule;
			capsule.P1 = orePosition;
			capsule.Radius = m_grid.LocalVolume.Radius * 4f;

			Vector3D centre = m_targetVoxel.GetCentre();
			Vector3D toDepositFromCentre; Vector3D.Subtract(ref orePosition, ref centre, out toDepositFromCentre);
			m_logger.debugLog("ore: " + orePosition + ", centre: " + centre + ", toDepositFromCentre: " + toDepositFromCentre);
			toDepositFromCentre.Normalize();
			Vector3D offset; Vector3D.Multiply(ref toDepositFromCentre, m_targetVoxel.LocalVolume.Radius, out offset);
			Vector3D.Add(ref centre, ref offset, out capsule.P0);

			CapsuleDExtensions.Intersects(ref capsule, (MyVoxelBase)m_targetVoxel, out m_approachPosition);
			m_logger.debugLog("Capsule: " + capsule.String() + ", hit: " + m_approachPosition);
		}

		private bool Return()
		{
			if (DrillFullness() > FullAmount_Return)
			{
				m_logger.debugLog("Drills are full", Logger.severity.DEBUG);
				m_navSet.Settings_Commands.Complaint |= InfoString.StringId.ReturnCause_Full;
				return true;
			}
			else if (!SufficientAcceleration(MinAccel_Return))
			{
				m_logger.debugLog("Not enough acceleration", Logger.severity.DEBUG);
				m_navSet.Settings_Commands.Complaint |= InfoString.StringId.ReturnCause_Heavy;
				return true;
			}
			else if (m_mover.ThrustersOverWorked())
			{
				m_logger.debugLog("Thrusters overworked", Logger.severity.DEBUG);
				m_navSet.Settings_Commands.Complaint |= InfoString.StringId.ReturnCause_OverWorked;
				return true;
			}
			return false;
		}

	}
}
