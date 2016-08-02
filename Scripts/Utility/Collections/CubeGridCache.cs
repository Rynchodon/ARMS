using System;
using System.Collections.Generic;
using Sandbox.Game.Entities;
using Sandbox.ModAPI;
using VRage;
using VRage.Game;
using VRage.Game.ModAPI;
using VRage.ModAPI;
using VRage.ObjectBuilders;
using VRage.Utils;
using System.Linq;
using VRageMath;
using Rynchodon.Utility.Vectors;
using VRage.Game.Entity;

namespace Rynchodon
{
	/// <summary>
	/// A better way to get cube blocks of type from a grid.
	/// </summary>
	public class CubeGridCache
	{

		private static FastResourceLock lock_constructing = new FastResourceLock();
		public static LockedDictionary<string, MyDefinitionId> DefinitionType = new LockedDictionary<string, MyDefinitionId>();

		static CubeGridCache()
		{
			Logger.SetFileName("CubeGridCache");
			MyAPIGateway.Entities.OnCloseAll += Entities_OnCloseAll;
		}

		private static void Entities_OnCloseAll()
		{
			MyAPIGateway.Entities.OnCloseAll -= Entities_OnCloseAll;
			lock_constructing = null;
			DefinitionType = null;
		}

		/// <summary>
		/// will return null if grid is closed or CubeGridCache cannot be created
		/// </summary>
		public static CubeGridCache GetFor(IMyCubeGrid grid)
		{
			if (grid.Closed || grid.MarkedForClose || Globals.WorldClosed)
				return null;

			CubeGridCache value;
			if (Registrar.TryGetValue(grid.EntityId, out value))
				return value;

			using (lock_constructing.AcquireExclusiveUsing())
			{
				if (Registrar.TryGetValue(grid.EntityId, out value))
					return value;

				try
				{ return new CubeGridCache(grid); }
				catch (Exception e)
				{
					Logger.AlwaysLog("Exception on creation: " + e, Logger.severity.WARNING);
					return null;
				}
			}
		}

		private readonly Logger myLogger;
		private readonly IMyCubeGrid CubeGrid;

		/// <summary>Blocks that do not have a FatBlock</summary>
		private List<IMySlimBlock> SlimOnly = new List<IMySlimBlock>();
		private Dictionary<MyObjectBuilderType, List<MyCubeBlock>> CubeBlocks = new Dictionary<MyObjectBuilderType, List<MyCubeBlock>>();
		private FastResourceLock lock_blocks = new FastResourceLock();

		public int CellCount { get; private set; }
		public int TerminalBlocks { get; private set; }

		private CubeGridCache(IMyCubeGrid grid)
		{
			myLogger = new Logger("CubeGridCache", () => grid.DisplayName);
			CubeGrid = grid;
			List<IMySlimBlock> allSlims = new List<IMySlimBlock>();
			CubeGrid.GetBlocks_Safe(allSlims, slim => slim.FatBlock != null);

			using (lock_blocks.AcquireExclusiveUsing())
				foreach (IMySlimBlock slim in allSlims)
					Add(slim);

			CubeGrid.OnBlockAdded += CubeGrid_OnBlockAdded;
			CubeGrid.OnBlockRemoved += CubeGrid_OnBlockRemoved;
			CubeGrid.OnClosing += CubeGrid_OnClosing;

			Registrar.Add(CubeGrid, this);
			myLogger.debugLog("built for: " + CubeGrid.DisplayName, Logger.severity.DEBUG);
		}

		private void CubeGrid_OnClosing(IMyEntity grid)
		{
			CubeGrid.OnBlockAdded -= CubeGrid_OnBlockAdded;
			CubeGrid.OnBlockRemoved -= CubeGrid_OnBlockRemoved;
			CubeGrid.OnClosing -= CubeGrid_OnClosing;
		}

		private void CubeGrid_OnBlockAdded(IMySlimBlock obj)
		{
			lock_blocks.AcquireExclusive();
			try { Add(obj); }
			catch (Exception e) { myLogger.alwaysLog("Exception: " + e, Logger.severity.ERROR); }
			finally { lock_blocks.ReleaseExclusive(); }
		}

