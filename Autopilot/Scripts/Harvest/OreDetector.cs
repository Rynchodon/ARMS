using System;
using System.Collections.Generic;
using System.Linq;
using Rynchodon.Threading;
using Sandbox.Definitions;
using Sandbox.Game.Entities;
using Sandbox.ModAPI;
using VRage;
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
			private static readonly TimeSpan LifeSpan = new TimeSpan(0, 1, 0);
			private static readonly bool[] RareMaterials;
			private static readonly Vector3I OneOneOne = new Vector3I(1, 1, 1);

			private static readonly ThreadManager m_thread = new ThreadManager(1, true, "VoxelData");

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
			//private readonly FastResourceLock lock_materialLocations = new FastResourceLock();

			private readonly FastResourceLock lock_readVoxels = new FastResourceLock();

			private DateTime m_expiresAt = DateTime.UtcNow + LifeSpan;
			private readonly FastResourceLock lock_expiresAt = new FastResourceLock();

			public VoxelData(Ingame.IMyOreDetector oreDetector, IMyVoxelMap voxel)
			{
				this.m_logger = new Logger(GetType().Name, () => oreDetector.CubeGrid.DisplayName, () => oreDetector.DisplayNameText, () => voxel.ToString());
				this.m_oreDetector = oreDetector;
				this.m_voxel = voxel;

				m_logger.debugLog("Created for voxel at " + voxel.PositionLeftBottomCorner, "VoxelData()");
			}

			public bool IsValid()
			{
				using (lock_expiresAt.AcquireSharedUsing())
					return DateTime.UtcNow < m_expiresAt;
			}

			public bool GetClosest(byte[] oreType, ref Vector3I voxelCoord, out Vector3I closest, out byte foundOre)
			{
				m_logger.debugLog("searching for: " + oreType, "GetClosest()");

				bool found = false;
				closest = Vector3I.Zero;
				foundOre = 255;

				float closestDistance = float.MaxValue;
				using (lock_readVoxels.AcquireSharedUsing())
				{
					m_logger.debugLog("material count: " + m_materialLocations.Count, "GetClosest()");
					foreach (var matLoc in m_materialLocations)
						if (oreType == null || oreType.Contains(matLoc.Value))
						{
							float dist = matLoc.Key.DistanceSquared(voxelCoord);
							if (dist < closestDistance)
							{
								closest = matLoc.Key;
								closestDistance = dist;
								found = true;
								foundOre = matLoc.Value;
							}
						}
				}

				return found;
			}

			/// <summary>
			/// Start the reads if it is not already running.
			/// </summary>
			/// <param name="callback">Invoked when reads finish, not invoked if already running.</param>
			/// <returns>True if started, false if already running.</returns>
			public bool StartRead(Action callback)
			{
				// if already queued/running, just skip the update
				if (!lock_readVoxels.TryAcquireExclusive())
				{
					m_logger.debugLog("already queued or running", "StartRead()");
					return false;
				}
				try
				{
					using (lock_expiresAt.AcquireExclusiveUsing())
						m_expiresAt = DateTime.UtcNow + LifeSpan;

					Vector3D odPos = m_oreDetector.GetPosition();
					Vector3D minWorld = odPos - m_oreDetector.Range;
					Vector3D maxWorld = odPos + m_oreDetector.Range;
					Vector3D referenceVoxelMapPosition = m_voxel.PositionLeftBottomCorner - (m_voxel as MyVoxelMap).StorageMin * MyVoxelConstants.VOXEL_SIZE_IN_METRES;

					Vector3D worldSize = maxWorld - minWorld;
					m_logger.debugLog("range: " + m_oreDetector.Range + ", world size: " + worldSize.X * worldSize.Y * worldSize.Z, "StartRead()");

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
							Vector3I size = maxLocal - minLocal;
							m_logger.debugLog("performing read of " + size.X * size.Y * size.Z + " voxel coords", "StartRead()");
							Vector3I prevVector = minLocal;
							minLocal.ForEachVector(maxLocal, vector => {
								if (vector.DistanceSquared(odPosVoxelCoord) > rangeSquared)
									return false;
								ReadVoxels(prevVector, vector);
								prevVector = vector;
								return false;
							});
							m_logger.debugLog("finished reads", "StartRead()");
						}
						finally
						{
							lock_readVoxels.ReleaseExclusive();
							callback.Invoke();
						}
					});
				}
				catch (Exception ex)
				{
					m_logger.alwaysLog("Exception: " + ex, "StartRead()", Logger.severity.ERROR);
					using (lock_expiresAt.AcquireExclusiveUsing())
						m_expiresAt = DateTime.MinValue; // get this VoxelData thrown out instead of guessing at lock state
					throw ex;
				}
				return true;
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
						//using (lock_materialLocations.AcquireExclusiveUsing())
						{
							m_materialLocations[depositPosition] = mat;
							return;
						}
					}

				// nothing found
				//using (lock_materialLocations.AcquireExclusiveUsing())
				if (m_materialLocations.Remove(depositPosition))
					m_logger.debugLog("removed ore at: " + depositPosition, "ReadVoxels()");
			}

		}

		#region Static

		private static readonly TimeSpan UpdateStationary = new TimeSpan(0, 10, 0);

		private static readonly Dictionary<long, OreDetector> registry = new Dictionary<long, OreDetector>();
		private static readonly FastResourceLock lock_registry = new FastResourceLock();

		/// <summary>
		/// Material indecies by chemical symbol, subtype, and MinedOre.
		/// </summary>
		private static Dictionary<string, byte[]> MaterialGroup;

		static OreDetector()
		{
			var defs = MyDefinitionManager.Static.GetVoxelMaterialDefinitions();
			Dictionary<string, List<byte>> MaterialGroup = new Dictionary<string, List<byte>>();
			foreach (var def in defs)
			{
				string subtype = def.Id.SubtypeName.Split('_')[0].Trim().ToLower();
				string minedOre = def.MinedOre.Trim().ToLower();

				List<byte> addTo;
				if (!MaterialGroup.TryGetValue(subtype, out addTo))
				{
					addTo = new List<byte>();
					MaterialGroup.Add(subtype, addTo);
				}
				addTo.Add(def.Index);

				if (!MaterialGroup.TryGetValue(minedOre, out addTo))
				{
					addTo = new List<byte>();
					MaterialGroup.Add(minedOre, addTo);
				}
				addTo.Add(def.Index);

				string symbol;
				if (GetChemicalSymbol(subtype, out symbol))
				{
					if (!MaterialGroup.TryGetValue(symbol, out addTo))
					{
						addTo = new List<byte>();
						MaterialGroup.Add(symbol, addTo);
					}
					addTo.Add(def.Index);
				}
			}

			OreDetector.MaterialGroup = new Dictionary<string, byte[]>();
			foreach (var pair in MaterialGroup)
				OreDetector.MaterialGroup.Add(pair.Key, pair.Value.ToArray());
		}

		public static bool TryGetMaterial(string oreName, out byte[] oreTypes)
		{ return MaterialGroup.TryGetValue(oreName.Trim().ToLower(), out oreTypes); }

		public static bool TryGetDetector(long entityId, out OreDetector value)
		{
			using (lock_registry.AcquireSharedUsing())
				return registry.TryGetValue(entityId, out value);
		}

		/// <param name="subtypeName">SubtypeId without the _##</param>
		private static bool GetChemicalSymbol(string subtypeName, out string symbol)
		{
			switch (subtypeName)
			{
				case "cobalt":
					symbol = "co";
					return true;
				case "gold":
					symbol = "au";
					return true;
				case "iron":
					symbol = "fe";
					return true;
				case "magnesium":
					symbol = "mg";
					return true;
				case "nickel":
					symbol = "ni";
					return true;
				case "platinum":
					symbol = "pt";
					return true;
				case "silicon":
					symbol = "si";
					return true;
				case "silver":
					symbol = "ag";
					return true;
				case "uraninite":
					symbol = "u";
					return true;
			}
			symbol = null;
			return false;
		}

		#endregion

		public readonly IMyCubeBlock Block;

		/// <summary>Dequeues and invokes all actions finished updating voxels.</summary>
		public readonly LockedQueue<Action> OnUpdateComplete = new LockedQueue<Action>();

		private readonly Logger m_logger;
		private readonly Ingame.IMyOreDetector m_oreDetector;

		private readonly Dictionary<IMyVoxelMap, VoxelData> m_voxelData = new Dictionary<IMyVoxelMap, VoxelData>();
		private readonly FastResourceLock l_voxelDate = new FastResourceLock();

		private byte m_waitingOn;
		private readonly FastResourceLock l_waitingOn = new FastResourceLock();

		/// <summary>
		/// Create an OreDetector for the given block.
		/// </summary>
		/// <param name="oreDetector">The ore detector block.</param>
		public OreDetector(IMyCubeBlock oreDetector)
		{
			this.m_logger = new Logger("OreDetector", oreDetector);
			this.Block = oreDetector;
			this.m_oreDetector = oreDetector as Ingame.IMyOreDetector;

			using (lock_registry.AcquireExclusiveUsing())
				registry.Add(Block.EntityId, this);
			m_oreDetector.OnClose += obj => {
				using (lock_registry.AcquireExclusiveUsing())
					registry.Remove(Block.EntityId);
			};
		}

		/// <summary>
		/// Removes old voxel data.
		/// </summary>
		public void Update()
		{
			using (l_voxelDate.AcquireExclusiveUsing())
				foreach (var voxelData in m_voxelData)
					if (!voxelData.Value.IsValid())
					{
						m_logger.debugLog("removing old: " + voxelData.Key, "Update()");
						m_voxelData.Remove(voxelData.Key);
						break;
					}
		}

		/// <summary>
		/// Updates the ore locations then fires OnUpdateComplete
		/// </summary>
		public void UpdateOreLocations()
		{
			if (!Block.IsWorking)
			{
				m_logger.debugLog("not working: " + Block.DisplayNameText, "Update()");
				return;
			}
			m_logger.debugLog("running update", "Update()");

			BoundingSphereD detection = new BoundingSphereD(m_oreDetector.GetPosition(), m_oreDetector.Range);
			List<MyVoxelBase> nearby = new List<MyVoxelBase>();

			MainLock.UsingShared(() => MyGamePruningStructure.GetAllVoxelMapsInSphere(ref detection, nearby));

			foreach (MyVoxelMap nearbyMap in nearby)
			{
				VoxelData data;
				using (l_voxelDate.AcquireExclusiveUsing())
					if (!m_voxelData.TryGetValue(nearbyMap, out data))
					{
						data = new VoxelData(m_oreDetector, nearbyMap);
						m_voxelData.Add(nearbyMap, data);
					}

				using (l_waitingOn.AcquireExclusiveUsing())
					if (data.StartRead(OnVoxelFinish))
						m_waitingOn++;
			}
		}

		/// <summary>
		/// Find the closest ore to position that matches one of the oreType
		/// </summary>
		/// <param name="position">The position to search near</param>
		/// <param name="oreType">The types of ore to search for</param>
		/// <param name="orePosition">The postion of the ore that was found</param>
		/// <param name="voxel">The voxel map that contains the ore that was found</param>
		/// <param name="oreName">The name of the ore that was found</param>
		/// <returns>True iff an ore was found</returns>
		/// TODO: make static and take a collection of OreDetector
		public bool FindClosestOre(Vector3D position, byte[] oreType, out Vector3D orePosition, out IMyVoxelMap voxel, out string oreName)
		{
			m_logger.debugLog("searching for: " + oreType, "FindClosestOre()");

			IOrderedEnumerable<IMyVoxelMap> sortedByDistance;
			using (l_voxelDate.AcquireExclusiveUsing())
				sortedByDistance = m_voxelData.Keys.OrderBy(map => Vector3.DistanceSquared(position, map.GetPosition()));

			foreach (IMyVoxelMap map in sortedByDistance)
			{
				VoxelData data;
				using (l_voxelDate.AcquireSharedUsing())
					data = m_voxelData[map];

				Vector3I myVoxelCoord; MyVoxelCoordSystems.WorldPositionToVoxelCoord(map.PositionLeftBottomCorner, ref position, out myVoxelCoord);
				m_logger.debugLog("PositionLeftBottomCorner: " + map.PositionLeftBottomCorner + ", pos: " + position + ", myVoxelCoord: " + myVoxelCoord, "FindClosestOre()");
				Vector3I closest;
				byte foundOreType;
				if (data.GetClosest(oreType, ref myVoxelCoord, out closest, out foundOreType))
				{
					oreName = MyDefinitionManager.Static.GetVoxelMaterialDefinition(foundOreType).MinedOre;
					MyVoxelCoordSystems.VoxelCoordToWorldPosition(map.PositionLeftBottomCorner, ref closest, out orePosition);
					m_logger.debugLog("closest: " + closest + ", PositionLeftBottomCorner: " + map.PositionLeftBottomCorner + ", worldPosition: " + orePosition, "FindClosestOre()");
					voxel = map;
					return true;
				}
			}

			orePosition = Vector3D.Zero;
			voxel = null;
			oreName = null;
			return false;
		}

		private void OnVoxelFinish()
		{
			using (l_waitingOn.AcquireExclusiveUsing())
			{
				m_logger.debugLog("entered", "OnVoxelFinish()");
				m_waitingOn--;

				if (m_waitingOn == 0)
				{
					m_logger.debugLog("All voxels are finished, call miner", "OnVoxelFinish()", Logger.severity.DEBUG);
					OnUpdateComplete.DequeueAll(act => act.Invoke());
				}
			}
		}

	}
}
