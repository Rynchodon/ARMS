using System;
using System.Collections.Generic;
using System.Linq;
using Rynchodon.Threading;
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

			private const int QUERY_LOD = 1;
			private const int QUERY_STEP = 2;
			private const int QUERY_MAX = QUERY_STEP - 1;

			private static readonly TimeSpan LifeSpan_Materials = new TimeSpan(0, 1, 0);
			private static readonly TimeSpan LifeSpan_VoxelData = new TimeSpan(1, 0, 0);
			private static bool[] RareMaterials;

			private static ThreadManager m_thread = new ThreadManager(2, true, "VoxelData");

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
				m_thread = null;
			}

			private readonly Logger m_logger;
			private readonly Ingame.IMyOreDetector m_oreDetector;
			private readonly IMyVoxelBase m_voxel;
			private readonly float m_maxRange;

			private readonly Dictionary<Vector3I, byte> m_materialLocations = new Dictionary<Vector3I, byte>(10000);
			private readonly MyStorageData m_storage = new MyStorageData();

			private readonly FastResourceLock lock_readVoxels = new FastResourceLock();

			private DateTime m_throwOutMaterials = DateTime.UtcNow + LifeSpan_Materials;
			private DateTime m_throwOutVoxelData = DateTime.UtcNow + LifeSpan_VoxelData;
			private readonly FastResourceLock lock_throwOut = new FastResourceLock();

			public VoxelData(Ingame.IMyOreDetector oreDetector, IMyVoxelBase voxel, float maxRange)
			{
				this.m_logger = new Logger(GetType().Name, () => oreDetector.CubeGrid.DisplayName, () => oreDetector.DisplayNameText, () => voxel.ToString());
				this.m_oreDetector = oreDetector;
				this.m_voxel = voxel;
				this.m_storage.Resize(new Vector3I(QUERY_STEP, QUERY_STEP, QUERY_STEP));
				this.m_maxRange = maxRange;

				m_logger.debugLog("Created for voxel at " + voxel.PositionLeftBottomCorner, "VoxelData()");
			}

			/// <summary>
			/// Clears data if it is old.
			/// </summary>
			/// <returns>False iff VoxelData is very old and should be disposed of.</returns>
			public bool IsValid()
			{
				using (lock_throwOut.AcquireExclusiveUsing())
				{
					if (DateTime.UtcNow > m_throwOutMaterials)
					{
						m_materialLocations.Clear();
						m_throwOutMaterials = DateTime.MaxValue;
					}
					return DateTime.UtcNow < m_throwOutVoxelData;
				}
			}

			public bool GetClosest(byte[] oreType, ref Vector3D worldPosition, out Vector3D closest, out byte foundOre)
			{
				if (oreType == null)
					m_logger.debugLog("searching for any", "GetClosest()");
				else
					foreach (byte b in oreType)
						m_logger.debugLog("searching for: " + b, "GetClosest()");

				Vector3I search_voxelCellCoord;
				MyVoxelCoordSystems.WorldPositionToVoxelCoord(m_voxel.PositionLeftBottomCorner, ref worldPosition, out search_voxelCellCoord);
				search_voxelCellCoord >>= QUERY_LOD;

				bool found = false;
				closest = Vector3D.Zero;
				foundOre = 255;

				int closestDistance = int.MaxValue;
				using (lock_readVoxels.AcquireSharedUsing())
				{
					m_logger.debugLog("material count: " + m_materialLocations.Count, "GetClosest()");
					foreach (var matLoc in m_materialLocations)
						if (oreType == null || oreType.Contains(matLoc.Value))
						{
							int dist = matLoc.Key.DistanceSquared(search_voxelCellCoord);
							if (dist < closestDistance)
							{
								Vector3D deposit_localPosition = matLoc.Key << QUERY_LOD;
								MyVoxelCoordSystems.LocalPositionToWorldPosition(m_voxel.PositionLeftBottomCorner, ref deposit_localPosition, out closest);

								m_logger.debugLog("entry position: " + matLoc.Key + ", local: " + deposit_localPosition + ", world: " + closest + ", distance: " + (float)Math.Sqrt(dist), "GetClosest()");

								MyVoxelBase map = m_voxel as MyVoxelBase;
								m_logger.debugLog("stor min: " + map.StorageMin, "GetClosest()");

								closestDistance = dist;
								found = true;
								foundOre = matLoc.Value;
							}
						}
				}

				return found;
			}

			private Vector3I m_localMin, m_localMax;
			/// <summary>Ore detector position in voxel storage.</summary>
			private Vector3I odPosVoxelStorage;
			private float rangeSquared;
			private Action onFinished;

			/// <summary>
			/// Start the reads if it is not already running.
			/// </summary>
			/// <param name="onFinished">Invoked when reads finish, not invoked if already running.</param>
			/// <returns>True if started, false if already running.</returns>
			public bool StartRead(Action onFinished)
			{
				// if already queued/running, just skip the update
				if (!lock_readVoxels.TryAcquireExclusive())
				{
					m_logger.debugLog("already queued or running", "StartRead()", Logger.severity.INFO);
					return false;
				}
				try
				{
					using (lock_throwOut.AcquireExclusiveUsing())
					{
						m_throwOutMaterials = DateTime.UtcNow + LifeSpan_Materials;
						m_throwOutVoxelData = DateTime.UtcNow + LifeSpan_VoxelData;
					}

					this.onFinished = onFinished;
					Vector3D odPos = m_oreDetector.GetPosition();
					Vector3D worldMin = odPos - m_maxRange;
					Vector3D worldMax = odPos + m_maxRange;

					rangeSquared = m_maxRange * m_maxRange;

					MyVoxelCoordSystems.WorldPositionToVoxelCoord(m_voxel.PositionLeftBottomCorner, ref odPos, out odPosVoxelStorage);
					MyVoxelCoordSystems.WorldPositionToVoxelCoord(m_voxel.PositionLeftBottomCorner, ref worldMin, out m_localMin);
					MyVoxelCoordSystems.WorldPositionToVoxelCoord(m_voxel.PositionLeftBottomCorner, ref worldMax, out m_localMax);

					MyVoxelBase vox = m_voxel as MyVoxelBase;
					m_localMin = Vector3I.Clamp(m_localMin, vox.StorageMin, vox.StorageMax);
					m_localMax = Vector3I.Clamp(m_localMax, vox.StorageMin, vox.StorageMax);
					m_localMin >>= QUERY_LOD;
					m_localMax >>= QUERY_LOD;
					odPosVoxelStorage >>= QUERY_LOD;
					m_logger.debugLog("minLocal: " + m_localMin + ", maxLocal: " + m_localMax + ", odPosVoxelStorage: " + odPosVoxelStorage, "StartRead()");

					m_logger.debugLog("Queueing read", "StartRead()", Logger.severity.DEBUG);

					m_thread.EnqueueAction(PerformRead);
				}
				catch (Exception ex)
				{
					m_logger.alwaysLog("Exception: " + ex, "StartRead()", Logger.severity.ERROR);
					m_throwOutVoxelData = DateTime.MinValue;
					throw ex;
				}
				return true;
			}

			private void PerformRead()
			{
				try
				{
					if (m_voxel == null || m_voxel.Storage == null)
						return;

					Vector3I size = m_localMax - m_localMin;
					m_logger.debugLog("number of coords in box: " + (size.X + 1) * (size.Y + 1) * (size.Z + 1), "PerformRead()");
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
														//	+ ", name: " + MyDefinitionManager.Static.GetVoxelMaterialDefinition(mat).MinedOre, "PerformRead()");
														m_materialLocations[vector + index] = mat;
														processed++;
														goto Finished_Deposit;
													}
												}
											}

