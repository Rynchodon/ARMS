#define LOG_ENABLED //remove on build

using System;
using System.Collections.Generic;
using Sandbox.Common.ObjectBuilders;
using Sandbox.ModAPI;
using VRage;
using Ingame = Sandbox.ModAPI.Ingame;

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
			CubeGrid = grid;
			List<IMySlimBlock> allSlims = new List<IMySlimBlock>();
			CubeGrid.GetBlocks_Safe(allSlims, slim => slim.FatBlock is IMyTerminalBlock);

			foreach (IMySlimBlock slim in allSlims)
				CubeGrid_OnBlockAdded(slim);

			CubeGrid.OnBlockAdded += CubeGrid_OnBlockAdded;
			CubeGrid.OnBlockRemoved += CubeGrid_OnBlockRemoved;
			CubeGrid.OnClose += CubeGrid_OnClose;

			registry.Add(CubeGrid, this); // protected by GetFor()
			log("built for: " + CubeGrid.DisplayName, ".ctor()", Logger.severity.DEBUG);
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
			catch (Exception e) { alwaysLog("Exception: " + e, "CubeGrid_OnBlockAdded()", Logger.severity.ERROR); }
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
			catch (Exception e) { alwaysLog("Exception: " + e, "CubeGrid_OnBlockAdded()", Logger.severity.ERROR); }
			finally { lock_CubeBlocks.ReleaseExclusive(); }
		}

		/// <summary>
		/// 
		/// </summary>
		/// <typeparam name="T"></typeparam>
		/// <returns>an immutable read only list or null if there are no blocks of type T</returns>
		public ReadOnlyList<IMyTerminalBlock> GetBlocksOfType(MyObjectBuilderType objBuildType)
		{
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
		//{ return GetBlocksByDefinition(getKnownDefinition(contains)); }
		{
			using (lock_CubeBlocks.AcquireSharedUsing())
			{
				List<ReadOnlyList<IMyTerminalBlock>> master = new List<ReadOnlyList<IMyTerminalBlock>>();
				foreach (var definition in CubeBlocks_Definition)
					if (definition.Key.looseContains(contains))
						master.Add(definition.Value.immutable());
				return master;
			}
		}

		///// <summary>
		///// <para>Checks each definition for looseContains(contains)</para>
		///// </summary>
		///// <param name="contains">string to search for</param>
		///// <returns>true if there are any blocks with definitions containing contains</returns>
		//public bool ContainsByDefinitionLooseContains(string contains)
		//{
		//	using (lock_CubeBlocks.AcquireSharedUsing())
		//	{
		//		foreach (var definition in CubeBlocks_Definition)
		//			if (definition.Key.looseContains(contains) && definition.Value.Count > 0)
		//				return true;

		//		return false;
		//	}
		//}

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
		public static string getKnownDefinition(string contains)
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
		[System.Diagnostics.Conditional("LOG_ENABLED")]
		private void log(string toLog, string method = null, Logger.severity level = Logger.severity.DEBUG)
		{ alwaysLog(toLog, method, level); }
		private void alwaysLog(string toLog, string method = null, Logger.severity level = Logger.severity.DEBUG)
		{
			if (myLogger == null)
				myLogger = new Logger(CubeGrid.DisplayName, "CubeGridCache");
			myLogger.log(level, method, toLog);
		}
	}
}
