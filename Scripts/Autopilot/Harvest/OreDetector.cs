using System;
using System.Collections.Generic;
using System.Linq;
using Rynchodon.AntennaRelay;
using Rynchodon.Threading;
using Sandbox.Definitions;
using Sandbox.Game.Entities;
using Sandbox.ModAPI;
using VRage;
using VRage.Game.ModAPI;
using VRage.ModAPI;
using VRage.Voxels;
using VRageMath;

namespace Rynchodon.Autopilot.Harvest
{
	public class OreDetector
	{

		private class VoxelData
		{

			private const int QUERY_LOD = 1;
			private const int QUERY_STEP = 2;
			private const int QUERY_MAX = QUERY_STEP - 1;

			private static readonly TimeSpan LifeSpan_VoxelData = new TimeSpan(1, 0, 0);
			private static bool[] RareMaterials;

			static VoxelData()
			{
				RareMaterials = new bool[MyDefinitionManager.Static.VoxelMaterialCount];
				for (byte materialIndex = 0; materialIndex < MyDefinitionManager.Static.VoxelMaterialCount; materialIndex++)
					RareMaterials[materialIndex] = MyDefinitionManager.Static.GetVoxelMaterialDefinition(materialIndex).IsRare;
				MyAPIGateway.Entities.OnCloseAll += Entities_OnCloseAll;
			}

			private static void Entities_OnCloseAll()
			{
				MyAPIGateway.Entities.OnCloseAll -= Entities_OnCloseAll;
				RareMaterials = null;
			}

			private readonly Logger m_logger;
			private readonly IMyOreDetector m_oreDetector;
			private readonly IMyVoxelBase m_voxel;
			private readonly float m_maxRange;

			private readonly Dictionary<Vector3I, byte> m_materialLocations = new Dictionary<Vector3I, byte>(1000);
			private readonly MyStorageData m_storage = new MyStorageData();

			private TimeSpan m_throwOutVoxelData = Globals.ElapsedTime + LifeSpan_VoxelData;
			private readonly FastResourceLock lock_throwOut = new FastResourceLock();

			public bool NeedsUpdate { get; private set; }

			public VoxelData(IMyOreDetector oreDetector, IMyVoxelBase voxel, float maxRange)
			{
				this.m_logger = new Logger(() => oreDetector.CubeGrid.DisplayName, () => oreDetector.DisplayNameText, () => voxel.ToString());
				this.m_oreDetector = oreDetector;
				this.m_voxel = voxel;
				this.m_storage.Resize(new Vector3I(QUERY_STEP, QUERY_STEP, QUERY_STEP));
				this.m_maxRange = maxRange;
				(this.m_voxel as MyVoxelBase).RangeChanged += m_voxel_RangeChanged;
				this.NeedsUpdate = true;

				m_logger.debugLog("Created for voxel at " + voxel.PositionLeftBottomCorner);
			}

			private void m_voxel_RangeChanged(MyVoxelBase storage, Vector3I minVoxelChanged, Vector3I maxVoxelChanged, MyStorageDataTypeFlags changedData)
			{
				NeedsUpdate = true;
			}

			/// <returns>False iff VoxelData is very old and should be disposed of.</returns>
			public bool IsValid()
			{
				using (lock_throwOut.AcquireExclusiveUsing())
					return Globals.ElapsedTime < m_throwOutVoxelData;
			}

