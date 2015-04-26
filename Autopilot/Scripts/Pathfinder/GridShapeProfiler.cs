#define LOG_ENABLED // remove on build

using System;
using System.Collections.Generic;
//using System.Linq;
//using System.Text;

using Sandbox.ModAPI;

using VRage;
using VRage.Collections;
using VRage.Library.Utils;
using VRageMath;

namespace Rynchodon.Autopilot.Pathfinder
{
	/// <summary>
	/// <para>Creates a List of every occupied cell for a grid. This List is used to create projections of the grid.</para>
	/// <para>This class only ever uses local positions.</para>
	/// </summary>
	internal class GridShapeProfiler
	{
		public IMyCubeGrid myCubeGrid { get; private set; }
		//public float PathBuffer { get { return CubeGrid.GridSize * 2; } }
		//public float PathBufferSquared { get { return PathBuffer * PathBuffer; } }

		private static Dictionary<IMyCubeGrid, GridShapeProfiler> registry = new Dictionary<IMyCubeGrid, GridShapeProfiler>();
		private static FastResourceLock lock_registry = new FastResourceLock();
		/// <summary>
		/// All the local positions of occupied cells in this grid.
		/// </summary>
		private ListSnapshots<IMySlimBlock> SlimBlocks;
		private FastResourceLock lock_SlimBlocks = new FastResourceLock();

		private Vector3 Centre { get { return myCubeGrid.LocalAABB.Center; } }
		private Vector3 CentreRejection;
		private Vector3 Displacement;
		private Vector3? Displacement_PartialCalculation = null;