		private void Add(IMySlimBlock obj)
		{
			IMyCubeBlock fatblock = obj.FatBlock;
			if (fatblock == null)
			{
				SlimOnly.Add(obj);
				CellCount++;
				return;
			}

			MyCubeBlock cubeBlock = (MyCubeBlock)fatblock;
			MyObjectBuilderType typeId = cubeBlock.BlockDefinition.Id.TypeId;
			List<MyCubeBlock> blockList;
			if (!CubeBlocks.TryGetValue(typeId, out blockList))
			{
				blockList = new List<MyCubeBlock>();
				CubeBlocks.Add(typeId, blockList);
			}
			blockList.Add(cubeBlock);

			IMyTerminalBlock term = cubeBlock as IMyTerminalBlock;
			if (term != null)
			{
				DefinitionType.TrySet(term.DefinitionDisplayNameText, ((MyCubeBlock)term).BlockDefinition.Id);
				TerminalBlocks++;
			}

			Vector3I cellSize = cubeBlock.Max - cubeBlock.Min + 1;
			CellCount += cellSize.X * cellSize.Y * cellSize.Z;
		}

		private void CubeGrid_OnBlockRemoved(IMySlimBlock obj)
		{
			IMyCubeBlock fatblock = obj.FatBlock;
			if (fatblock == null)
			{
				SlimOnly.Remove(obj);
				CellCount--;
				return;
			}

			myLogger.debugLog("block removed: " + obj.getBestName());
			myLogger.debugLog("block removed: " + obj.FatBlock.DefinitionDisplayNameText + "/" + obj.getBestName());
			lock_blocks.AcquireExclusive();
			try
			{
				MyCubeBlock cubeBlock = (MyCubeBlock)fatblock;
				MyObjectBuilderType typeId = cubeBlock.BlockDefinition.Id.TypeId;
				List<MyCubeBlock> blockList;
				if (!CubeBlocks.TryGetValue(typeId, out blockList))
				{
					myLogger.debugLog("failed to get list of type: " + typeId);
					return;
				}
				if (blockList.Count == 1)
					CubeBlocks.Remove(typeId);
				else
					blockList.Remove(cubeBlock);

				if (cubeBlock is IMyTerminalBlock)
					TerminalBlocks--;

				Vector3I cellSize = cubeBlock.Max - cubeBlock.Min + 1;
				CellCount -= cellSize.X * cellSize.Y * cellSize.Z;
			}
			catch (Exception e) { myLogger.alwaysLog("Exception: " + e, Logger.severity.ERROR); }
			finally { lock_blocks.ReleaseExclusive(); }
			myLogger.debugLog("leaving CubeGrid_OnBlockRemoved(): " + obj.getBestName());
		}

		public IEnumerable<MyCubeBlock> BlocksOfType(MyObjectBuilderType typeId)
		{
			lock_blocks.AcquireShared();
			try
			{
				List<MyCubeBlock> blockList;
				if (CubeBlocks.TryGetValue(typeId, out blockList))
					return blockList;
				else
					return null;
			}
			finally
			{
				lock_blocks.ReleaseShared();
			}
		}

		public IEnumerable<MyCubeBlock> BlocksOfType(MyDefinitionId defId)
		{
			lock_blocks.AcquireShared();
			try
			{
				List<MyCubeBlock> blockList;
				if (CubeBlocks.TryGetValue(defId.TypeId, out blockList))
					for (int i = blockList.Count - 1; i >= 0; i--)
					{
						MyCubeBlock block = (MyCubeBlock)blockList[i];
						if (!block.Closed && block.BlockDefinition.Id.SubtypeId == defId.SubtypeId)
							yield return block;
					}
			}
			finally
			{
				lock_blocks.ReleaseShared();
			}
		}

		public IEnumerable<MyCubeBlock> AllCubeBlocks()
		{
			lock_blocks.AcquireShared();
			try
			{
				foreach (List<MyCubeBlock> blockList in CubeBlocks.Values)
					for (int i = blockList.Count - 1; i >= 0; i--)
						yield return blockList[i];
			}
			finally
			{
				lock_blocks.ReleaseShared();
			}
		}