Finished_Deposit:
									processed++;
								}

					m_logger.debugLog("read " + processed + " chunks" + ", number of mats: " + m_materialLocations.Count, "PerformRead()", Logger.severity.DEBUG);
				}
				finally
				{
					lock_readVoxels.ReleaseExclusive();
					onFinished.Invoke();
				}
			}

		}

		#region Static

		private static readonly TimeSpan UpdateStationary = new TimeSpan(0, 10, 0);

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
		}

		public static bool TryGetMaterial(string oreName, out byte[] oreTypes)
		{ return MaterialGroup.TryGetValue(oreName.Trim().ToLower(), out oreTypes); }

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

		private readonly List<MyVoxelBase> m_nearbyVoxel = new List<MyVoxelBase>();
		/// <summary>Dequeues and invokes all actions finished updating voxels.</summary>
		public readonly LockedQueue<Action> OnUpdateComplete = new LockedQueue<Action>();

		private readonly Logger m_logger;
		private readonly Ingame.IMyOreDetector m_oreDetector;
		private readonly float m_maxRange;

		private readonly Dictionary<IMyVoxelBase, VoxelData> m_voxelData = new Dictionary<IMyVoxelBase, VoxelData>();
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

			BoundingSphereD detection = new BoundingSphereD(m_oreDetector.GetPosition(), m_maxRange);
			m_nearbyVoxel.Clear();
			MainLock.UsingShared(() => MyGamePruningStructure.GetAllVoxelMapsInSphere(ref detection, m_nearbyVoxel));

			if (m_nearbyVoxel.Count == 0)
			{
				using (l_waitingOn.AcquireExclusiveUsing())
					m_waitingOn++;
				OnVoxelFinish();
				return;
			}

			foreach (IMyVoxelBase nearbyMap in m_nearbyVoxel)
			{
				VoxelData data;
				using (l_voxelDate.AcquireExclusiveUsing())
					if (!m_voxelData.TryGetValue(nearbyMap, out data))
					{
						data = new VoxelData(m_oreDetector, nearbyMap, m_maxRange);
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
		public bool FindClosestOre(Vector3D position, byte[] oreType, out Vector3D orePosition, out IMyVoxelBase voxel, out string oreName)
		{
			IOrderedEnumerable<IMyVoxelBase> sortedByDistance;
			using (l_voxelDate.AcquireExclusiveUsing())
				sortedByDistance = m_voxelData.Keys.OrderBy(map => Vector3.DistanceSquared(position, map.GetPosition()));

			foreach (IMyVoxelBase map in sortedByDistance)
			{
				VoxelData data;
				using (l_voxelDate.AcquireSharedUsing())
					data = m_voxelData[map];

				m_logger.debugLog("PositionLeftBottomCorner: " + map.PositionLeftBottomCorner + ", pos: " + position, "FindClosestOre()");
				byte foundOreType;
				if (data.GetClosest(oreType, ref position, out orePosition, out foundOreType))
				{
					oreName = MyDefinitionManager.Static.GetVoxelMaterialDefinition(foundOreType).MinedOre;
					m_logger.debugLog("PositionLeftBottomCorner: " + map.PositionLeftBottomCorner + ", worldPosition: " + orePosition + ", distance: " + Vector3D.Distance(position, orePosition), "FindClosestOre()");
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
