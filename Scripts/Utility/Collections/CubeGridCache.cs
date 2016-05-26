using System;
using System.Collections.Generic;
using Sandbox.ModAPI;
using VRage;
using VRage.Game.ModAPI;
using VRage.ModAPI;
using VRage.ObjectBuilders;

namespace Rynchodon
{
	/// <summary>
	/// A better way to get cube blocks of type from a grid.
	/// </summary>
	public class CubeGridCache
	{
		private static FastResourceLock lock_constructing = new FastResourceLock();
		public static LockedDictionary<string, MyObjectBuilderType> DefinitionType = new LockedDictionary<string, MyObjectBuilderType>();

		static CubeGridCache()
		{
			MyAPIGateway.Entities.OnCloseAll += Entities_OnCloseAll;
		}

		private static void Entities_OnCloseAll()
		{
			MyAPIGateway.Entities.OnCloseAll -= Entities_OnCloseAll;
			lock_constructing = null;
			DefinitionType = null;
		}

		[Obsolete("Use BlockTypeList constructor")]
		public static BlockTypeList GetBlockList(string[] blockNamesContain)
		{
			return new BlockTypeList(blockNamesContain);
		}

		private readonly Logger myLogger;
		private Dictionary<MyObjectBuilderType, ListSnapshots<IMyCubeBlock>> CubeBlocks_Type = new Dictionary<MyObjectBuilderType, ListSnapshots<IMyCubeBlock>>();
		private FastResourceLock lock_CubeBlocks = new FastResourceLock();

		private readonly IMyCubeGrid CubeGrid;

		public int TerminalBlocks { get; private set; }

		private CubeGridCache(IMyCubeGrid grid)
		{
			myLogger = new Logger("CubeGridCache", () => grid.DisplayName);
			CubeGrid = grid;
			List<IMySlimBlock> allSlims = new List<IMySlimBlock>();
			CubeGrid.GetBlocks_Safe(allSlims, slim => slim.FatBlock != null);

			foreach (IMySlimBlock slim in allSlims)
				CubeGrid_OnBlockAdded(slim);

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

			myLogger.debugLog("closing", Logger.severity.DEBUG);

			using (lock_CubeBlocks.AcquireExclusiveUsing())
				CubeBlocks_Type = null;

			myLogger.debugLog("closed", Logger.severity.DEBUG);
		}

		private void CubeGrid_OnBlockAdded(IMySlimBlock obj)
		{
			IMyCubeBlock fatblock = obj.FatBlock;
			if (fatblock == null)
				return;

			lock_CubeBlocks.AcquireExclusive();
			try
			{
				// by type
				MyObjectBuilderType myOBtype = fatblock.BlockDefinition.TypeId;
				ListSnapshots<IMyCubeBlock> setBlocks_Type;
				if (!CubeBlocks_Type.TryGetValue(myOBtype, out setBlocks_Type))
				{
					setBlocks_Type = new ListSnapshots<IMyCubeBlock>();
					CubeBlocks_Type.Add(myOBtype, setBlocks_Type);
				}
				setBlocks_Type.mutable().Add(fatblock);

				// by definition
				IMyTerminalBlock asTerm = fatblock as IMyTerminalBlock;
				if (asTerm == null)
					return;

				DefinitionType.TrySet(asTerm.DefinitionDisplayNameText, myOBtype);
				TerminalBlocks++;
			}
			catch (Exception e) { myLogger.alwaysLog("Exception: " + e, Logger.severity.ERROR); }
			finally { lock_CubeBlocks.ReleaseExclusive(); }
		}