		public IEnumerable<IMySlimBlock> AllSlimBlocks()
		{
			lock_blocks.AcquireShared();
			try
			{
				for (int i = SlimOnly.Count - 1; i >= 0; i--)
					yield return SlimOnly[i];

				foreach (List<MyCubeBlock> blockList in CubeBlocks.Values)
					for (int i = blockList.Count - 1; i >= 0; i--)
						yield return blockList[i].SlimBlock;
			}
			finally
			{
				lock_blocks.ReleaseShared();
			}
		}

		public IEnumerable<Vector3I> OccupiedCells()
		{
			lock_blocks.AcquireShared();
			try
			{
				for (int i = SlimOnly.Count - 1; i >= 0; i--)
					yield return SlimOnly[i].Position;

				Matrix invLocal = new Matrix();

				foreach (List<MyCubeBlock> blockList in CubeBlocks.Values)
					for (int i = blockList.Count - 1; i >= 0; i--)
					{
						MyCubeBlock block = blockList[i];
						if (block.Closed)
							continue;

						if (block is IMyDoor && block.Subparts.Count != 0)
						{
							foreach (MyEntitySubpart part in block.Subparts.Values)
								yield return block.CubeGrid.WorldToGridInteger(part.PositionComp.GetPosition());
							continue;
						}

						// for piston base and stator, cell may not actually be inside local AABB
						// if this is done for doors, they would always be treated as open
						// other blocks have not been tested
						bool checkLocal = block is IMyMotorStator || block is IMyPistonBase;

						if (checkLocal)
							invLocal = Matrix.Invert(block.PositionComp.LocalMatrix);

						Vector3I cell;
						for (cell.X = block.Min.X; cell.X <= block.Max.X; cell.X++)
							for (cell.Y = block.Min.Y; cell.Y <= block.Max.Y; cell.Y++)
								for (cell.Z = block.Min.Z; cell.Z <= block.Max.Z; cell.Z++)
								{
									if (checkLocal)
									{
										Vector3 posGrid = cell * block.CubeGrid.GridSize;
										Vector3 posBlock;
										Vector3.Transform(ref posGrid, ref invLocal, out posBlock);
										if (block.PositionComp.LocalAABB.Contains(posBlock) == ContainmentType.Disjoint)
											continue;
									}
									yield return cell;
								}
					}
			}
			finally
			{
				lock_blocks.ReleaseShared();
			}
		}

		///// <summary>
		///// Yields every occupied cell and all of its neighbours, expect some cells to be yielded more than once.
		///// </summary>
		//public IEnumerable<Vector3I> OccupiedCellsAndNeighbours()
		//{
		//	lock_blocks.AcquireShared();
		//	try
		//	{
		//		for (int i = SlimOnly.Count - 1; i >= 0; i--)
		//		{
		//			Vector3I blockPosition = SlimOnly[i].Position;
		//			Vector3I blockCell;
		//			for (blockCell.X = blockPosition.X - 1; blockCell.X <= blockPosition.X + 1; blockCell.X++)
		//				for (blockCell.Y = blockPosition.Y - 1; blockCell.Y <= blockPosition.Y + 1; blockCell.Y++)
		//					for (blockCell.Z = blockPosition.Z - 1; blockCell.Z <= blockPosition.Z + 1; blockCell.Z++)
		//						yield return blockCell;
		//		}

		//		Matrix invLocal = new Matrix();

		//		foreach (List<MyCubeBlock> blockList in CubeBlocks.Values)
		//			for (int i = blockList.Count - 1; i >= 0; i--)
		//			{
		//				MyCubeBlock block = blockList[i];
		//				if (block.Closed)
		//					continue;

		//				if (block is IMyDoor && block.Subparts.Count != 0)
		//				{
		//					foreach (MyEntitySubpart part in block.Subparts.Values)
		//					{
		//						Vector3I partPosition = block.CubeGrid.WorldToGridInteger(part.PositionComp.GetPosition());
		//						Vector3I partCell;
		//						for (partCell.X = partPosition.X - 1; partCell.X <= partPosition.X + 1; partCell.X++)
		//							for (partCell.Y = partPosition.Y - 1; partCell.Y <= partPosition.Y + 1; partCell.Y++)
		//								for (partCell.Z = partPosition.Z - 1; partCell.Z <= partPosition.Z + 1; partCell.Z++)
		//									yield return partCell;
		//					}
		//					continue;
		//				}

