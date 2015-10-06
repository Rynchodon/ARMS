using System;
using System.Collections.Generic;
using System.Linq;

using Sandbox.Definitions;
using Sandbox.Game.Entities;
using Sandbox.ModAPI;
using VRage.Voxels;
using VRageMath;
using Ingame = Sandbox.ModAPI.Ingame;

namespace Rynchodon.Autopilot.Harvest
{
	public class OreDetector
	{

		private class VoxelData
		{
			#region SE Constants

			private const int QUERY_LOD = 1;

			#endregion

			private const byte VOXEL_ISO_LEVEL = 127;
			private static readonly TimeSpan LifeSpan = new TimeSpan(1, 0, 0);
			private static readonly bool[] RareMaterials;
			private static readonly Vector3I OneOneOne = new Vector3I(1, 1, 1);

			private static readonly ThreadManager m_thread = new ThreadManager(2, true);

			static VoxelData()
			{
				RareMaterials = new bool[MyDefinitionManager.Static.VoxelMaterialCount];
				for (byte materialIndex = 0; materialIndex < MyDefinitionManager.Static.VoxelMaterialCount; materialIndex++)
					RareMaterials[materialIndex] = MyDefinitionManager.Static.GetVoxelMaterialDefinition(materialIndex).IsRare;
			}

			private readonly Logger m_logger;
			private readonly Ingame.IMyOreDetector m_oreDetector;
			private readonly IMyVoxelMap m_voxel;

			private readonly Dictionary<Vector3I, byte> m_materialLocations = new Dictionary<Vector3I, byte>();
			private readonly FastResourceLock lock_materialLocations = new FastResourceLock();
			
			private readonly FastResourceLock lock_readVoxels = new FastResourceLock();
			private DateTime m_expiresAt = DateTime.UtcNow + LifeSpan;

			public VoxelData(Ingame.IMyOreDetector oreDetector, IMyVoxelMap voxel)
			{
				this.m_logger = new Logger(GetType().Name, () => oreDetector.CubeGrid.DisplayName, () => oreDetector.DisplayNameText, () => voxel.ToString());
				this.m_oreDetector = oreDetector;
				this.m_voxel = voxel;

				m_logger.debugLog("Created for voxel at " + voxel.PositionLeftBottomCorner, "VoxelData()");
			}

			public bool IsValid()
			{ return DateTime.UtcNow < m_expiresAt; }

			public bool GetClosest(byte oreType, ref Vector3I voxelCoord, out Vector3I closest)
			{
				m_logger.debugLog("searching for: " + oreType, "GetClosest()");

				bool found = false;
				closest = Vector3I.Zero;

				float closestDistance = float.MaxValue;
				using (lock_materialLocations.AcquireSharedUsing())
				{
					m_logger.debugLog("material count: " + m_materialLocations.Count, "GetClosest()");
					foreach (var matLoc in m_materialLocations)
						if (matLoc.Value == oreType)
						{
							float dist = matLoc.Key.DistanceSquared(voxelCoord);
							if (dist < closestDistance)
							{
								closest = matLoc.Key;
								closestDistance = dist;
								found = true;
							}
						}
				}

				return found;
			}

			public void StartRead()
			{
				// if already queued/running, just skip the update
				if (!lock_readVoxels.TryAcquireExclusive())
					return;
				try
				{
					m_expiresAt = DateTime.UtcNow + LifeSpan;

					Vector3D odPos = m_oreDetector.GetPosition();
					Vector3D minWorld = odPos - m_oreDetector.Range;
					Vector3D maxWorld = odPos + m_oreDetector.Range;
					Vector3D referenceVoxelMapPosition = m_voxel.PositionLeftBottomCorner - (m_voxel as MyVoxelMap).StorageMin * MyVoxelConstants.VOXEL_SIZE_IN_METRES;

					Vector3I odPosVoxelCoord; MyVoxelCoordSystems.WorldPositionToVoxelCoord(referenceVoxelMapPosition, ref odPos, out odPosVoxelCoord);
					float rangeSquared = m_oreDetector.Range * m_oreDetector.Range;

					Vector3I minLocal, maxLocal;
					MyVoxelCoordSystems.WorldPositionToVoxelCoord(referenceVoxelMapPosition, ref minWorld, out minLocal);
					MyVoxelCoordSystems.WorldPositionToVoxelCoord(referenceVoxelMapPosition, ref maxWorld, out maxLocal);

					MyVoxelMap vox = m_voxel as MyVoxelMap;
					minLocal = Vector3I.Clamp(minLocal, vox.StorageMin, vox.StorageMax);
					maxLocal = Vector3I.Clamp(maxLocal, vox.StorageMin, vox.StorageMax);

					maxLocal -= OneOneOne;

					m_logger.debugLog("Queueing read", "StartRead()");

					m_thread.EnqueueAction(() => {
						try
						{
							Vector3I prevVector = minLocal;
							minLocal.ForEachVector(maxLocal, vector => {
								if (vector.DistanceSquared(odPosVoxelCoord) > rangeSquared)
									return false;
								ReadVoxels(prevVector, vector);
								prevVector = vector;
								return false;
							});
						}
						finally
						{ lock_readVoxels.ReleaseExclusive(); }
					});
				}
				catch (Exception ex)
				{
					m_logger.alwaysLog("Exception: " + ex, "StartRead()", Logger.severity.ERROR);
					m_expiresAt = DateTime.MinValue; // get this VoxelData thrown out instead of guessing at lock state
					throw ex;
				}
			}

