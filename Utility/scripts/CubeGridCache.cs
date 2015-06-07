#define LOG_ENABLED //remove on build

using System;
using System.Collections.Generic;
using Sandbox.ModAPI;
using VRage;
using VRage.ModAPI;
using VRage.ObjectBuilders;

namespace Rynchodon
{
	/// <summary>
	/// A better way to get terminal blocks of type from a grid.
	/// </summary>
	public class CubeGridCache
	{
		private static Dictionary<IMyCubeGrid, CubeGridCache> registry = new Dictionary<IMyCubeGrid, CubeGridCache>();
		private static FastResourceLock lock_registry = new FastResourceLock();

		private static List<string> knownDefinitions = new List<string>();
		private static FastResourceLock lock_knownDefinitions = new FastResourceLock();

		private Dictionary<MyObjectBuilderType, ListSnapshots<IMyTerminalBlock>> CubeBlocks_Type = new Dictionary<MyObjectBuilderType, ListSnapshots<IMyTerminalBlock>>();
		private Dictionary<string, ListSnapshots<IMyTerminalBlock>> CubeBlocks_Definition = new Dictionary<string, ListSnapshots<IMyTerminalBlock>>();
		private FastResourceLock lock_CubeBlocks = new FastResourceLock();

		private readonly IMyCubeGrid CubeGrid;

		private CubeGridCache(IMyCubeGrid grid)
		{
			myLogger = new Logger("CubeGridCache", () => grid.DisplayName);
			CubeGrid = grid;
			List<IMySlimBlock> allSlims = new List<IMySlimBlock>();
			CubeGrid.GetBlocks_Safe(allSlims, slim => slim.FatBlock is IMyTerminalBlock);

			foreach (IMySlimBlock slim in allSlims)
				CubeGrid_OnBlockAdded(slim);

			CubeGrid.OnBlockAdded += CubeGrid_OnBlockAdded;
			CubeGrid.OnBlockRemoved += CubeGrid_OnBlockRemoved;
			CubeGrid.OnClose += CubeGrid_OnClose;

			registry.Add(CubeGrid, this); // protected by GetFor()
			myLogger.debugLog("built for: " + CubeGrid.DisplayName, ".ctor()", Logger.severity.DEBUG);
		}

		private void CubeGrid_OnClose(IMyEntity grid)
		{
			CubeGrid.OnBlockAdded -= CubeGrid_OnBlockAdded;
			CubeGrid.OnBlockRemoved -= CubeGrid_OnBlockRemoved;
			CubeGrid.OnClose -= CubeGrid_OnClose;

			CubeBlocks_Type = null;
			CubeBlocks_Definition = null;

			using (lock_registry.AcquireExclusiveUsing())
				registry.Remove(CubeGrid);
		}

		private void addKnownDefinition(string definition)
		{
			bool definitionIsKnown;
			using (lock_knownDefinitions.AcquireSharedUsing())
				definitionIsKnown = knownDefinitions.Contains(definition);
			if (!definitionIsKnown)
				using (lock_knownDefinitions.AcquireExclusiveUsing())
					knownDefinitions.Add(definition);
		}

		private void CubeGrid_OnBlockAdded(IMySlimBlock obj)
		{
			IMyTerminalBlock asTerm = obj.FatBlock as IMyTerminalBlock;
			if (asTerm == null)
				return; // only track IMyTerminalBlock

			lock_CubeBlocks.AcquireExclusive();
			try
			{
				MyObjectBuilderType myOBtype = asTerm.BlockDefinition.TypeId;
				string definition = asTerm.DefinitionDisplayNameText;

				ListSnapshots<IMyTerminalBlock> setBlocks_Type;
				ListSnapshots<IMyTerminalBlock> setBlocks_Def;
				if (!CubeBlocks_Type.TryGetValue(myOBtype, out setBlocks_Type))
				{
					setBlocks_Type = new ListSnapshots<IMyTerminalBlock>();
					CubeBlocks_Type.Add(myOBtype, setBlocks_Type);
				}
				if (!CubeBlocks_Definition.TryGetValue(definition, out setBlocks_Def))
				{
					setBlocks_Def = new ListSnapshots<IMyTerminalBlock>();
					CubeBlocks_Definition.Add(definition, setBlocks_Def);
					addKnownDefinition(definition);
				}

				setBlocks_Type.mutable().Add(asTerm);
				setBlocks_Def.mutable().Add(asTerm);
			}
			catch (Exception e) { myLogger.alwaysLog("Exception: " + e, "CubeGrid_OnBlockAdded()", Logger.severity.ERROR); }
			finally { lock_CubeBlocks.ReleaseExclusive(); }
		}

		private void CubeGrid_OnBlockRemoved(IMySlimBlock obj)
		{
			IMyTerminalBlock asTerm = obj.FatBlock as IMyTerminalBlock;
			if (asTerm == null)
				return; // only track IMyTerminalBlock

			lock_CubeBlocks.AcquireExclusive();
			try
			{
				MyObjectBuilderType myOBtype = asTerm.BlockDefinition.TypeId;
				string definition = asTerm.DefinitionDisplayNameText;

				ListSnapshots<IMyTerminalBlock> setBlocks_Type = CubeBlocks_Type[myOBtype];
				ListSnapshots<IMyTerminalBlock> setBlocks_Def = CubeBlocks_Definition[definition];

				setBlocks_Type.mutable().Remove(asTerm);
				setBlocks_Def.mutable().Remove(asTerm);
			}
			catch (Exception e) { myLogger.alwaysLog("Exception: " + e, "CubeGrid_OnBlockAdded()", Logger.severity.ERROR); }
			finally { lock_CubeBlocks.ReleaseExclusive(); }
		}