			public bool GetClosest(byte[] oreType, ref Vector3D worldPosition, out Vector3D closest, out byte foundOre)
			{
				if (oreType == null)
					m_logger.debugLog("searching for any");
				else
					foreach (byte b in oreType)
						m_logger.debugLog("searching for: " + b);

				Vector3I search_voxelCellCoord;
				MyVoxelCoordSystems.WorldPositionToVoxelCoord(m_voxel.PositionLeftBottomCorner, ref worldPosition, out search_voxelCellCoord);
				search_voxelCellCoord >>= QUERY_LOD;

				bool found = false;
				closest = Vector3D.Zero;
				foundOre = 255;

				int closestDistance = int.MaxValue;
				m_logger.debugLog("material count: " + m_materialLocations.Count);
				foreach (var matLoc in m_materialLocations)
					if (oreType == null || oreType.Contains(matLoc.Value))
					{
						int dist = matLoc.Key.DistanceSquared(search_voxelCellCoord);
						if (dist < closestDistance)
						{
							Vector3D deposit_localPosition = matLoc.Key << QUERY_LOD;
							MyVoxelCoordSystems.LocalPositionToWorldPosition(m_voxel.PositionLeftBottomCorner, ref deposit_localPosition, out closest);

							m_logger.debugLog("entry position: " + matLoc.Key + ", local: " + deposit_localPosition + ", world: " + closest + ", distance: " + (float)Math.Sqrt(dist));

							MyVoxelBase map = m_voxel as MyVoxelBase;
							m_logger.debugLog("stor min: " + map.StorageMin);

							closestDistance = dist;
							found = true;
							foundOre = matLoc.Value;
						}
					}

				return found;
			}

			/// <summary>
			/// Start the reads if it is not already running.
			/// </summary>
			/// <param name="onFinished">Invoked when reads finish, not invoked if already running.</param>
			/// <returns>True if started, false if already running.</returns>
			public void Read()
			{
				using (lock_throwOut.AcquireExclusiveUsing())
					m_throwOutVoxelData = Globals.ElapsedTime + LifeSpan_VoxelData;

				NeedsUpdate = false;
				Vector3D m_oreDetectorPosition = m_oreDetector.GetPosition();
				Vector3D worldMin = m_oreDetectorPosition - m_maxRange;
				Vector3D worldMax = m_oreDetectorPosition + m_maxRange;

				float rangeSquared = m_maxRange * m_maxRange;

				Vector3I odPosVoxelStorage, m_localMin, m_localMax;
				MyVoxelCoordSystems.WorldPositionToVoxelCoord(m_voxel.PositionLeftBottomCorner, ref m_oreDetectorPosition, out odPosVoxelStorage);
				MyVoxelCoordSystems.WorldPositionToVoxelCoord(m_voxel.PositionLeftBottomCorner, ref worldMin, out m_localMin);
				MyVoxelCoordSystems.WorldPositionToVoxelCoord(m_voxel.PositionLeftBottomCorner, ref worldMax, out m_localMax);

				MyVoxelBase vox = m_voxel as MyVoxelBase;
				if (m_voxel == null || m_voxel.Storage == null)
					return;

				m_localMin = Vector3I.Clamp(m_localMin, vox.StorageMin, vox.StorageMax);
				m_localMax = Vector3I.Clamp(m_localMax, vox.StorageMin, vox.StorageMax);
				m_localMin >>= QUERY_LOD;
				m_localMax >>= QUERY_LOD;
				odPosVoxelStorage >>= QUERY_LOD;
				m_logger.debugLog("minLocal: " + m_localMin + ", maxLocal: " + m_localMax + ", odPosVoxelStorage: " + odPosVoxelStorage);

				Vector3I size = m_localMax - m_localMin;
				m_logger.debugLog("number of coords in box: " + (size.X + 1) * (size.Y + 1) * (size.Z + 1));
				ulong processed = 0;
				m_materialLocations.Clear();

				Vector3I vector = new Vector3I();
				for (vector.X = m_localMin.X; vector.X < m_localMax.X; vector.X += QUERY_STEP)
					for (vector.Y = m_localMin.Y; vector.Y < m_localMax.Y; vector.Y += QUERY_STEP)
						for (vector.Z = m_localMin.Z; vector.Z < m_localMax.Z; vector.Z += QUERY_STEP)
							if (vector.DistanceSquared(odPosVoxelStorage) <= rangeSquared)
							{
								m_voxel.Storage.ReadRange(m_storage, MyStorageDataTypeFlags.ContentAndMaterial, QUERY_LOD, vector, vector + QUERY_MAX);

								Vector3I index = Vector3I.Zero;
								Vector3I size3D = m_storage.Size3D;
								for (index.X = 0; index.X < size3D.X; index.X++)
									for (index.Y = 0; index.Y < size3D.Y; index.Y++)
										for (index.Z = 0; index.Z < size3D.Z; index.Z++)
										{
											int linear = m_storage.ComputeLinear(ref index);
											if (m_storage.Content(linear) > MyVoxelConstants.VOXEL_ISO_LEVEL)
											{
												byte mat = m_storage.Material(linear);
												if (RareMaterials[mat])
												{
													//m_logger.debugLog("mat: " + mat + ", content: " + m_storage.Content(linear) + ", vector: " + vector + ", position: " + vector + index
													//	+ ", name: " + MyDefinitionManager.Static.GetVoxelMaterialDefinition(mat).MinedOre, "Read()");
													m_materialLocations[vector + index] = mat;
													processed++;
													goto Finished_Deposit;
												}
											}
										}

Finished_Deposit:
								processed++;
							}

				m_logger.debugLog("read " + processed + ", chunks" + ", number of mats: " + m_materialLocations.Count, Logger.severity.DEBUG);
			}

		}

