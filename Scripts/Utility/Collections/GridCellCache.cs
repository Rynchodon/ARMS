using System;
using System.Collections.Generic;
using Sandbox.Game.Entities;
using Sandbox.ModAPI;
using VRage;
using VRage.Game.Entity;
using VRage.Game.ModAPI;
using VRageMath;

namespace Rynchodon
{

	/// <summary>
	/// Keeps a HashSet of all the occupied cells in a grid.
	/// </summary>
	public class GridCellCache
	{

		public static GridCellCache GetCellCache(IMyCubeGrid grid)
		{
			if (grid.Closed || Globals.WorldClosed)
				return null;

			GridCellCache cache;
			if (!Registrar.TryGetValue(grid, out cache))
			{
				cache = new GridCellCache(grid);
				Registrar.Add(grid, cache);
			}
			return cache;
		}

		private readonly Logger m_logger;
		private readonly IMyCubeGrid m_grid;

		private HashSet<Vector3I> CellPositions = new HashSet<Vector3I>();
		private List<MyCubeBlock> LargeDoors = new List<MyCubeBlock>();
		private FastResourceLock lock_cellPositions = new FastResourceLock();

		/// <summary>
		/// Not totally accurate as it does not check door extension.
		/// </summary>
		public int CellCount
		{
			get { return CellPositions.Count + LargeDoors.Count; }
		}

		private GridCellCache(IMyCubeGrid grid)
		{
			m_logger = new Logger("GridCellCache", () => grid.DisplayName);
			m_grid = grid;

			List<IMySlimBlock> dummy = new List<IMySlimBlock>();
			MainLock.UsingShared(() => {
				using (lock_cellPositions.AcquireExclusiveUsing())
					grid.GetBlocks(dummy, slim => {
						Add(slim);
						return false;
					});

				grid.OnBlockAdded += grid_OnBlockAdded;
				grid.OnBlockRemoved += grid_OnBlockRemoved;
			});

			m_logger.debugLog("Initialized");
		}

		public void ForEach(Action<Vector3I> action)
		{
			using (lock_cellPositions.AcquireSharedUsing())
			{
				foreach (Vector3I cell in CellPositions)
					action(cell);

				foreach (MyCubeBlock door in LargeDoors)
				{
					if (door.Closed)
						continue;

					Dictionary<string, MyEntitySubpart> subparts = door.Subparts;
					foreach (var part in subparts)
						action(m_grid.WorldToGridInteger(part.Value.PositionComp.GetPosition()));
				}
			}
		}

		public void ForEach(Func<Vector3I, bool> function)
		{
			using (lock_cellPositions.AcquireSharedUsing())
			{
				foreach (Vector3I cell in CellPositions)
					if (function(cell))
						return;

				foreach (MyCubeBlock door in LargeDoors)
				{
					if (door.Closed)
						continue;

					Dictionary<string, MyEntitySubpart> subparts = door.Subparts;
					foreach (var part in subparts)
						if (function(m_grid.WorldToGridInteger(part.Value.PositionComp.GetPosition())))
							return;
				}
			}
		}

		public IEnumerable<Vector3I> EachCell()
		{
			using (lock_cellPositions.AcquireSharedUsing())
			{
				foreach (Vector3I cell in CellPositions)
					yield return cell;

				foreach (MyCubeBlock door in LargeDoors)
				{
					if (door.Closed)
						continue;

					foreach (var part in door.Subparts)
						yield return m_grid.WorldToGridInteger(part.Value.PositionComp.GetPosition());
				}
			}
		}

		/// <summary>
		/// Gets the closest occupied cell.
		/// </summary>
		public void GetClosestOccupiedCell(ref Vector3I startCell, ref Vector3I previousCell, out Vector3I closestCell)
		{
			closestCell = previousCell;
			int closestDistance;
			using (lock_cellPositions.AcquireSharedUsing())
				// bias against switching
				closestDistance = CellPositions.Contains(previousCell) ? previousCell.DistanceSquared(startCell) - 2 : int.MaxValue;

			foreach (Vector3I cell in EachCell())
			{
				int dist = cell.DistanceSquared(startCell);
				if (dist < closestDistance)
				{
					closestCell = cell;
					closestDistance = dist;
				}
			}
		}

		/// <summary>
		/// Gets the closest occupied cell.
		/// </summary>
		public void GetClosestOccupiedCell(ref Vector3D startWorld, ref Vector3I previousCell, out Vector3I closestCell)
		{
			Vector3I startPoint = m_grid.WorldToGridInteger(startWorld);
			GetClosestOccupiedCell(ref startPoint, ref previousCell, out closestCell);
		}

		private void grid_OnBlockAdded(IMySlimBlock slim)
		{
			using (lock_cellPositions.AcquireExclusiveUsing())
				Add(slim);
		}

		private void grid_OnBlockRemoved(IMySlimBlock slim)
		{
			using (lock_cellPositions.AcquireExclusiveUsing())
			{
				if (slim.FatBlock is IMyDoor && ((MyCubeBlock)slim.FatBlock).Subparts.Count != 0)
				{
					LargeDoors.Remove((MyCubeBlock)slim.FatBlock);
					return;
				}

				// some cells may not be occupied
				slim.ForEachCell(cell => {
					CellPositions.Remove(cell);
					return false;
				});
			}
		}

		private void Add(IMySlimBlock slim)
		{
			bool checkLocal;
			if (slim.FatBlock != null)
			{
				if (slim.FatBlock is IMyDoor && ((MyCubeBlock)slim.FatBlock).Subparts.Count != 0)
				{
					LargeDoors.Add((MyCubeBlock)slim.FatBlock);
					return;
				}

				checkLocal = slim.FatBlock is IMyMotorStator || slim.FatBlock is IMyPistonBase;
			}
			else
				checkLocal = false;

			slim.ForEachCell(cell => {
				if (checkLocal)
				{
					// for piston base and stator, cell may not actually be inside local AABB
					// if this is done for doors, they would always be treated as open
					// other blocks have not been tested
					Vector3 positionBlock = Vector3.Transform(slim.CubeGrid.GridIntegerToWorld(cell), slim.FatBlock.WorldMatrixNormalizedInv);

					if (slim.FatBlock.LocalAABB.Contains(positionBlock) == ContainmentType.Disjoint)
						return false;
				}

				CellPositions.Add(cell);
				return false;
			});
		}

	}

}
