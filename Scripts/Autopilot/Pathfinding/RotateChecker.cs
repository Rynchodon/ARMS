using System;
using System.Collections.Generic;
using Rynchodon.Attached;
using Rynchodon.Utility;
using Sandbox.Game.Entities;
using Sandbox.ModAPI;
using VRage.Game.Entity;
using VRage.Game.ModAPI;
using VRage.ModAPI;
using VRageMath;

namespace Rynchodon.Autopilot.Pathfinding
{
	public class RotateChecker
	{

		private static Threading.ThreadManager Thread = new Threading.ThreadManager(threadName: "RotateChecker");

		private readonly IMyCubeBlock m_block;
		private readonly Func<List<MyEntity>, IEnumerable<MyEntity>> m_collector;
		private readonly List<MyEntity> m_obstructions = new List<MyEntity>();

		private ulong m_nextRunRotate;
		private MyPlanet m_closestPlanet;
		private bool m_planetObstruction;

		private Logable Log { get { return new Logable(m_block); } }
		public IMyEntity ObstructingEntity { get; private set; }

		public RotateChecker(IMyCubeBlock block, Func<List<MyEntity>, IEnumerable<MyEntity>> collector)
		{
			this.m_block = block;
			this.m_collector = collector;
		}

		public void TestRotate(Vector3 displacement)
		{
			if (Globals.UpdateCount < m_nextRunRotate)
				return;
			m_nextRunRotate = Globals.UpdateCount + 10ul;

			displacement.Normalize();
			Thread.EnqueueAction(() => in_TestRotate(displacement));
		}

