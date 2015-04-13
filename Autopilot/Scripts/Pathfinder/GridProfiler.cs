using System;
using System.Collections.Generic;
//using System.Linq;
//using System.Text;

using Sandbox.ModAPI;

using VRage;
using VRageMath;

namespace Rynchodon.Autopilot.Pathfinder
{
	/// <summary>
	/// Creates a List of every occupied cell for a grid. This List is used to create projections of the grid. 
	/// </summary>
	public class GridProfiler
	{
		public IMyCubeGrid CubeGrid { get; private set; }

		private static Dictionary<IMyCubeGrid, GridProfiler> registry = new Dictionary<IMyCubeGrid, GridProfiler>();
		private static FastResourceLock lock_registry = new FastResourceLock();
		/// <summary>
		/// All the local positions of occupied cells in this grid.
		/// </summary>
		private ListSnapshots<Vector3I> OccupiedCells;
		private FastResourceLock lock_OccupiedCells = new FastResourceLock();

		#region Life Cycle

		/// <summary>
		/// Create a profile for a grid.
		/// </summary>
		/// <param name="grid">grid to profile</param>
		private GridProfiler(IMyCubeGrid grid)
		{
			this.CubeGrid = grid;

			this.OccupiedCells = new ListSnapshots<Vector3I>();

			// instead of iterating over blocks, test cells of grid for contents (no need to lock anything)
			ReadOnlyList<Vector3I> mutable = OccupiedCells.mutable();
			foreachVector3I(CubeGrid.Min, CubeGrid.Max, (cell) =>
			{
				if (CubeGrid.CubeExists(cell))
					mutable.Add(cell);
			});

			CubeGrid.OnBlockAdded += Grid_OnBlockAdded;
			CubeGrid.OnBlockRemoved += Grid_OnBlockRemoved;
			CubeGrid.OnClose += Grid_OnClose;

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
		/// <para>Not Implemented</para>
		/// Get or build the GridProfiler for a given grid.
		/// </summary>
		/// <param name="grid"></param>
		/// <param name="iterateLock">if not null and a build is required, obtains a shared lock while iterating over blocks in a grid</param>
		public static GridProfiler getFor(IMyCubeGrid grid)
		{
			VRage.Exceptions.ThrowIf<NotImplementedException>(true);
			return null;
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
		/// Perform a vector rejection of every occupied cell from the specified direction and store the results in a HashSet.
		/// </summary>
		/// <param name="direction">vector to perform rejection from</param>
		/// <returns></returns>
		public HashSet<Vector3> rejectAll(Vector3 direction)
		{
			HashSet<Vector3> rejections = new HashSet<Vector3>();
			Vector3? dir_part = null;
			foreach (Vector3I cell in OccupiedCells.immutable())
				rejections.Add((cell * CubeGrid.GridSize).Rejection(direction, ref dir_part));

			return rejections;
		}

		#endregion
		#region Private Functions

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

		#endregion
	}
}