		private void CubeGrid_OnBlockRemoved(IMySlimBlock obj)
		{
			IMyCubeBlock fatblock = obj.FatBlock;
			if (fatblock == null)
				return;

			myLogger.debugLog("block removed: " + obj.getBestName());
			myLogger.debugLog("block removed: " + obj.FatBlock.DefinitionDisplayNameText + "/" + obj.getBestName());
			lock_CubeBlocks.AcquireExclusive();
			try
			{
				// by type
				MyObjectBuilderType myOBtype = fatblock.BlockDefinition.TypeId;
				ListSnapshots<IMyCubeBlock> setBlocks_Type;
				if (!CubeBlocks_Type.TryGetValue(myOBtype, out setBlocks_Type))
				{
					myLogger.debugLog("failed to get list of type: " + myOBtype);
					return;
				}
				if (setBlocks_Type.Count == 1)
					CubeBlocks_Type.Remove(myOBtype);
				else
					setBlocks_Type.mutable().Remove(fatblock);

				// by definition
				IMyTerminalBlock asTerm = obj.FatBlock as IMyTerminalBlock;
				if (asTerm != null)
					TerminalBlocks--;
			}
			catch (Exception e) { myLogger.alwaysLog("Exception: " + e, Logger.severity.ERROR); }
			finally { lock_CubeBlocks.ReleaseExclusive(); }
			myLogger.debugLog("leaving CubeGrid_OnBlockRemoved(): " + obj.getBestName());
		}

		/// <returns>an immutable read only list or null if there are no blocks of type T</returns>
		public ReadOnlyList<IMyCubeBlock> GetBlocksOfType(MyObjectBuilderType objBuildType)
		{
			//myLogger.debugLog("looking up type " + objBuildType, "GetBlocksOfType<T>()");
			using (lock_CubeBlocks.AcquireSharedUsing())
			{
				if (CubeBlocks_Type == null)
					return null;
				ListSnapshots<IMyCubeBlock> value;
				if (CubeBlocks_Type.TryGetValue(objBuildType, out value))
					return value.immutable();
				return null;
			}
		}

		/// <summary>
		/// Return the number of blocks of the given type.
		/// </summary>
		/// <param name="objBuildType">Type to search for</param>
		/// <returns>The number of blocks of the given type</returns>
		public int CountByType(MyObjectBuilderType objBuildType)
		{
			lock_CubeBlocks.AcquireShared();
			try
			{
				ListSnapshots<IMyCubeBlock> value;
				if (CubeBlocks_Type.TryGetValue(objBuildType, out value))
					return value.Count;
				return 0;
			}
			finally { lock_CubeBlocks.ReleaseShared(); }
		}

		/// <summary>
		/// Count the number of blocks that match a particular condition.
		/// </summary>
		/// <param name="objBuildType">Type to search for</param>
		/// <param name="condition">Condition that block must match</param>
		/// <returns>The number of blocks of the given type that match the condition.</returns>
		public int CountByType(MyObjectBuilderType objBuildType, Func<IMyCubeBlock, bool> condition, int stopCaringAt = int.MaxValue)
		{
			using (lock_CubeBlocks.AcquireSharedUsing())
			{
				ListSnapshots<IMyCubeBlock> value;
				if (CubeBlocks_Type.TryGetValue(objBuildType, out value))
				{
					int count = 0;
					foreach (IMyCubeBlock block in value.myList)
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
		}

		/// <summary>
		/// Return the total number of blocks cached by type.
		/// </summary>
		/// <returns>The total number of blocks cached by type</returns>
		public int TotalByType()
		{
			lock_CubeBlocks.AcquireShared();
			try
			{
				int count = 0;
				foreach (var byType in CubeBlocks_Type)
					count += byType.Value.Count;
				return count;
			}
			finally { lock_CubeBlocks.ReleaseShared(); }
		}

		/// <summary>
		/// will return null if grid is closed or CubeGridCache cannot be created
		/// </summary>
		public static CubeGridCache GetFor(IMyCubeGrid grid)
		{
			if (grid.Closed || grid.MarkedForClose)
				return null;

			CubeGridCache value;
				if(	Registrar.TryGetValue(grid.EntityId, out value))
					return value;

			using (lock_constructing.AcquireExclusiveUsing())
			{
				if (Registrar.TryGetValue(grid.EntityId, out value))
					return value;

				try
				{ return new CubeGridCache(grid); }
				catch (Exception e)
				{
					(new Logger(null, "CubeGridCache")).alwaysLog("Exception on creation: " + e, Logger.severity.WARNING);
					return null;
				}
			}
		}

	}
}
