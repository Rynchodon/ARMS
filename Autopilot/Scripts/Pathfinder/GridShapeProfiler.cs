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
		public IMyCubeGrid CubeGrid { get; private set; }
		//public float PathBuffer { get { return CubeGrid.GridSize * 2; } }
		//public float PathBufferSquared { get { return PathBuffer * PathBuffer; } }

		private static Dictionary<IMyCubeGrid, GridShapeProfiler> registry = new Dictionary<IMyCubeGrid, GridShapeProfiler>();
		private static FastResourceLock lock_registry = new FastResourceLock();
		/// <summary>
		/// All the local positions of occupied cells in this grid.
		/// </summary>
		private ListSnapshots<Vector3I> OccupiedCells;
		private FastResourceLock lock_OccupiedCells = new FastResourceLock();

		private Vector3 Centre { get { return CubeGrid.LocalAABB.Center; } }
		private Vector3 CentreRejection;
		private Vector3 Displacement;
		private Vector3? Displacement_PartialCalculation = null;

		public MyUniqueList<Vector3> rejectionCells;
		public Path myPath { get; private set; }

		private Logger myLogger = new Logger(null, "GridShapeProfiler");

		#region Life Cycle

		/// <summary>
		/// Create a profile for a grid.
		/// </summary>
		/// <param name="grid">grid to profile</param>
		private GridShapeProfiler(IMyCubeGrid grid)
		{
			this.CubeGrid = grid;
			myLogger = new Logger("GridShapeProfiler", () => CubeGrid.DisplayName);
			this.OccupiedCells = new ListSnapshots<Vector3I>();

			// instead of iterating over blocks, test cells of grid for contents (no need to lock grid)
			ReadOnlyList<Vector3I> mutable = OccupiedCells.mutable();
			foreachVector3I(CubeGrid.Min, CubeGrid.Max, (cell) =>
			{
				if (CubeGrid.CubeExists(cell))
					mutable.Add(cell);
			});

			CubeGrid.OnBlockAdded += Grid_OnBlockAdded;
			CubeGrid.OnBlockRemoved += Grid_OnBlockRemoved;
			CubeGrid.OnClose += Grid_OnClose;

			using (lock_registry.AcquireExclusiveUsing())
				registry.Add(this.CubeGrid, this);
		}

		/// <summary>
		/// Add a block to the profile.
		/// </summary>
		private void Grid_OnBlockAdded(IMySlimBlock obj)
		{
			using (lock_OccupiedCells.AcquireExclusiveUsing())
			{
				IMyCubeBlock fatblock = obj.FatBlock;
				ReadOnlyList<Vector3I> mutable = OccupiedCells.mutable();
				if (fatblock == null)
					mutable.Add(obj.Position);
				else
					foreachVector3I(fatblock.Min, fatblock.Max, (cell) => { mutable.Add(cell); });
			}
		}

		/// <summary>
		/// Remove a block from the profile.
		/// </summary>
		private void Grid_OnBlockRemoved(IMySlimBlock obj)
		{
			using (lock_OccupiedCells.AcquireExclusiveUsing())
			{
				IMyCubeBlock fatblock = obj.FatBlock;
				ReadOnlyList<Vector3I> mutable = OccupiedCells.mutable();
				if (fatblock == null)
					mutable.Remove(obj.Position);
				else
					foreachVector3I(fatblock.Min, fatblock.Max, (cell) => { mutable.Remove(cell); });
			}
		}

		/// <summary>
		/// Invalidate and cleanup the profile.
		/// </summary>
		private void Grid_OnClose(IMyEntity obj)
		{
			// remove references to this
			using (lock_registry.AcquireExclusiveUsing())
				registry.Remove(CubeGrid);

			CubeGrid.OnBlockAdded -= Grid_OnBlockAdded;
			CubeGrid.OnBlockRemoved -= Grid_OnBlockRemoved;
			CubeGrid.OnClose -= Grid_OnClose;

			// invalidate
			using (lock_OccupiedCells.AcquireExclusiveUsing())
				OccupiedCells = null;
			CubeGrid = null;
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
		/// Gets a snapshot of current occupied cells
		/// </summary>
		/// <returns>An immutable ReadOnlyList of occupied cells</returns>
		public ReadOnlyList<Vector3I> get_OccupiedCells()
		{
			using (lock_OccupiedCells.AcquireSharedUsing())
				return OccupiedCells.immutable();
		}

		/// <summary>
		/// Set the destination, calulate the profile.
		/// </summary>
		/// <param name="destination">waypoint or destination to fly to</param>
		/// <param name="navigationBlock">usually remote control</param>
		public void SetDestination(RelativeVector3F destination, IMyCubeBlock navigationBlock, Vector3? displacement = null)
		{
			if (displacement == null)
				this.Displacement = destination.getLocal() - navigationBlock.Position * CubeGrid.GridSize;
			else
				this.Displacement = (Vector3)displacement;
			Vector3 centreDestination = destination.getLocal() + (Centre - navigationBlock.Position) * CubeGrid.GridSize;

			rejectAll();
			createCapsule(centreDestination);
		}

		/// <summary>
		/// <para>Perform a vector rejection from direction to destination.</para>
		/// <para>Destination must be set first.</para>
		/// </summary>
		/// <param name="toReject">the grid-local vector to reject</param>
		/// <returns>the rejected vector</returns>
		public Vector3 rejectVector(Vector3 toReject)
		{ return toReject.Rejection(Displacement, ref Displacement_PartialCalculation).Round(CubeGrid.GridSize); }

		#endregion
		#region Private Methods

		/// <summary>
		/// Perform an Action for each Vector3I from min to max (inclusive).
		/// </summary>
		/// <param name="min">starting Vector3I</param>
		/// <param name="max">ending Vector3I</param>
		/// <param name="action">action to invoke</param>
		private void foreachVector3I(Vector3I min, Vector3I max, Action<Vector3I> action)
		{
			for (int x = min.X; x <= max.X; x++)
				for (int y = min.Y; y <= max.Y; y++)
					for (int z = min.Z; z <= max.Z; z++)
						action.Invoke(new Vector3I(x, y, z));
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
			rejectionCells = new MyUniqueList<Vector3>();

			ReadOnlyList<Vector3I> immutable;
			using (lock_OccupiedCells.AcquireSharedUsing())
				immutable = OccupiedCells.immutable();

			CentreRejection = Centre.Rejection(Displacement, ref Displacement_PartialCalculation).Round(CubeGrid.GridSize);
			foreach (Vector3I cell in immutable)
			{
				Vector3 rejection = (cell * CubeGrid.GridSize).Rejection(Displacement, ref Displacement_PartialCalculation).Round(CubeGrid.GridSize);
				rejectionCells.Add(rejection);
			}
		}

		/// <param name="centreDestination">where the centre of the grid will end up (local)</param>
		private void createCapsule(Vector3 centreDestination)
		{
			float radiusSquared = 0;
			foreach (Vector3 rejection in rejectionCells)
			{
				float distanceSquared = (rejection - CentreRejection).LengthSquared();
				if (distanceSquared > radiusSquared)
					radiusSquared = distanceSquared;
			}

			myPath = new Path(RelativeVector3F.createFromLocal(Centre, CubeGrid), RelativeVector3F.createFromLocal(centreDestination, CubeGrid), radiusSquared, CubeGrid.GridSize);
		}

		#endregion
	}
}
