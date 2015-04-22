#define LOG_ENABLED //remove on build

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
					BoundingSphereD detectInSphere = new BoundingSphereD(myCubeBlock.GetPosition(), myOreDetector.Range * 1.5); // myOreDetector.Range seems to get an incorrect value, it is fine for testing though
					List<IMyEntity> entitiesInSphere = MyAPIGateway.Entities.GetEntitiesInSphere_Safe(ref detectInSphere);

					foreach (IMyEntity entity in entitiesInSphere)
					{
						IMyVoxelMap asteroid = entity as IMyVoxelMap;
						if (asteroid == null)
							continue;

						var results = TimeAction.Time(() => GetOre(asteroid, detectInSphere));
						myLogger.debugLog("timing results: " + results.Total.ToPrettySeconds(), "Update100()");
					}
				}
			}
			catch (Exception ex)
			{ myLogger.log("Exception: " + ex, "Update100()", Logger.severity.ERROR); }
		}

		private void GetOre(IMyVoxelMap asteroid, BoundingSphereD oreInSphere)
		{
			myLogger.debugLog("params: " + asteroid + ", " + oreInSphere, "GetOre()");

			BoundingBoxD boundingBox = BoundingBoxD.CreateFromSphere(oreInSphere);
			Vector3I boundsMin, boundsMax; // local
			Vector3I spherePosition; // local
			MyVoxelCoordSystems.WorldPositionToVoxelCoord(asteroid.GetPosition(), ref boundingBox.Min, out boundsMin);
			MyVoxelCoordSystems.WorldPositionToVoxelCoord(asteroid.GetPosition(), ref boundingBox.Max, out boundsMax);
			MyVoxelCoordSystems.WorldPositionToVoxelCoord(asteroid.GetPosition(), ref oreInSphere.Center, out spherePosition);
			Vector3 spherePosFloat = (Vector3)spherePosition;
			float radiusSquared = (float)(oreInSphere.Radius * oreInSphere.Radius);

			//(boundingBox.Min - asteroid.PositionLeftBottomCorner).ApplyOperation(Math.Floor, out boundsMin);
			//(boundingBox.Max - asteroid.PositionLeftBottomCorner).ApplyOperation(Math.Ceiling, out boundsMax);

			var results = TimeAction.Time(() =>
			{
				MyStorageDataCache cache = new MyStorageDataCache();
				cache.Resize(boundsMin, boundsMax);
				asteroid.Storage.ReadRange(cache, MyStorageDataTypeFlags.All, 10, boundsMin, boundsMax);
			}, 100, true);
			myLogger.debugLog("timed ReadRange: " + results.Pretty_FiveNumbers(), "GetOre()");

			var iterateVoxels = TimeAction.Time(() =>
			{
				ulong count = 0;
				Vector3I.Zero.ForEach(boundsMax - boundsMin, (Vector3I current) =>
				{
					//if (Vector3.DistanceSquared(spherePosFloat, current) > radiusSquared)
					//	return;
					count++;
					//myLogger.debugLog("At " + current + ", Material = " + cache.Material(ref current) + ", Content = " + cache.Content(ref current), "GetOre()");
				});
				myLogger.debugLog("Voxel Min is " + boundsMin + ", Voxel Max is " + boundsMax + ", Voxel Count is " + count, "GetOre()");
			}, 1, true);
			myLogger.debugLog("Time to iterate over voxels: " + iterateVoxels.Pretty_FiveNumbers(), "GetOre()");
		}
	}
}
