#define LOG_ENABLED //remove on build

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
//using System.Linq;
//using System.Text;

using Sandbox.Common;
using Sandbox.Common.Components;
using Sandbox.Common.ObjectBuilders;
using Sandbox.ModAPI;
using Ingame = Sandbox.ModAPI.Ingame;
using Sandbox.ModAPI.Interfaces;

using VRage;
using VRage.Collections;

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

		private Dictionary<MyObjectBuilderType, CleanList<Ingame.IMyTerminalBlock>> CubeBlocks_Type = new Dictionary<MyObjectBuilderType, CleanList<Ingame.IMyTerminalBlock>>();
		private Dictionary<string, CleanList<Ingame.IMyTerminalBlock>> CubeBlocks_Definition = new Dictionary<string, CleanList<Ingame.IMyTerminalBlock>>();
		private FastResourceLock lock_CubeBlocks = new FastResourceLock();

		private readonly IMyCubeGrid CubeGrid;

		private CubeGridCache(IMyCubeGrid grid, FastResourceLock lock_iterateBlocks)
		{
			CubeGrid = grid;
			List<IMySlimBlock> allSlims = new List<IMySlimBlock>();

			if (lock_iterateBlocks != null)
				lock_iterateBlocks.AcquireShared();
			try { CubeGrid.GetBlocks(allSlims, slim => slim.FatBlock is IMyTerminalBlock); }
			finally
			{
				if (lock_iterateBlocks != null)
					lock_iterateBlocks.ReleaseShared();
			}

			foreach (IMySlimBlock slim in allSlims)
				CubeGrid_OnBlockAdded(slim);

			CubeGrid.OnBlockAdded += CubeGrid_OnBlockAdded;
			CubeGrid.OnBlockRemoved += CubeGrid_OnBlockRemoved;
			CubeGrid.OnClose += CubeGrid_OnClose;

			registry.Add(CubeGrid, this); // protected by GetFor()
			log("built for: " + CubeGrid.DisplayName, ".ctor()", Logger.severity.DEBUG);
		}

		public void CubeGrid_OnClose(IMyEntity grid)
		{
			CubeGrid.OnBlockAdded -= CubeGrid_OnBlockAdded;
			CubeGrid.OnBlockRemoved -= CubeGrid_OnBlockRemoved;
			CubeGrid.OnClose -= CubeGrid_OnClose;

			//CubeBlocks_Type = null;
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

		/// <summary>
		/// Get the shortest definition that looseContains(contains).
		/// </summary>
		/// <param name="contains"></param>
		/// <returns></returns>
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

		private void CubeGrid_OnBlockAdded(IMySlimBlock obj)
		{
			lock_CubeBlocks.AcquireExclusive();
			try
			{
				IMyTerminalBlock asTerm = obj.FatBlock as IMyTerminalBlock;
				if (asTerm == null)
					return; // only track IMyTerminalBlock

				MyObjectBuilderType myOBtype = asTerm.BlockDefinition.TypeId;
				string definition = asTerm.DefinitionDisplayNameText;

				//log("adding " + termType + " : " + definition, "CubeGrid_OnBlockAdded()", Logger.severity.TRACE);

				CleanList<Ingame.IMyTerminalBlock> setBlocks_Type;
				CleanList<Ingame.IMyTerminalBlock> setBlocks_Def;
				if (!CubeBlocks_Type.TryGetValue(myOBtype, out setBlocks_Type))
				{
					//log("new lists for " + asTerm.DefinitionDisplayNameText, "CubeGrid_OnBlockAdded()", Logger.severity.TRACE);
					setBlocks_Type = new CleanList<Ingame.IMyTerminalBlock>();
					CubeBlocks_Type.Add(myOBtype, setBlocks_Type);
				}
				if (!CubeBlocks_Definition.TryGetValue(definition, out setBlocks_Def))
				{
					setBlocks_Def = new CleanList<Ingame.IMyTerminalBlock>();
					CubeBlocks_Definition.Add(definition, setBlocks_Def);
					addKnownDefinition(definition);
				}

				//log("checking for dirty lists: " + asTerm.DefinitionDisplayNameText, "CubeGrid_OnBlockAdded()", Logger.severity.TRACE);
				// replace dirty list
				if (!setBlocks_Def.IsClean)
				{
					setBlocks_Def = new CleanList<Ingame.IMyTerminalBlock>(setBlocks_Def);
					CubeBlocks_Definition[definition] = setBlocks_Def;
				}
				if (!setBlocks_Type.IsClean)
				{
					setBlocks_Type = new CleanList<Ingame.IMyTerminalBlock>(setBlocks_Type);
					CubeBlocks_Type[myOBtype] = setBlocks_Type;
				}

				log("adding: " + asTerm.DefinitionDisplayNameText + ", termType = " + myOBtype, "CubeGrid_OnBlockAdded()", Logger.severity.TRACE);
				setBlocks_Type.Add(asTerm);
				setBlocks_Def.Add(asTerm);
			}
			catch (Exception e) { alwaysLog("Exception: " + e, "CubeGrid_OnBlockAdded()", Logger.severity.ERROR); }
			finally { lock_CubeBlocks.ReleaseExclusive(); }
		}

		private void CubeGrid_OnBlockRemoved(IMySlimBlock obj)
		{
			lock_CubeBlocks.AcquireExclusive();
			try
			{
				IMyTerminalBlock asTerm = obj.FatBlock as IMyTerminalBlock;
				if (asTerm == null)
					return; // only track IMyTerminalBlock

				MyObjectBuilderType myOBtype = asTerm.BlockDefinition.TypeId;
				string definition = asTerm.DefinitionDisplayNameText;

				CleanList<Ingame.IMyTerminalBlock> setBlocks_Type = CubeBlocks_Type[myOBtype];
				CleanList<Ingame.IMyTerminalBlock> setBlocks_Def = CubeBlocks_Definition[definition];

				// replace dirty list
				if (!setBlocks_Def.IsClean)
				{
					setBlocks_Def = new CleanList<Ingame.IMyTerminalBlock>(setBlocks_Def);
					CubeBlocks_Definition[definition] = setBlocks_Def;
				}
				if (!setBlocks_Type.IsClean)
				{
					setBlocks_Type = new CleanList<Ingame.IMyTerminalBlock>(setBlocks_Type);
					CubeBlocks_Type[myOBtype] = setBlocks_Type;
				}

				setBlocks_Type.Remove(asTerm);
				setBlocks_Def.Remove(asTerm);
			}
			catch (Exception e) { alwaysLog("Exception: " + e, "CubeGrid_OnBlockAdded()", Logger.severity.ERROR); }
			finally { lock_CubeBlocks.ReleaseExclusive(); }
		}

		/// <summary>
		/// 
		/// </summary>
		/// <typeparam name="T"></typeparam>
		/// <returns>an immutable read only list or null if there are no blocks of type T</returns>
		public ReadOnlyCollection<Ingame.IMyTerminalBlock> GetBlocksOfType(MyObjectBuilderType objBuildType )
		{
			myLogger.debugLog("looking up type " + objBuildType, "GetBlocksOfType<T>()");
			using (lock_CubeBlocks.AcquireSharedUsing())
			{
				CleanList<Ingame.IMyTerminalBlock> value;
				if (CubeBlocks_Type.TryGetValue(objBuildType, out value))
				{
					value.IsClean = false;
					return value.AsReadOnly();
				}
				return null;
			}
		}

		/// <summary>
		/// 
		/// </summary>
		/// <param name="definition"></param>
		/// <returns>an immutable read only list or null if there are no blocks matching definition</returns>
		public ReadOnlyCollection<Ingame.IMyTerminalBlock> GetBlocksByDefinition(string definition)
		{
			using (lock_CubeBlocks.AcquireSharedUsing())
			{
				CleanList<Ingame.IMyTerminalBlock> value;
				if (CubeBlocks_Definition.TryGetValue(definition, out value))
				{
					value.IsClean = false;
					return value.AsReadOnly();
				}
				return null;
			}
		}

		/// <summary>
		/// Much slower than GetBlocksByDefinition(), as contained is compared to each definition.
		/// </summary>
		/// <param name="contained"></param>
		/// <returns>an immutable read only list or null if there are no blocks matching definition</returns>
		public ReadOnlyCollection<Ingame.IMyTerminalBlock> GetBlocksByDefLooseContains(string contained)
		{
			using (lock_CubeBlocks.AcquireSharedUsing())
			{
				foreach (string key in CubeBlocks_Definition.Keys)
					if (key.looseContains(contained))
					{
						CleanList<Ingame.IMyTerminalBlock> value = CubeBlocks_Definition[key];
						value.IsClean = false;
						return value.AsReadOnly();
					}
				return null;
			}
		}

		/// <summary>
		/// will return null if grid is closed, or CubeGridCache cannot be created
		/// </summary>
		/// <param name="grid"></param>
		/// <param name="lock_iterateBlocks">if not null, will obtain a shared lock if it is necessary to iterate over blocks</param>
		/// <returns></returns>
		public static CubeGridCache GetFor(IMyCubeGrid grid, FastResourceLock lock_iterateBlocks = null)
		{
			if (grid.Closed)
				return null;

			CubeGridCache value;
			using (lock_registry.AcquireSharedUsing())
				if (registry.TryGetValue(grid, out value))
					return value;

			using (lock_registry.AcquireExclusiveUsing())
			{
				if (registry.TryGetValue(grid, out value)) // CubeGridCache created while waiting for exclusive lock
					return value;
				try
				{ return new CubeGridCache(grid, lock_iterateBlocks); }
				catch (Exception e)
				{
					(new Logger(null, "CubeGridCache")).log("Exception on creation: " + e, "GetFor()", Logger.severity.WARNING);
					return null;
				}
			}
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

		private class CleanList<T> : List<T>
		{
			private bool value_IsClean = true;
			public bool IsClean { get { return value_IsClean; } set { value_IsClean &= value; } }

			public CleanList() : base() { }
			public CleanList(List<T> toCopy) : base(toCopy) { }
		}
	}
}