		/// <summary>
		/// Test if it is safe for the grid to rotate.
		/// </summary>
		/// <param name="axis">Normalized axis of rotation in world space.</param>
		/// <returns>True iff the path is clear.</returns>
		private bool in_TestRotate(Vector3 axis)
		{
			IMyCubeGrid myGrid = m_block.CubeGrid;
			Vector3 centreOfMass = myGrid.Physics.CenterOfMassWorld;
			float longestDim = myGrid.GetLongestDim();

			// calculate height
			Matrix toMyLocal = myGrid.WorldMatrixNormalizedInv;
			Vector3 myLocalCoM = Vector3.Transform(centreOfMass, toMyLocal);
			Vector3 myLocalAxis = Vector3.Transform(axis, toMyLocal.GetOrientation());
			Vector3 myLocalCentre = myGrid.LocalAABB.Center; // CoM may not be on ship (it now considers mass from attached grids)
			Ray upper = new Ray(myLocalCentre + myLocalAxis * longestDim * 2f, -myLocalAxis);
			float? upperBound = myGrid.LocalAABB.Intersects(upper);
			if (!upperBound.HasValue)
				Log.AlwaysLog("Math fail, upperBound does not have a value", Logger.severity.FATAL);
			Ray lower = new Ray(myLocalCentre - myLocalAxis * longestDim * 2f, myLocalAxis);
			float? lowerBound = myGrid.LocalAABB.Intersects(lower);
			if (!lowerBound.HasValue)
				Log.AlwaysLog("Math fail, lowerBound does not have a value", Logger.severity.FATAL);
			//Log.DebugLog("LocalAABB: " + myGrid.LocalAABB + ", centre: " + myLocalCentre + ", axis: " + myLocalAxis + ", longest dimension: " + longestDim + ", upper ray: " + upper + ", lower ray: " + lower);
			float height = longestDim * 4f - upperBound.Value - lowerBound.Value;

			float furthest = 0f;
			foreach (IMyCubeGrid grid in AttachedGrid.AttachedGrids(myGrid, Attached.AttachedGrid.AttachmentKind.Physics, true))
			{
				CubeGridCache cache = CubeGridCache.GetFor(grid);
				if (cache == null)
					return false;
				foreach (Vector3I cell in cache.OccupiedCells())
				{
					Vector3 rejection = Vector3.Reject(cell * myGrid.GridSize, myLocalAxis);
					float cellDistSquared = Vector3.DistanceSquared(myLocalCoM, rejection);
					if (cellDistSquared > furthest)
						furthest = cellDistSquared;
				}
			}
			float length = (float)Math.Sqrt(furthest) + myGrid.GridSize;

			//Log.DebugLog("height: " + height + ", length: " + length);

			BoundingSphereD surroundingSphere = new BoundingSphereD(centreOfMass, Math.Max(length, height) * MathHelper.Sqrt2);
			m_obstructions.Clear();
			MyGamePruningStructure.GetAllTopMostEntitiesInSphere(ref surroundingSphere, m_obstructions);

			LineSegment axisSegment = new LineSegment();

			m_closestPlanet = null;

			foreach (MyEntity entity in m_collector.Invoke(m_obstructions))
			{
				if (entity is IMyVoxelBase)
				{
					IMyVoxelMap voxel = entity as IMyVoxelMap;
					if (voxel != null)
					{
						if (voxel.GetIntersectionWithSphere(ref surroundingSphere))
						{
							Log.DebugLog("Too close to " + voxel.getBestName() + ", CoM: " + centreOfMass.ToGpsTag("Centre of Mass") + ", required distance: " + surroundingSphere.Radius);
							ObstructingEntity = voxel;
							return false;
						}
						continue;
					}

					if (m_closestPlanet == null)
					{
						MyPlanet planet = entity as MyPlanet;
						if (planet == null)
							continue;

						double distToPlanetSq = Vector3D.DistanceSquared(centreOfMass, planet.PositionComp.GetPosition());
						if (distToPlanetSq < planet.MaximumRadius * planet.MaximumRadius)
						{
							m_closestPlanet = planet;

							if (m_planetObstruction)
							{
								Log.DebugLog("planet blocking");
								ObstructingEntity = m_closestPlanet;
								return false;
							}
						}
					}
					continue;
				}

				IMyCubeGrid grid = entity as IMyCubeGrid;
				if (grid != null)
				{
					Matrix toLocal = grid.WorldMatrixNormalizedInv;
					Vector3 localAxis = Vector3.Transform(axis, toLocal.GetOrientation());
					Vector3 localCentre = Vector3.Transform(centreOfMass, toLocal);
					axisSegment.From = localCentre - localAxis * height;
					axisSegment.To = localCentre + localAxis * height;

					CubeGridCache cache = CubeGridCache.GetFor(grid);
					if (cache == null)
						return false;
					foreach (Vector3I cell in cache.OccupiedCells())
						if (axisSegment.PointInCylinder(length, cell * grid.GridSize))
						{
							Log.DebugLog("axis segment: " + axisSegment.From + " to " + axisSegment.To + ", radius: " + length + ", hit " + grid.nameWithId() + " at " + cell);
							ObstructingEntity = grid;
							return false;
						}

					continue;
				}

				Log.DebugLog("No tests for object: " + entity.getBestName(), Logger.severity.INFO);
				ObstructingEntity = entity;
				return false;
			}

			MyAPIGateway.Utilities.TryInvokeOnGameThread(TestPlanet);
			ObstructingEntity = null;
			return true;
		}

		private void TestPlanet()
		{
			MyPlanet planet = m_closestPlanet;
			if (planet == null)
				return;

			IMyCubeGrid grid = m_block.CubeGrid;

			Vector3D myPos = grid.GetCentre();
			Vector3D planetCentre = planet.GetCentre();

			double distSqToPlanet = Vector3D.DistanceSquared(myPos, planetCentre);
			if (distSqToPlanet > planet.MaximumRadius * planet.MaximumRadius)
			{
				Log.DebugLog("higher than planet maximum");
				m_planetObstruction = false;
				return;
			}

			Vector3D closestPoint = planet.GetClosestSurfacePointGlobal(ref myPos);

			if (distSqToPlanet < Vector3D.DistanceSquared(closestPoint, planetCentre))
			{
				Log.DebugLog("below surface");
				return;
			}

			float longest = grid.GetLongestDim();
			if (Vector3D.DistanceSquared(myPos, closestPoint) < longest * longest)
			{
				Log.DebugLog("near surface");
				m_planetObstruction = true;
				return;
			}

			Log.DebugLog("clear");
			m_planetObstruction = false;
			return;
		}

	}
}
