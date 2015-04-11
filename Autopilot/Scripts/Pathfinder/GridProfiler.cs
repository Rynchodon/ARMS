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
	/// Creates a List of every occupied cell for a grid. This List is uses to create projections of the grid. 
	/// </summary>
	public class GridProfiler
	{
		public IMyCubeGrid CubeGrid { get; private set; }

		private static Dictionary<IMyCubeGrid, GridProfiler> registry = new Dictionary<IMyCubeGrid, GridProfiler>();
		private static FastResourceLock lock_registry = new FastResourceLock();
		/// <summary>
		/// All the local positions of occupied cells in this grid.
		/// </summary>
		private ListCacher<Vector3I> OccupiedCells;
		private FastResourceLock lock_OccupiedCells = new FastResourceLock();

		#region Life Cycle

		/// <summary>
		/// Create a profile for a grid.
		/// </summary>
		/// <param name="grid">grid to profile</param>
		private GridProfiler(IMyCubeGrid grid)
		{
			this.CubeGrid = grid;

			this.OccupiedCells = new ListCacher<Vector3I>();

			// instead of iterating over blocks, test cells of grid for contents (no need to lock anything)
			List<Vector3I> mutable = OccupiedCells.mutable();
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
			IMyCubeBlock fatblock = obj.FatBlock;
			if (fatblock == null)



			foreachVector3I(obj

			throw new NotImplementedException();
		}

		/// <summary>
		/// Remove a block from the profile.
		/// </summary>
		private void Grid_OnBlockRemoved(IMySlimBlock obj)
		{
			throw new NotImplementedException();
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
		public static GridProfiler getFor(IMyCubeGrid grid, FastResourceLock iterateLock)
		{
			VRage.Exceptions.ThrowIf<NotImplementedException>(true);
			return null;
		}

		/// <summary>
		/// 
		/// </summary>
		/// <returns></returns>
		public ReadOnlyList<Vector3I> get_OccupiedCells()
		{
			using (lock_OccupiedCells.AcquireSharedUsing())
			{
				OccupiedCells.IsClean = false;
				return new ReadOnlyList<Vector3I>(OccupiedCells);
			}
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
