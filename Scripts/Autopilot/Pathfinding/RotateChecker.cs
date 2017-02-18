using System;
using System.Collections.Generic;
using Rynchodon.Attached;
using Sandbox.Game.Entities;
using VRage.Game.Entity;
using VRage.Game.ModAPI;
using VRage.ModAPI;
using VRageMath;

namespace Rynchodon.Autopilot.Pathfinding
{
	public class RotateChecker
	{

		private static Threading.ThreadManager Thread = new Threading.ThreadManager(threadName: "RotateChecker");

		private readonly Logger m_logger;
		private readonly IMyCubeBlock m_block;
		private readonly Func<List<MyEntity>, IEnumerable<MyEntity>> m_collector;
		private readonly List<MyEntity> m_obstructions = new List<MyEntity>();

		private ulong m_nextRunRotate;

		public IMyEntity ObstructingEntity { get; private set; }

		public RotateChecker(IMyCubeBlock block, Func<List<MyEntity>, IEnumerable<MyEntity>> collector)
		{
			this.m_logger = new Logger(block);
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
				m_logger.alwaysLog("Math fail, upperBound does not have a value", Logger.severity.FATAL);
			Ray lower = new Ray(myLocalCentre - myLocalAxis * longestDim * 2f, myLocalAxis);
			float? lowerBound = myGrid.LocalAABB.Intersects(lower);
			if (!lowerBound.HasValue)
				m_logger.alwaysLog("Math fail, lowerBound does not have a value", Logger.severity.FATAL);
			//m_logger.debugLog("LocalAABB: " + myGrid.LocalAABB + ", centre: " + myLocalCentre + ", axis: " + myLocalAxis + ", longest dimension: " + longestDim + ", upper ray: " + upper + ", lower ray: " + lower);
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

			//m_logger.debugLog("height: " + height + ", length: " + length);

			BoundingSphereD surroundingSphere = new BoundingSphereD(centreOfMass, Math.Max(length, height) * MathHelper.Sqrt2);
			m_obstructions.Clear();
			MyGamePruningStructure.GetAllTopMostEntitiesInSphere(ref surroundingSphere, m_obstructions);

			LineSegment axisSegment = new LineSegment();

			foreach (MyEntity entity in m_collector.Invoke(m_obstructions))
			{
				if (entity is IMyVoxelBase)
				{
					MyVoxelBase voxel = (MyVoxelBase)entity;
					Vector3D leftBottom = voxel.PositionLeftBottomCorner;
					BoundingSphereD localSphere;
					Vector3D.Subtract(ref surroundingSphere.Center, ref leftBottom, out localSphere.Center);
					localSphere.Radius = surroundingSphere.Radius;
					voxel.Storage.Geometry.Intersects(ref localSphere);

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
							m_logger.debugLog("axis segment: " + axisSegment.From + " to " + axisSegment.To + ", radius: " + length + ", hit " + grid.nameWithId() + " at " + cell);
							ObstructingEntity = grid;
							return false;
						}

					continue;
				}

				m_logger.debugLog("No tests for object: " + entity.getBestName(), Logger.severity.INFO);
				ObstructingEntity = entity;
				return false;
			}

			ObstructingEntity = null;
			return true;
		}

	}
}
