#define LOG_ENABLED // remove on build

using System;
using System.Collections.Generic;
using Sandbox.ModAPI;
using VRage;
using VRage.Collections;
using VRage.ModAPI;
using VRageMath;

namespace Rynchodon.Autopilot.Pathfinder
{
	/// <summary>
	/// <para>Creates a List of every occupied cell for a grid. This List is used to create projections of the grid.</para>
	/// <para>This class only ever uses local positions.</para>
	/// </summary>
	internal class GridShapeProfiler
	{
		public readonly int CubesSpacingAltPath = 10;

		public IMyCubeGrid myCubeGrid { get; private set; }

		private static Dictionary<IMyCubeGrid, GridShapeProfiler> registry = new Dictionary<IMyCubeGrid, GridShapeProfiler>();
		private static FastResourceLock lock_registry = new FastResourceLock();
		/// <summary>
		/// All the local positions of blocks in this grid.
		/// </summary>
		private ListSnapshots<IMySlimBlock> SlimBlocks;
		private FastResourceLock lock_SlimBlocks = new FastResourceLock();

		private Vector3 Centre { get { return myCubeGrid.LocalAABB.Center; } }
		private Vector3 CentreRejection;
		private Vector3 DirectionNorm;

		private MyUniqueList<Vector3> rejectionCells;
		public Capsule myPath { get; private set; }

		private Logger myLogger = new Logger(null, "GridShapeProfiler");

		#region Life Cycle

		/// <summary>
		/// Create a profile for a grid.
		/// </summary>
		/// <param name="grid">grid to profile</param>
		private GridShapeProfiler(IMyCubeGrid grid)
		{
			this.myCubeGrid = grid;
			myLogger = new Logger("GridShapeProfiler", () => myCubeGrid.DisplayName);
			this.SlimBlocks = new ListSnapshots<IMySlimBlock>();

			// instead of iterating over blocks, test cells of grid for contents (no need to lock grid)
			ReadOnlyList<IMySlimBlock> mutable = SlimBlocks.mutable();
			myCubeGrid.Min.ForEachVector(myCubeGrid.Max, (cell) => {
				IMySlimBlock slim = myCubeGrid.GetCubeBlock(cell);
				if (slim != null)
					mutable.Add(slim);
				return false;
			});

			myCubeGrid.OnBlockAdded += Grid_OnBlockAdded;
			myCubeGrid.OnBlockRemoved += Grid_OnBlockRemoved;
			myCubeGrid.OnClose += Grid_OnClose;

			using (lock_registry.AcquireExclusiveUsing())
				registry.Add(this.myCubeGrid, this);
		}

		/// <summary>
		/// Add a block to the profile.
		/// </summary>
		private void Grid_OnBlockAdded(IMySlimBlock slim)
		{
			using (lock_SlimBlocks.AcquireExclusiveUsing())
				SlimBlocks.mutable().Add(slim);
		}

		/// <summary>
		/// Remove a block from the profile.
		/// </summary>
		private void Grid_OnBlockRemoved(IMySlimBlock slim)
		{
			myLogger.debugLog("entered Grid_OnBlockRemoved()", "Grid_OnBlockRemoved()");

			using (lock_SlimBlocks.AcquireExclusiveUsing())
				SlimBlocks.mutable().Remove(slim);

			myLogger.debugLog("leaving Grid_OnBlockRemoved()", "Grid_OnBlockRemoved()");
		}

		/// <summary>
		/// Invalidate and cleanup the profile.
		/// </summary>
		private void Grid_OnClose(IMyEntity obj)
		{
			myLogger.debugLog("entered Grid_OnClose()", "Grid_OnClose()");

			// remove references to this
			using (lock_registry.AcquireExclusiveUsing())
				registry.Remove(myCubeGrid);

			myCubeGrid.OnBlockAdded -= Grid_OnBlockAdded;
			myCubeGrid.OnBlockRemoved -= Grid_OnBlockRemoved;
			myCubeGrid.OnClose -= Grid_OnClose;

			// invalidate
			using (lock_SlimBlocks.AcquireExclusiveUsing())
				SlimBlocks = null;
			myCubeGrid = null;

			myLogger.debugLog("leaving Grid_OnClose()", "Grid_OnClose()");
		}

		#endregion
		#region Public Methods

		/// <summary>
		/// Get or build the GridShapeProfiler for a given grid.
		/// </summary>
		/// <param name="grid">grid the profile will be for</param>
		public static GridShapeProfiler getFor(IMyCubeGrid grid)
		{
			GridShapeProfiler result;
			using (lock_registry.AcquireSharedUsing())
				if (registry.TryGetValue(grid, out result))
					return result;

			return new GridShapeProfiler(grid);
		}