			private void ReadVoxels(Vector3I lodVoxelRangeMin, Vector3I lodVoxelRangeMax)
			{
				if (m_voxel == null || m_voxel.Storage == null)
					return;

				MyStorageDataCache storage = new MyStorageDataCache();
				storage.Resize(lodVoxelRangeMin, lodVoxelRangeMax);

				m_voxel.Storage.ReadRange(storage, MyStorageDataTypeFlags.ContentAndMaterial, QUERY_LOD, lodVoxelRangeMin, lodVoxelRangeMax);

				Vector3I depositPosition = lodVoxelRangeMin * 2;

				int iMax = storage.SizeLinear / storage.StepLinear;
				for (int i = 0; i < iMax; i += storage.StepLinear)
					if (storage.Content(i) > VOXEL_ISO_LEVEL)
					{
						byte mat = storage.Material(i);
						if (RareMaterials[mat])
							using (lock_materialLocations.AcquireExclusiveUsing())
							{
								m_materialLocations[depositPosition] = mat;
								return;
							}
					}

				// nothing found
				using (lock_materialLocations.AcquireExclusiveUsing())
					if (m_materialLocations.Remove(depositPosition))
						m_logger.debugLog("removed ore at: " + depositPosition, "ReadVoxels()");
			}

		}

		public static readonly Dictionary<long, OreDetector> registry = new Dictionary<long, OreDetector>();
		private static readonly TimeSpan UpdateStationary = new TimeSpan(0, 0, 1);

		private readonly Logger m_logger;
		private readonly IMyCubeBlock m_block;
		private readonly Ingame.IMyOreDetector m_oreDetector;

		private readonly Dictionary<IMyVoxelMap, VoxelData> m_voxelData = new Dictionary<IMyVoxelMap, VoxelData>();

		private Vector3D m_previousPosition;
		private DateTime m_nextStationary;

		public OreDetector(IMyCubeBlock oreDetector)
		{
			this.m_logger = new Logger("OreDetector", oreDetector);
			this.m_block = oreDetector;
			this.m_oreDetector = oreDetector as Ingame.IMyOreDetector;

			registry.Add(m_block.EntityId, this);
			m_oreDetector.OnClose += obj => registry.Remove(m_block.EntityId);
		}

		public void Update()
		{
			if (!m_block.IsWorking)
				return;
			if (DateTime.UtcNow < m_nextStationary && Vector3D.DistanceSquared(m_oreDetector.GetPosition(), m_previousPosition) < 100d)
				return;

			m_nextStationary = DateTime.UtcNow + UpdateStationary;
			m_previousPosition = m_oreDetector.GetPosition();

			m_logger.debugLog("running update", "Update()");

			BoundingSphereD detection = new BoundingSphereD(m_oreDetector.GetPosition(), m_oreDetector.Range);
			List<MyVoxelBase> nearby = new List<MyVoxelBase>();
			MyGamePruningStructure.GetAllVoxelMapsInSphere(ref detection, nearby);

			foreach (MyVoxelMap nearbyMap in nearby)
			{
				VoxelData data;
				if (!m_voxelData.TryGetValue(nearbyMap, out data))
				{
					data = new VoxelData(m_oreDetector, nearbyMap);
					m_voxelData.Add(nearbyMap, data);
				}

				data.StartRead();
			}

			foreach (var voxelData in m_voxelData)
				if (!voxelData.Value.IsValid())
				{
					m_logger.debugLog("removing old: " + voxelData.Key, "Update()");
					m_voxelData.Remove(voxelData.Key);
					break;
				}
		}

		public bool FindClosestOre(Vector3D position, byte oreType, out Vector3D orePosition)
		{
			m_logger.debugLog("searching for: " + oreType, "FindClosestOre()");

			var sortedByDistance = m_voxelData.Keys.OrderBy(map => Vector3.DistanceSquared(position, map.GetPosition()));

			foreach (IMyVoxelMap map in sortedByDistance)
			{
				VoxelData data = m_voxelData[map];

				Vector3I myVoxelCoord; MyVoxelCoordSystems.WorldPositionToVoxelCoord(map.PositionLeftBottomCorner, ref position, out myVoxelCoord);
				m_logger.debugLog("PositionLeftBottomCorner: " + map.PositionLeftBottomCorner + ", pos: " + position + ", myVoxelCoord: " + myVoxelCoord, "FindClosestOre()");
				Vector3I closest;
				if (data.GetClosest(oreType, ref myVoxelCoord, out closest))
				{
					MyVoxelCoordSystems.VoxelCoordToWorldPosition(map.PositionLeftBottomCorner, ref closest, out orePosition);
					m_logger.debugLog("closest: " + closest + ", PositionLeftBottomCorner: " + map.PositionLeftBottomCorner.ToGpsTag("Asteroid") + ", worldPosition: " + orePosition, "FindClosestOre()");
					return true;
				}
			}

			orePosition = Vector3D.Zero;
			return false;
		}

	}
}
