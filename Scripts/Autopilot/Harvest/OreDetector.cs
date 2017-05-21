using System;
using System.Collections.Generic;
using System.Linq;
using Rynchodon.AntennaRelay;
using Rynchodon.Threading;
using Rynchodon.Utility;
using Sandbox.Definitions;
using Sandbox.Game.Entities;
using Sandbox.ModAPI;
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

			private class StaticVariables
			{
				public readonly TimeSpan LifeSpan_VoxelData = new TimeSpan(1, 0, 0);
				public readonly bool[] RareMaterials;

				public StaticVariables()
				{
					Logger.DebugLog("entered", Logger.severity.TRACE);
					RareMaterials = new bool[MyDefinitionManager.Static.VoxelMaterialCount];
					for (byte materialIndex = 0; materialIndex < MyDefinitionManager.Static.VoxelMaterialCount; materialIndex++)
						RareMaterials[materialIndex] = MyDefinitionManager.Static.GetVoxelMaterialDefinition(materialIndex).IsRare;
				}
			}

			private static StaticVariables value_static;
			private static StaticVariables Static
			{
				get
				{
					if (Globals.WorldClosed)
						throw new Exception("World closed");
					if (value_static == null)
						value_static = new StaticVariables();
					return value_static;
				}
				set { value_static = value; }
			}

			[OnWorldClose]
			private static void Unload()
			{
				Static = null;
			}

			private readonly IMyOreDetector m_oreDetector;
			private readonly IMyVoxelBase m_voxel;
			private readonly float m_maxRange;

			private readonly Dictionary<byte, List<Vector3I>> m_materialLocations2 = new Dictionary<byte, List<Vector3I>>();
			private readonly MyStorageData m_storage = new MyStorageData();

			private TimeSpan m_throwOutVoxelData = Globals.ElapsedTime + Static.LifeSpan_VoxelData;

			private bool m_forceUpdate;
			private Vector3D m_position;
			public bool NeedsUpdate
			{
				get
				{
					if (m_forceUpdate)
						return true;
					Vector3D currentPosition = m_oreDetector.GetPosition();
					double distSq; Vector3D.DistanceSquared(ref m_position, ref currentPosition, out distSq);
					return distSq > 100f;
				}
				set
				{
					m_forceUpdate = value;
					if (!value)
						m_position = m_oreDetector.GetPosition();
				}
			}

			private Logable Log
			{
				get { return new Logable(m_oreDetector, m_voxel?.ToString()); }
			}

			private void AddMaterialLocation(byte material, ref Vector3I location)
			{
				List<Vector3I> list;
				if (!m_materialLocations2.TryGetValue(material, out list))
				{
					list = new List<Vector3I>();
					m_materialLocations2.Add(material, list);
				}
				list.Add(location);
			}

			public VoxelData(IMyOreDetector oreDetector, IMyVoxelBase voxel, float maxRange)
			{
				this.m_oreDetector = oreDetector;
				this.m_voxel = voxel;
				this.m_storage.Resize(new Vector3I(QUERY_STEP, QUERY_STEP, QUERY_STEP));
				this.m_maxRange = maxRange;
				(this.m_voxel as MyVoxelBase).RangeChanged += m_voxel_RangeChanged;
				this.NeedsUpdate = true;

				Log.DebugLog("Created for voxel at " + voxel.PositionLeftBottomCorner);
			}

			private void m_voxel_RangeChanged(MyVoxelBase storage, Vector3I minVoxelChanged, Vector3I maxVoxelChanged, MyStorageDataTypeFlags changedData)
			{
				NeedsUpdate = true;
			}

			/// <returns>False iff VoxelData is very old and should be disposed of.</returns>
			public bool IsValid()
			{
				return Globals.ElapsedTime < m_throwOutVoxelData;
			}

			public bool GetRandom(byte[] oreType, ref Vector3D worldPosition, out byte foundOre, out IEnumerable<Vector3D> positions)
			{
				Vector3I search_voxelCellCoord;
				MyVoxelCoordSystems.WorldPositionToVoxelCoord(m_voxel.PositionLeftBottomCorner, ref worldPosition, out search_voxelCellCoord);
				search_voxelCellCoord >>= QUERY_LOD;

				if (oreType == null)
				{
					foreach (KeyValuePair<byte, List<Vector3I>> locations in m_materialLocations2.OrderBy(obj => Globals.Random.NextDouble()))
						if (locations.Value.Count != 0)
						{
							foundOre = locations.Key;
							positions = WorldPositions(locations.Value);
							return true;
						}
				}
				else
				{
					foreach (byte ore in oreType.OrderBy(obj => Globals.Random.NextDouble()))
					{
						List<Vector3I> locations;
						if (m_materialLocations2.TryGetValue(ore, out locations) && locations.Count != 0)
						{
							foundOre = ore;
							positions = WorldPositions(locations);
							return true;
						}
					}
				}

				foundOre = 255;
				positions = null;
				return false;
			}

			private IEnumerable<Vector3D> WorldPositions(List<Vector3I> localPositions)
			{
				foreach (Vector3I intPos in localPositions.OrderBy(obj => Globals.Random.NextDouble()))
				{
					Vector3D localPosition = intPos << QUERY_LOD;
					Vector3D worldPosition;
					MyVoxelCoordSystems.LocalPositionToWorldPosition(m_voxel.PositionLeftBottomCorner, ref localPosition, out worldPosition);
					yield return worldPosition;
				}
			}

			/// <summary>
			/// Start the reads if it is not already running.
			/// </summary>
			/// <param name="onFinished">Invoked when reads finish, not invoked if already running.</param>
			/// <returns>True if started, false if already running.</returns>
			public void Read()
			{
				Profiler.StartProfileBlock();
				m_throwOutVoxelData = Globals.ElapsedTime + Static.LifeSpan_VoxelData;

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
				{
					Profiler.EndProfileBlock();
					return;
				}

				m_localMin = Vector3I.Clamp(m_localMin, vox.StorageMin, vox.StorageMax);
				m_localMax = Vector3I.Clamp(m_localMax, vox.StorageMin, vox.StorageMax);
				m_localMin >>= QUERY_LOD;
				m_localMax >>= QUERY_LOD;
				odPosVoxelStorage >>= QUERY_LOD;
				Log.DebugLog("minLocal: " + m_localMin + ", maxLocal: " + m_localMax + ", odPosVoxelStorage: " + odPosVoxelStorage);

				Vector3I size = m_localMax - m_localMin;
				Log.DebugLog("number of coords in box: " + (size.X + 1) * (size.Y + 1) * (size.Z + 1));
				//ulong processed = 0;
				foreach (List<Vector3I> locations in m_materialLocations2.Values)
					locations.Clear();

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
												if (Static.RareMaterials[mat])
												{
													//Log.DebugLog("mat: " + mat + ", content: " + m_storage.Content(linear) + ", vector: " + vector + ", position: " + vector + index
													//	+ ", name: " + MyDefinitionManager.Static.GetVoxelMaterialDefinition(mat).MinedOre, "Read()");
													//m_materialLocations[vector + index] = mat;

													List<Vector3I> locations;
													if (!m_materialLocations2.TryGetValue(mat, out locations))
													{
														locations = new List<Vector3I>(1000);
														m_materialLocations2.Add(mat, locations);
													}
													locations.Add(vector + index);

													//processed++;
													goto Finished_Deposit;
												}
											}
										}

								Finished_Deposit:;
								//processed++;
							}

				//Log.DebugLog("read " + processed + ", chunks" + ", number of mats: " + m_materialLocations.Count, Logger.severity.DEBUG);
				Profiler.EndProfileBlock();
			}

		}

		#region Static

		public delegate void OreSearchComplete(bool success, IMyVoxelBase voxel, string oreName, IEnumerable<Vector3D> orePositions);

		private class StaticVariables
		{
			public ThreadManager m_thread = new ThreadManager(2, true, "OreDetector");
			/// <summary>
			/// Material indecies by chemical symbol, subtype, and MinedOre.
			/// </summary>
			public Dictionary<string, byte[]> MaterialGroup;

			public StaticVariables()
			{
				Logger.DebugLog("entered", Logger.severity.TRACE);
				var defs = MyDefinitionManager.Static.GetVoxelMaterialDefinitions();
				Dictionary<string, List<byte>> matGroup = new Dictionary<string, List<byte>>();
				foreach (var def in defs)
				{
					string subtype = def.Id.SubtypeName.Split('_')[0].Trim().ToLower();
					string minedOre = def.MinedOre.Trim().ToLower();

					List<byte> addTo;
					if (!matGroup.TryGetValue(subtype, out addTo))
					{
						addTo = new List<byte>();
						matGroup.Add(subtype, addTo);
					}
					if (!addTo.Contains(def.Index))
						addTo.Add(def.Index);

					if (!matGroup.TryGetValue(minedOre, out addTo))
					{
						addTo = new List<byte>();
						matGroup.Add(minedOre, addTo);
					}
					if (!addTo.Contains(def.Index))
						addTo.Add(def.Index);

					string symbol;
					if (GetChemicalSymbol(subtype, out symbol))
					{
						if (!matGroup.TryGetValue(symbol, out addTo))
						{
							addTo = new List<byte>();
							matGroup.Add(symbol, addTo);
						}
						if (!addTo.Contains(def.Index))
							addTo.Add(def.Index);
					}
				}

				this.MaterialGroup = new Dictionary<string, byte[]>();
				foreach (var pair in matGroup)
					this.MaterialGroup.Add(pair.Key, pair.Value.ToArray());
			}
		}

		private static StaticVariables value_static;
		private static StaticVariables Static
		{
			get
			{
				if (Globals.WorldClosed)
					throw new Exception("World closed");
				if (value_static == null)
					value_static = new StaticVariables();
				return value_static;
			}
			set { value_static = value; }
		}

		[OnWorldClose]
		private static void Unload()
		{
			Static = null;
		}

		public static bool TryGetMaterial(string oreName, out byte[] oreTypes)
		{ return Static.MaterialGroup.TryGetValue(oreName.Trim().ToLower(), out oreTypes); }

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

			Static.m_thread.EnqueueAction(() => {
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

				onComplete(false, null, null, null);
			});
		}

		#endregion

		public readonly IMyCubeBlock Block;

		private readonly List<MyVoxelBase> m_nearbyVoxel = new List<MyVoxelBase>();

		private readonly IMyOreDetector m_oreDetector;
		private readonly float m_maxRange;
		private readonly RelayClient m_netClient;

		private readonly Dictionary<long, VoxelData> m_voxelData = new Dictionary<long, VoxelData>();
		private readonly FastResourceLock l_voxelData = new FastResourceLock();

		private readonly FastResourceLock l_getOreLocations = new FastResourceLock();

		private Logable Log { get { return new Logable(m_oreDetector); } }

		/// <summary>
		/// Create an OreDetector for the given block.
		/// </summary>
		/// <param name="oreDetector">The ore detector block.</param>
		public OreDetector(IMyCubeBlock oreDetector)
		{
			this.Block = oreDetector;
			this.m_oreDetector = oreDetector as IMyOreDetector;
			this.m_netClient = new RelayClient(oreDetector);

			float maxrange = ((MyOreDetectorDefinition)((MyCubeBlock)m_oreDetector).BlockDefinition).MaximumRange;
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
						Log.DebugLog("removing old: " + voxelData.Key);
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
			Log.DebugLog("entered GetOreLocations()");

			using (l_getOreLocations.AcquireExclusiveUsing())
			{
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
						{
							if (!m_voxelData.TryGetValue(nearbyMap.EntityId, out data))
							{
								data = new VoxelData(m_oreDetector, nearbyMap, m_maxRange);
								m_voxelData.Add(nearbyMap.EntityId, data);
							}
						}
						if (data.NeedsUpdate)
						{
							Log.DebugLog("Data needs to be updated for " + nearbyMap.getBestName());
							data.Read();
						}
						else
							Log.DebugLog("Old data OK for " + nearbyMap.getBestName());

						IEnumerable<Vector3D> positions;
						byte foundOre;
						if (data.GetRandom(oreType, ref position, out foundOre, out positions))
						{
							//Log.DebugLog("PositionLeftBottomCorner: " + nearbyMap.PositionLeftBottomCorner + ", worldPosition: " + closest + ", distance: " + Vector3D.Distance(position, closest));
							string oreName = MyDefinitionManager.Static.GetVoxelMaterialDefinition(foundOre).MinedOre;
							onComplete(true, nearbyMap, oreName, positions);
							m_nearbyVoxel.Clear();
							return true;
						}
						else
							Log.DebugLog("No ore found");
					}
				}

				m_nearbyVoxel.Clear();
				return false;
			}
		}

	}
}