		//				// for piston base and stator, cell may not actually be inside local AABB
		//				// if this is done for doors, they would always be treated as open
		//				// other blocks have not been tested
		//				bool checkLocal = block is IMyMotorStator || block is IMyPistonBase;

		//				if (checkLocal)
		//					invLocal = Matrix.Invert(block.PositionComp.LocalMatrix);

		//				Vector3I blockCell;
		//				for (blockCell.X = block.Min.X - 1; blockCell.X <= block.Max.X + 1; blockCell.X++)
		//					for (blockCell.Y = block.Min.Y - 1; blockCell.Y <= block.Max.Y + 1; blockCell.Y++)
		//						for (blockCell.Z = block.Min.Z - 1; blockCell.Z <= block.Max.Z + 1; blockCell.Z++)
		//						{
		//							if (checkLocal)
		//							{
		//								Vector3 posGrid = blockCell * block.CubeGrid.GridSize;
		//								Vector3 posBlock;
		//								Vector3.Transform(ref posGrid, ref invLocal, out posBlock);
		//								if (block.PositionComp.LocalAABB.Contains(posBlock) == ContainmentType.Disjoint)
		//									continue;
		//							}
		//							yield return blockCell;
		//						}
		//			}
		//	}
		//	finally
		//	{
		//		lock_blocks.ReleaseShared();
		//	}
		//}

		public int CountByType(MyObjectBuilderType typeId)
		{
			lock_blocks.AcquireShared();
			try
			{
				List<MyCubeBlock> blockList;
				if (CubeBlocks.TryGetValue(typeId, out blockList))
					return blockList.Count;
				else
					return 0;
			}
			finally
			{
				lock_blocks.ReleaseShared();
			}
		}

		public int CountByType(MyDefinitionId defId)
		{
			int count = 0;
			lock_blocks.AcquireShared();
			try
			{
				List<MyCubeBlock> blockList;
				if (CubeBlocks.TryGetValue(defId.TypeId, out blockList))
					for (int i = blockList.Count - 1; i >= 0; i--)
					{
						MyCubeBlock block = (MyCubeBlock)blockList[i];
						if (!block.Closed && block.BlockDefinition.Id.SubtypeId == defId.SubtypeId)
							count++;
					}
			}
			finally
			{
				lock_blocks.ReleaseShared();
			}
			return count;
		}

		/// <summary>
		/// Count the number of blocks that match a particular condition.
		/// </summary>
		/// <param name="objBuildType">Type to search for</param>
		/// <param name="condition">Condition that block must match</param>
		/// <returns>The number of blocks of the given type that match the condition.</returns>
		public int CountByType(MyObjectBuilderType objBuildType, Func<IMyCubeBlock, bool> condition, int stopCaringAt = int.MaxValue)
		{
			lock_blocks.AcquireShared();
			try
			{
				List<MyCubeBlock> blockList;
				if (CubeBlocks.TryGetValue(objBuildType, out blockList))
				{
					int count = 0;
					foreach (MyCubeBlock block in blockList)
						if (condition(block))
						{
							count++;
							if (count >= stopCaringAt)
								return count;
						}

					return count;
				}
				return 0;
			}
			finally
			{
				lock_blocks.ReleaseShared();
			}
		}

		/// <summary>
		/// Gets the closest occupied cell.
		/// </summary>
		public Vector3I GetClosestOccupiedCell(Vector3I startCell, Vector3I previousCell)
		{
			Vector3I closestCell = previousCell;
			int closestDistance = CubeGrid.GetCubeBlock(closestCell) != null ? previousCell.DistanceSquared(startCell) - 2 : int.MaxValue;

			CubeGridCache cache = CubeGridCache.GetFor(CubeGrid);
			if (cache == null)
				return closestCell;

			foreach (Vector3I cell in cache.OccupiedCells())
			{
				int dist = cell.DistanceSquared(startCell);
				if (dist < closestDistance)
				{
					closestCell = cell;
					closestDistance = dist;
				}
			}

			return closestCell;
		}

		/// <summary>
		/// Gets the closest occupied cell.
		/// </summary>
		public Vector3I GetClosestOccupiedCell(Vector3D startWorld, Vector3I previousCell)
		{
			return GetClosestOccupiedCell(CubeGrid.WorldToGridInteger(startWorld), previousCell);
		}

	}
}
