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
		/// All the local positions of blocks in this grid.
		/// </summary>
		private ListSnapshots<IMySlimBlock> SlimBlocks;
		private FastResourceLock lock_SlimBlocks = new FastResourceLock();
		///// <summary>
		///// All the cells that are occupied and neighbouring cells.
		///// </summary>
		//private MyUniqueList<Vector3I> CellsSurround;

		private Vector3 Centre { get { return myCubeGrid.LocalAABB.Center; } }
		private Vector3 CentreRejection;
		private Vector3 DirectionNorm;
		//private Vector3? Displacement_PartialCalculation = null;

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
			//var results = TimeAction.Time(() =>
			//{
			//Vector3 newDirectionNorm = Vector3.Normalize(destination.getLocal() - navigationBlock.Position * myCubeGrid.GridSize);
			//if (DirectionNorm.IsValid() && Vector3.RectangularDistance(ref this.DirectionNorm, ref newDirectionNorm) < 0.1)
			//{
			//	myLogger.debugLog("newDirectionNorm(" + newDirectionNorm + ") is not appreciably different from DirectionNorm (" + DirectionNorm + ")", "SetDestination()");
			//	return;
			//}
			//DirectionNorm = newDirectionNorm;
			DirectionNorm = Vector3.Normalize(destination.getLocal() - navigationBlock.Position * myCubeGrid.GridSize);
			//}, 10, false);
			//myLogger.debugLog("Time to DirectionNorm: " + results.Pretty_FiveNumbers(), "SetDestination()");
			//this.Displacement_PartialCalculation = null;
			//else
			//	this.Displacement = (Vector3)displacement;
			//Vector3 centreDestination = new Vector3();
			//results = TimeAction.Time(() =>
			//{
				Vector3 centreDestination = destination.getLocal() + Centre - navigationBlock.Position * myCubeGrid.GridSize;
			//}, 10, false);
			//myLogger.debugLog("Time to centreDestination: " + results.Pretty_FiveNumbers(), "SetDestination()");

			//results = TimeAction.Time(() =>
			//{
				rejectAll();
			//}, 10, false);
			//myLogger.debugLog("Time to reject all: " + results.Pretty_FiveNumbers(), "SetDestination()");
			//results = TimeAction.Time(() =>
			//{
				createCapsule(centreDestination);
			//}, 10, false);
			//myLogger.debugLog("Time to createCapsule: " + results.Pretty_FiveNumbers(), "SetDestination()");
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
				//if (TestRejection == ProfileRejection)
				if (Vector3.DistanceSquared(TestRejection, ProfileRejection) < GridSize + myCubeGrid.GridSize)
					return true;
			return false;
		}

		#endregion
		#region Private Methods

		///// <summary>
		///// Perform a rejection of a cell from Displacement and round the result to 1 decimal
		///// </summary>
		///// <remarks>
		///// To ensure consistency, all rejections should be performed by this method.
		///// </remarks>
		//private Vector3 RejectCell(Vector3I cellPosition)
		//{
		//	Vector3 rejection = Vector3.Reject(cellPosition, DirectionNorm);
		//	rejection.ApplyOperation((comp) => Math.Round(comp, 1), out rejection);
		//	return rejection;
		//}

		/// <summary>
		/// Convert metres to a cell, perfrom a rejection of the cell, and round the result to 1 decimal
		/// </summary>
		private Vector3 RejectMetres(Vector3 metresPosition)
		{ return Vector3.Reject(metresPosition, DirectionNorm); } // RejectCell(myCubeGrid.WorldToGridInteger(metresPosition)); }

		/// <summary>
		/// Perform a vector rejection of every occupied cell from the specified direction and store the results in rejectionCells.
		/// </summary>
		/// <remarks>
		/// It is not useful to normalize direction first.
		/// </remarks>
		private void rejectAll()
		{
			VRage.Exceptions.ThrowIf<ArgumentNullException>(DirectionNorm == null, "DirectionNorm");
			rejectionCells = new MyUniqueList<Vector3>();

			ReadOnlyList<IMySlimBlock> immutable;
			using (lock_SlimBlocks.AcquireSharedUsing())
				immutable = SlimBlocks.immutable();

			CentreRejection = RejectMetres(Centre);
			//Centre.Rejection(DirectionNorm, ref Displacement_PartialCalculation).ApplyOperation(Math.Round, out CentreRejection);//.Round(CubeGrid.GridSize);
			foreach (IMySlimBlock slim in immutable)
			{
				slim.ForEachCellSurround((cell) =>
				{
					Vector3 rejection = RejectMetres(cell * myCubeGrid.GridSize);
					//(cell * myCubeGrid.GridSize).Rejection(DirectionNorm, ref Displacement_PartialCalculation).ApplyOperation(Math.Round, out rejection);
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