		private MyUniqueList<Vector3I> rejectionCells;
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
			//foreachVector3I(CubeGrid.Min, CubeGrid.Max, (cell) =>
			myCubeGrid.Min.ForEachVector(myCubeGrid.Max, (cell) =>
			{
				IMySlimBlock slim = myCubeGrid.GetCubeBlock(cell);
				if (slim != null)
					mutable.Add(slim);
				//if (CubeGrid.CubeExists(cell))
				//	mutable.Add(cell);
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
			{
				//IMyCubeBlock fatblock = obj.FatBlock;
				//ReadOnlyList<IMySlimBlock> mutable = 
				SlimBlocks.mutable().Add(slim);
				//if (fatblock == null)
				//	mutable.Add(obj.Position);
				//else
				//	fatblock.Min.ForEachVector(fatblock.Max, (cell) => { mutable.Add(cell); return false; });
			}
		}

		/// <summary>
		/// Remove a block from the profile.
		/// </summary>
		private void Grid_OnBlockRemoved(IMySlimBlock slim)
		{
			using (lock_SlimBlocks.AcquireExclusiveUsing())
			{
				//IMyCubeBlock fatblock = obj.FatBlock;
				//ReadOnlyList<Vector3I> mutable = SlimBlocks.mutable();
				//if (fatblock == null)
				//	mutable.Remove(obj.Position);
				//else
				//	fatblock.Min.ForEachVector(fatblock.Max, (cell) => { mutable.Remove(cell); return false; });
				SlimBlocks.mutable().Add(slim);
			}
		}

		/// <summary>
		/// Invalidate and cleanup the profile.
		/// </summary>
		private void Grid_OnClose(IMyEntity obj)
		{
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

		///// <summary>
		///// Gets a snapshot of current occupied cells
		///// </summary>
		///// <returns>An immutable ReadOnlyList of occupied cells</returns>
		//public ReadOnlyList<Vector3I> get_OccupiedCells()
		//{
		//	using (lock_SlimBlocks.AcquireSharedUsing())
		//		return SlimBlocks.immutable();
		//}

		/// <summary>
		/// Set the destination, calulate the profile.
		/// </summary>
		/// <param name="destination">waypoint or destination to fly to</param>
		/// <param name="navigationBlock">usually remote control</param>
		public void SetDestination(RelativeVector3F destination, IMyCubeBlock navigationBlock)
		{
			//if (displacement == null)
			this.Displacement = destination.getLocal() - navigationBlock.Position * myCubeGrid.GridSize;
			this.Displacement_PartialCalculation = null;
			//else
			//	this.Displacement = (Vector3)displacement;
			Vector3 centreDestination = destination.getLocal() + (Centre - navigationBlock.Position) * myCubeGrid.GridSize;

			rejectAll();
			createCapsule(centreDestination);
		}

		///// <summary>
		///// <para>Perform a vector rejection from direction to destination.</para>
		///// <para>Destination must be set first.</para>
		///// </summary>
		///// <param name="toReject">the grid-local vector to reject</param>
		///// <returns>the rejected vector</returns>
		//public Vector3 rejectVector(Vector3 toReject)
		//{
		//	Vector3 result;
		//	toReject.Rejection(Displacement, ref Displacement_PartialCalculation).ApplyOperation(Math.Round, out result);
		//	myLogger.debugLog("rejected vector " + toReject + " is " + result, "rejectVector()");
		//	return result;
		//}

		public bool testVector(Vector3 toTest)
		{
			Vector3I asCell = myCubeGrid.WorldToGridInteger(toTest);



			// perform vector rejection, convert to cell, and == against rejectionCells
			// or convert to cell, perform rejection, and == against rejectionCells
		}

		#endregion
		#region Private Methods

		///// <summary>
		///// Perform an Action for each Vector3I from min to max (inclusive).
		///// </summary>
		///// <param name="min">starting Vector3I</param>
		///// <param name="max">ending Vector3I</param>
		///// <param name="action">action to invoke</param>
		//private void foreachVector3I(Vector3I min, Vector3I max, Action<Vector3I> action)
		//{
		//	for (int x = min.X; x <= max.X; x++)
		//		for (int y = min.Y; y <= max.Y; y++)
		//			for (int z = min.Z; z <= max.Z; z++)
		//				action.Invoke(new Vector3I(x, y, z));
		//}

		/// <summary>
		/// to ensure consistency, all rejections should be performed by this method
		/// </summary>
		/// <param name="toReject">local position vector</param>
		/// <returns></returns>
		private Vector3I rejectLocalPosition(Vector3I toReject)
		{
			Vector3I rejection;
			toReject.Rejection(Displacement, ref Displacement_PartialCalculation).ApplyOperation(Math.Round, out rejection);
			return rejection;
		}

		/// <summary>
		/// Perform a vector rejection of every occupied cell from the specified direction and store the results in rejectionCells.
		/// </summary>
		/// <remarks>
		/// It is not useful to normalize direction first.
		/// </remarks>
		private void rejectAll()
		{
			VRage.Exceptions.ThrowIf<ArgumentNullException>(Displacement == null, "direction");
			rejectionCells = new MyUniqueList<Vector3I>();

			ReadOnlyList<IMySlimBlock> immutable;
			using (lock_SlimBlocks.AcquireSharedUsing())
				immutable = SlimBlocks.immutable();

			Centre.Rejection(Displacement, ref Displacement_PartialCalculation).ApplyOperation(Math.Round, out CentreRejection);//.Round(CubeGrid.GridSize);
			foreach (IMySlimBlock slim in immutable)
			{
				slim.ForEachCellSurround((cell) =>
				{
					Vector3I rejection;
					(cell * myCubeGrid.GridSize).Rejection(Displacement, ref Displacement_PartialCalculation).ApplyOperation(Math.Round, out rejection);
					rejectionCells.Add(rejection);
					return false;
				});
			}
		}

		/// <param name="centreDestination">where the centre of the grid will end up (local)</param>
		private void createCapsule(Vector3 centreDestination)
		{
			float longestDistanceSquared = 0;
			foreach (Vector3 rejection in rejectionCells)
			{
				float distanceSquared = (rejection - CentreRejection).LengthSquared();
				if (distanceSquared > longestDistanceSquared)
					longestDistanceSquared = distanceSquared;
			}
			//myPath = new PathCapsule(RelativeVector3F.createFromLocal(Centre, CubeGrid), RelativeVector3F.createFromLocal(centreDestination, CubeGrid), longestDistanceSquared, CubeGrid.GridSize);
			RelativeVector3F P0 = RelativeVector3F.createFromLocal(Centre, myCubeGrid);
			RelativeVector3F P1 = RelativeVector3F.createFromLocal(centreDestination, myCubeGrid);
			float CapsuleRadius = (float)(Math.Pow(longestDistanceSquared, 0.5) + myCubeGrid.GridSize);
			myPath = new Capsule(P0.getWorldAbsolute(), P1.getWorldAbsolute(), CapsuleRadius);
		}

		#endregion
	}
}