		#region Static

		private static ThreadManager m_thread = new ThreadManager(2, true, "OreDetector");
		public delegate void OreSearchComplete(bool success, Vector3D orePosition, IMyVoxelBase voxel, string oreName);

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
				if (!addTo.Contains(def.Index))
					addTo.Add(def.Index);

				if (!MaterialGroup.TryGetValue(minedOre, out addTo))
				{
					addTo = new List<byte>();
					MaterialGroup.Add(minedOre, addTo);
				}
				if (!addTo.Contains(def.Index))
					addTo.Add(def.Index);

				string symbol;
				if (GetChemicalSymbol(subtype, out symbol))
				{
					if (!MaterialGroup.TryGetValue(symbol, out addTo))
					{
						addTo = new List<byte>();
						MaterialGroup.Add(symbol, addTo);
					}
					if (!addTo.Contains(def.Index))
						addTo.Add(def.Index);
				}
			}

			OreDetector.MaterialGroup = new Dictionary<string, byte[]>();
			foreach (var pair in MaterialGroup)
				OreDetector.MaterialGroup.Add(pair.Key, pair.Value.ToArray());

			MyAPIGateway.Entities.OnCloseAll += Entities_OnCloseAll;
		}

		private static void Entities_OnCloseAll()
		{
			MyAPIGateway.Entities.OnCloseAll -= Entities_OnCloseAll;
			MaterialGroup = null;
			m_thread = null;
		}

		public static bool TryGetMaterial(string oreName, out byte[] oreTypes)
		{ return MaterialGroup.TryGetValue(oreName.Trim().ToLower(), out oreTypes); }

		/// <param name="subtypeName">SubtypeId without the _##</param>
		public static bool GetChemicalSymbol(string subtypeName, out string symbol)
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

		/// <summary>
		/// Search every possible ore detector for materials.
		/// </summary>
		/// <param name="requester">Must be able to control OreDetector and have same NetworkStorage</param>
		/// <param name="oreType">Voxel materials to search for</param>
		public static void SearchForMaterial(ShipControllerBlock requester, byte[] oreType, OreSearchComplete onComplete)
		{
			Vector3D position = requester.CubeBlock.GetPosition();
			RelayStorage storage = requester.NetworkStorage;

			m_thread.EnqueueAction(() => {
				List<OreDetector> oreDetectors = ResourcePool<List<OreDetector>>.Get();
				Registrar.ForEach((OreDetector detector) => {
					if (detector.Block.IsWorking && requester.CubeBlock.canControlBlock(detector.Block) && detector.m_netClient.GetStorage() == storage)
						oreDetectors.Add(detector);
				});

				IOrderedEnumerable<OreDetector> sorted = oreDetectors.OrderBy(detector => Vector3D.DistanceSquared(position, detector.Block.GetPosition()));

				foreach (OreDetector detector in sorted)
					if (detector.GetOreLocations(position, oreType, onComplete))
						return;

				oreDetectors.Clear();
				ResourcePool<List<OreDetector>>.Return(oreDetectors);

				onComplete(false, Vector3D.Zero, null, null);
			});
		}

		#endregion

		public readonly IMyCubeBlock Block;

		private readonly List<MyVoxelBase> m_nearbyVoxel = new List<MyVoxelBase>();

		private readonly Logger m_logger;
		private readonly IMyOreDetector m_oreDetector;
		private readonly float m_maxRange;
		private readonly RelayClient m_netClient;

		private readonly Dictionary<IMyVoxelBase, VoxelData> m_voxelData = new Dictionary<IMyVoxelBase, VoxelData>();
		private readonly FastResourceLock l_voxelData = new FastResourceLock();

		private readonly FastResourceLock l_getOreLocations = new FastResourceLock();

		/// <summary>
		/// Create an OreDetector for the given block.
		/// </summary>
		/// <param name="oreDetector">The ore detector block.</param>
		public OreDetector(IMyCubeBlock oreDetector)
		{
			this.m_logger = new Logger(oreDetector);
			this.Block = oreDetector;
			this.m_oreDetector = oreDetector as IMyOreDetector;
			this.m_netClient = new RelayClient(oreDetector);

			float maxrange = 0f;
			MainLock.UsingShared(() => {
				var def = MyDefinitionManager.Static.GetCubeBlockDefinition(m_oreDetector.BlockDefinition) as MyOreDetectorDefinition;
				maxrange = def.MaximumRange;
			});
			m_maxRange = maxrange;

			Registrar.Add(Block, this);
		}

		/// <summary>
		/// Removes old voxel data.
		/// </summary>
		public void Update()
		{
			using (l_voxelData.AcquireExclusiveUsing())
				foreach (var voxelData in m_voxelData)
					if (!voxelData.Value.IsValid())
					{
						m_logger.debugLog("removing old: " + voxelData.Key);
						m_voxelData.Remove(voxelData.Key);
						break;
					}
		}

		/// <summary>
		/// Searches nearby voxels for ores.
		/// </summary>
		/// <param name="position">Position of autopilot block</param>
		/// <param name="oreType">Ore types to search for</param>
		/// <param name="onComplete">Invoked iff an ore is found</param>
		/// <returns>true iff an ore is found</returns>
		private bool GetOreLocations(Vector3D position, byte[] oreType, OreSearchComplete onComplete)
		{
			m_logger.debugLog("entered GetOreLocations()");

			BoundingSphereD detection = new BoundingSphereD(m_oreDetector.GetPosition(), m_maxRange);
			m_nearbyVoxel.Clear();
			MyGamePruningStructure.GetAllVoxelMapsInSphere(ref detection, m_nearbyVoxel);

			IOrderedEnumerable<MyVoxelBase> sorted = m_nearbyVoxel.OrderBy(voxel => Vector3D.DistanceSquared(voxel.GetCentre(), position));

			foreach (IMyVoxelBase nearbyMap in sorted)
			{
				if (nearbyMap is IMyVoxelMap || nearbyMap is MyPlanet)
				{
					VoxelData data;
					using (l_voxelData.AcquireExclusiveUsing())
						if (!m_voxelData.TryGetValue(nearbyMap, out data))
						{
							data = new VoxelData(m_oreDetector, nearbyMap, m_maxRange);
							m_voxelData.Add(nearbyMap, data);
						}

					if (data.NeedsUpdate)
					{
						m_logger.debugLog("Data needs to be updated for " + nearbyMap.getBestName());
						data.Read();
					}
					else
						m_logger.debugLog("Old data OK for " + nearbyMap.getBestName());

					Vector3D closest;
					byte foundOre;
					if (data.GetClosest(oreType, ref position, out closest, out foundOre))
					{
						m_logger.debugLog("PositionLeftBottomCorner: " + nearbyMap.PositionLeftBottomCorner + ", worldPosition: " + closest + ", distance: " + Vector3D.Distance(position, closest));
						string oreName = MyDefinitionManager.Static.GetVoxelMaterialDefinition(foundOre).MinedOre;
						onComplete(true, closest, nearbyMap, oreName);
						m_nearbyVoxel.Clear();
						return true;
					}
				}
			}

			m_nearbyVoxel.Clear();
			return false;
		}

	}
}