		/// <summary>
		/// 
		/// </summary>
		/// <typeparam name="T"></typeparam>
		/// <returns>an immutable read only list or null if there are no blocks of type T</returns>
		/// TODO: return IMySlimBlock
		public ReadOnlyList<IMyTerminalBlock> GetBlocksOfType(MyObjectBuilderType objBuildType)
		{
			//myLogger.debugLog("looking up type " + objBuildType, "GetBlocksOfType<T>()");
			using (lock_CubeBlocks.AcquireSharedUsing())
			{
				ListSnapshots<IMyTerminalBlock> value;
				if (CubeBlocks_Type.TryGetValue(objBuildType, out value))
					return value.immutable();
				return null;
			}
		}

		/// <summary>
		/// 
		/// </summary>
		/// <param name="definition"></param>
		/// <returns>an immutable read only list or null if there are no blocks matching definition</returns>
		public ReadOnlyList<IMyTerminalBlock> GetBlocksByDefinition(string definition)
		{
			if (definition == null)
				return null;

			lock_CubeBlocks.AcquireShared();
			using (lock_CubeBlocks.AcquireSharedUsing())
			{
				ListSnapshots<IMyTerminalBlock> value;
				if (CubeBlocks_Definition.TryGetValue(definition, out value))
					return value.immutable();
				return null;
			}
		}

		/// <summary>
		/// <para>slower than GetBlocksByDefinition(), as contained is compared to each definition.</para>
		/// </summary>
		/// <param name="contained"></param>
		/// <returns>an immutable read only list or null if there are no blocks matching definition</returns>
		public List<ReadOnlyList<IMyTerminalBlock>> GetBlocksByDefLooseContains(string contains)
		{
			lock_CubeBlocks.AcquireShared();
			try
			{
				List<ReadOnlyList<IMyTerminalBlock>> master = new List<ReadOnlyList<IMyTerminalBlock>>();
				foreach (var definition in CubeBlocks_Definition)
					if (definition.Key.looseContains(contains))
						master.Add(definition.Value.immutable());
				return master;
			}
			finally { lock_CubeBlocks.ReleaseShared(); }
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
				ListSnapshots<IMyTerminalBlock> value;
				if (CubeBlocks_Type.TryGetValue(objBuildType, out value))
					return value.Count;
				return 0;
			}
			finally { lock_CubeBlocks.ReleaseShared(); }
		}

		/// <summary>
		/// Return the number of blocks with the given definition
		/// </summary>
		/// <param name="definition">Definition to search for</param>
		/// <returns>The number of blocks with the given definition</returns>
		public int CountByDefinition(string definition)
		{
			lock_CubeBlocks.AcquireShared();
			try
			{
				ListSnapshots<IMyTerminalBlock> value;
				if (CubeBlocks_Definition.TryGetValue(definition, out value))
					return value.Count;
				return 0;
			}
			finally { lock_CubeBlocks.ReleaseShared(); }
		}

		/// <summary>
		/// Return the number of blocks containing the given definition
		/// </summary>
		/// <param name="contains">substring of definition to search for</param>
		/// <returns>The number of blocks containing the given definition</returns>
		public int CountByDefLooseContains(string contains)
		{
			lock_CubeBlocks.AcquireShared();
			try
			{
				int count = 0;
				foreach (var definition in CubeBlocks_Definition)
					if (definition.Key.looseContains(contains))
						count += definition.Value.Count;
				return count;
			}
			finally { lock_CubeBlocks.ReleaseShared(); }
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
		/// Returns the total number of blocks cached by definition.
		/// </summary>
		/// <returns>The total number of blocks cached by definition.</returns>
		public int TotalByDefinition()
		{
			lock_CubeBlocks.AcquireShared();
			try
			{
				int count = 0;
				foreach (var definition in CubeBlocks_Definition)
					count += definition.Value.Count;
				return count;
			}
			finally { lock_CubeBlocks.ReleaseShared(); }
		}

		/// <summary>
		/// will return null if grid is closed or CubeGridCache cannot be created
		/// </summary>
		public static CubeGridCache GetFor(IMyCubeGrid grid)
		{
			if (grid.Closed)
				return null;

			CubeGridCache value;
			using (lock_registry.AcquireSharedUsing())
				if (registry.TryGetValue(grid, out value))
					return value;

			using (lock_registry.AcquireExclusiveUsing())
			{
				if (registry.TryGetValue(grid, out value))
					return value;
				try
				{ return new CubeGridCache(grid); }
				catch (Exception e)
				{
					(new Logger(null, "CubeGridCache")).alwaysLog("Exception on creation: " + e, "GetFor()", Logger.severity.WARNING);
					return null;
				}
			}
		}

		/// <summary>
		/// Get the shortest definition that looseContains(contains).
		/// </summary>
		public string getKnownDefinition(string contains)
		{
			int bestLength = int.MaxValue;
			string bestMatch = null;
			using (lock_knownDefinitions.AcquireSharedUsing())
				foreach (string match in knownDefinitions)
					if (match.looseContains(contains) && match.Length < bestLength)
					{
						bestLength = match.Length;
						bestMatch = match;
					}
			return bestMatch;
		}


		private Logger myLogger;
	}
}