		/// <summary>
		/// Set the destination, calulate the profile.
		/// </summary>
		/// <param name="destination">waypoint or destination to fly to</param>
		/// <param name="navigationBlock">usually remote control</param>
		public void SetDestination(RelativeVector3F destination, IMyCubeBlock navigationBlock)
		{
			DirectionNorm = Vector3.Normalize(destination.getLocal() - navigationBlock.Position * myCubeGrid.GridSize);
			Vector3 centreDestination = destination.getLocal() + Centre - navigationBlock.Position * myCubeGrid.GridSize;
			//myLogger.debugLog("destination.getLocal() = " + destination.getLocal() + ", Centre = " + Centre + ", navigationBlock.Position * myCubeGrid.GridSize = " + navigationBlock.Position * myCubeGrid.GridSize, "SetDestination()");
			//myLogger.debugLog("centreDestination = " + centreDestination + ", world = " + RelativeVector3F.createFromLocal(centreDestination, myCubeGrid).getWorldAbsolute(), "SetDestination()");
			rejectAll();
			createCapsule(centreDestination, navigationBlock);
		}

		/// <summary>
		/// Rejection test for intersection with the profiled grid.
		/// </summary>
		/// <param name="position">position of potential obstruction</param>
		/// <returns>true if the rejection collides with one or more of the grid's rejections</returns>
		public bool rejectionIntersects(RelativeVector3F position, float GridSize)
		{ return rejectionIntersects(position.getLocal(), GridSize); }

		/// <summary>
		/// Rejection test for intersection with the profiled grid.
		/// </summary>
		/// <param name="localMetresPosition">The local position in metres.</param>
		/// <returns>true if the rejection collides with one or more of the grid's rejections</returns>
		public bool rejectionIntersects(Vector3 localMetresPosition, float GridSize)
		{
			Vector3 TestRejection = RejectMetres(localMetresPosition);
			foreach (Vector3 ProfileRejection in rejectionCells)
				if (Vector3.DistanceSquared(TestRejection, ProfileRejection) < 2 * GridSize + 2 * myCubeGrid.GridSize)
					return true;
			return false;
		}

		#endregion
		#region Private Methods

		private Vector3 RejectMetres(Vector3 metresPosition)
		{ return Vector3.Reject(metresPosition, DirectionNorm); } // RejectCell(myCubeGrid.WorldToGridInteger(metresPosition)); }

		/// <summary>
		/// Perform a vector rejection of every occupied cell from DirectionNorm and store the results in rejectionCells.
		/// </summary>
		private void rejectAll()
		{
			VRage.Exceptions.ThrowIf<ArgumentNullException>(DirectionNorm == null, "DirectionNorm");
			rejectionCells = new MyUniqueList<Vector3>();

			ReadOnlyList<IMySlimBlock> immutable;
			using (lock_SlimBlocks.AcquireSharedUsing())
				immutable = SlimBlocks.immutable();

			CentreRejection = RejectMetres(Centre);
			foreach (IMySlimBlock slim in immutable)
			{
				slim.ForEachCell((cell) => {
					Vector3 rejection = RejectMetres(cell * myCubeGrid.GridSize);
					rejectionCells.Add(rejection);
					return false;
				});
			}
		}

		/// <param name="centreDestination">where the centre of the grid will end up (local)</param>
		private void createCapsule(Vector3 centreDestination, IMyCubeBlock navigationBlock)
		{
			float longestDistanceSquared = 0;
			foreach (Vector3 rejection in rejectionCells)
			{
				float distanceSquared = (rejection - CentreRejection).LengthSquared();
				if (distanceSquared > longestDistanceSquared)
					longestDistanceSquared = distanceSquared;
			}
			Vector3D P0 = RelativeVector3F.createFromLocal(Centre, myCubeGrid).getWorldAbsolute();
			//Vector3D P1 = RelativeVector3F.createFromLocal(centreDestination, myCubeGrid).getWorldAbsolute();

			// need to extend capsule past destination by distance between remote and front of ship
			Vector3 localPosition = navigationBlock.LocalPosition();
			Ray navTowardsDest = new Ray(localPosition, DirectionNorm);
			float tMin, tMax;
			myCubeGrid.LocalVolume.IntersectRaySphere(navTowardsDest, out tMin, out tMax);
			Vector3D P1 = RelativeVector3F.createFromLocal(centreDestination + tMax * DirectionNorm, myCubeGrid).getWorldAbsolute();

			float CapsuleRadius = (float)(Math.Pow(longestDistanceSquared, 0.5) + 3 * myCubeGrid.GridSize);
			myPath = new Capsule(P0, P1, CapsuleRadius);
		}

		#endregion
	}
}