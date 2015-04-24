// skip file on build
#define LOG_ENABLED //remove on build

using Sandbox.Definitions;
using Sandbox.ModAPI;
using System;
using System.Collections.Generic;
using VRage.Voxels;
using VRageMath;
using Ingame = Sandbox.ModAPI.Ingame;

namespace Rynchodon.Autopilot.Harvest
{
	public class OreDetector
	{
		public IMyCubeBlock myCubeBlock { get; private set; }
		public Ingame.IMyOreDetector myOreDetector { get; private set; }

		private Logger myLogger;

		public OreDetector(IMyCubeBlock block)
		{
			myCubeBlock = block;
			myOreDetector = block as Ingame.IMyOreDetector;

			myLogger = new Logger("OreDetector", () => myCubeBlock.CubeGrid.DisplayName);
		}

		bool run = true;

		public void Update100()
		{
			try
			{
				if (!run)
					return;
				run = false;

				if (myCubeBlock.IsWorking)
				{
					BoundingSphereD detectInSphere = new BoundingSphereD(myCubeBlock.GetPosition(), 150); // myOreDetector.Range seems to get an incorrect value, it is fine for testing though
					List<IMyEntity> entitiesInSphere = MyAPIGateway.Entities.GetEntitiesInSphere_Safe(ref detectInSphere);

					myLogger.debugLog("Started testing", "Update100()", Logger.severity.INFO);
					foreach (IMyEntity entity in entitiesInSphere)
					{
						IMyVoxelMap asteroid = entity as IMyVoxelMap;
						if (asteroid == null)
							continue;

						MyStorageDataCache cache = null;
						TimeAction.Results results = TimeAction.Time(() => cache = GetOre(asteroid, detectInSphere));
						myLogger.debugLog("Time to GetOre: " + results.Pretty_FiveNumbers(), "Update100()");

						results = TimeAction.Time(() => CountVoxels(cache));
						myLogger.debugLog("Time to CountVoxels: " + results.Pretty_FiveNumbers(), "Update100()");
					}
					MyAPIGateway.Utilities.ShowNotification("Concluded testing", int.MaxValue);
					myLogger.debugLog("Concluded testing", "Update100()", Logger.severity.INFO);
				}
			}
			catch (Exception ex)
			{
				myLogger.log("Exception: " + ex, "Update100()", Logger.severity.ERROR);
				MyAPIGateway.Utilities.ShowNotification("Testing failed", int.MaxValue);
			}
		}

		private const int Asteroid_StepSize = 100;

		private MyStorageDataCache GetOre(IMyVoxelMap asteroid, BoundingSphereD oreInSphere)
		{
			myLogger.debugLog("params: " + asteroid + ", " + oreInSphere, "GetOre()");

			//BoundingBoxD boundingBox = BoundingBoxD.CreateFromSphere(oreInSphere);

			// Get scan_min and scan_max from bounding box of sphere
			BoundingBoxD boundingBox = asteroid.WorldAABB;
			Vector3I scan_min, scan_max; // local
			MyVoxelCoordSystems.WorldPositionToVoxelCoord(asteroid.GetPosition(), ref boundingBox.Min, out scan_min);
			MyVoxelCoordSystems.WorldPositionToVoxelCoord(asteroid.GetPosition(), ref boundingBox.Max, out scan_max);
			
			//Vector3I spherePosition; // local
			//MyVoxelCoordSystems.WorldPositionToVoxelCoord(asteroid.GetPosition(), ref oreInSphere.Center, out spherePosition);
			//Vector3 spherePosFloat = (Vector3)spherePosition;
			//float radiusSquared = (float)(oreInSphere.Radius * oreInSphere.Radius);

			//// Get scan_min and scan_max from asteroid min / max
			//Vector3I scan_min, scan_max; // local
			//Vector3D asteroid_min = asteroid.LocalAABB.Min;
			//Vector3D asteroid_max = asteroid.LocalAABB.Max;
			//MyVoxelCoordSystems.LocalPositionToVoxelCoord(ref asteroid_min, out scan_min);
			//MyVoxelCoordSystems.LocalPositionToVoxelCoord(ref asteroid_max, out scan_max);

			//(boundingBox.Min - asteroid.PositionLeftBottomCorner).ApplyOperation(Math.Floor, out boundsMin);
			//(boundingBox.Max - asteroid.PositionLeftBottomCorner).ApplyOperation(Math.Ceiling, out boundsMax);

			myLogger.debugLog("scan_min = " + scan_min + ", scan_max = " + scan_max, "GetOre()");
			MyStorageDataCache cache = new MyStorageDataCache();
			cache.Resize(scan_min, scan_max);
			asteroid.Storage.ReadRange(cache, MyStorageDataTypeFlags.Material, 0, scan_min, scan_max);
			myLogger.debugLog("finished reading", "GetOre()");

			return cache;
		}

		private void CountVoxels(MyStorageDataCache cache)
		{
			Dictionary<byte, long> materialCounts = new Dictionary<byte, long>();
			//try
			//{
				for (int index = 0; index < cache.SizeLinear; index++)
				{
					//byte content = cache.Content(index);
					//if (content == 0)
					//	continue;
					byte mat = cache.Material(index);
					long sum;
					if (materialCounts.TryGetValue(mat, out sum))
						materialCounts[mat] += 1;
					else
						materialCounts[mat] = 1;
				}
			//}
			//catch (IndexOutOfRangeException) { }

			myLogger.debugLog("finished counting", "GetOre()");

			foreach (var matCou in materialCounts)
			{
				MyVoxelMaterialDefinition voxelMaterial = MyDefinitionManager.Static.GetVoxelMaterialDefinition(matCou.Key);
				if (voxelMaterial == null)
				{
					myLogger.debugLog("could not get material for " + matCou.Key + ", Count  = " + matCou.Value, "GetOre()");
					continue;
				}
				string minedName = voxelMaterial.MinedOre;
				myLogger.debugLog("Material = " + voxelMaterial + ", Mined = " + minedName + ", byte = " + matCou.Key + ", Count  = " + matCou.Value, "GetOre()");
			}
		}
	}
}
