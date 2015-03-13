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

		private Dictionary<Type, CleanList<IMyTerminalBlock>> CubeBlocks_Type = new Dictionary<Type, CleanList<IMyTerminalBlock>>();
		private Dictionary<string, CleanList<IMyTerminalBlock>> CubeBlocks_Definition = new Dictionary<string, CleanList<IMyTerminalBlock>>();
		private FastResourceLock lock_CubeBlocks = new FastResourceLock();

		private readonly IMyCubeGrid CubeGrid;

		private CubeGridCache(IMyCubeGrid grid)
		{
			CubeGrid = grid;
			List<IMySlimBlock> allSlims = new List<IMySlimBlock>();
			CubeGrid.GetBlocks_Safe(allSlims);
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

			CubeBlocks_Type = null;
			CubeBlocks_Definition = null;

			using (lock_registry.AcquireExclusiveUsing())
				registry.Remove(CubeGrid);
		}

		private void CubeGrid_OnBlockAdded(IMySlimBlock obj)
		{
			lock_CubeBlocks.AcquireExclusive();
			try
			{
				IMyTerminalBlock asTerm = obj.FatBlock as IMyTerminalBlock;
				if (asTerm == null)
					return; // only track IMyTerminalBlock

				Type termType = asTerm.GetType();
				string definition = asTerm.DefinitionDisplayNameText;

				CleanList<IMyTerminalBlock> setBlocks_Type, setBlocks_Def;
				if (!CubeBlocks_Type.TryGetValue(termType, out setBlocks_Type))
				{
					log("new lists for " + asTerm.DefinitionDisplayNameText, "CubeGrid_OnBlockAdded()", Logger.severity.TRACE);
					setBlocks_Type = new CleanList<IMyTerminalBlock>();
					setBlocks_Def = new CleanList<IMyTerminalBlock>();
					CubeBlocks_Type.Add(termType, setBlocks_Type);
				}
				else
				{
					log("got setBlocks_Type " + setBlocks_Type, "CubeGrid_OnBlockAdded()", Logger.severity.TRACE);
					log("grabbing setBlocks_Def for " + asTerm.DefinitionDisplayNameText, "CubeGrid_OnBlockAdded()", Logger.severity.TRACE);
					setBlocks_Def = CubeBlocks_Definition[definition];
				}

				log("checking for dirty lists: " + asTerm.DefinitionDisplayNameText, "CubeGrid_OnBlockAdded()", Logger.severity.TRACE);
				// replace dirty list
				if (!setBlocks_Def.IsClean)
				{
					setBlocks_Def = new CleanList<IMyTerminalBlock>(setBlocks_Def);
					CubeBlocks_Definition[definition] = setBlocks_Def;
				}
				if (!setBlocks_Type.IsClean)
				{
					setBlocks_Type = new CleanList<IMyTerminalBlock>(setBlocks_Type);
					CubeBlocks_Type[termType] = setBlocks_Type;
				}

				log("adding: " + asTerm.DefinitionDisplayNameText, "CubeGrid_OnBlockAdded()", Logger.severity.TRACE);
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

				Type termType = asTerm.GetType();
				string definition = asTerm.DefinitionDisplayNameText;

				CleanList<IMyTerminalBlock> setBlocks_Type = CubeBlocks_Type[termType];
				CleanList<IMyTerminalBlock> setBlocks_Def = CubeBlocks_Definition[definition];

				// replace dirty list
				if (!setBlocks_Def.IsClean)
				{
					setBlocks_Def = new CleanList<IMyTerminalBlock>(setBlocks_Def);
					CubeBlocks_Definition[definition] = setBlocks_Def;
				}
				if (!setBlocks_Type.IsClean)
				{
					setBlocks_Type = new CleanList<IMyTerminalBlock>(setBlocks_Type);
					CubeBlocks_Type[termType] = setBlocks_Type;
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
		/// <returns>a truly read only list; the contents will never be modified</returns>
		public ReadOnlyCollection<IMyTerminalBlock> GetBlocksOfType<T>() where T : IMyTerminalBlock
		{
			using (lock_CubeBlocks.AcquireSharedUsing())
			{
				CleanList<IMyTerminalBlock> value;
				if (CubeBlocks_Type.TryGetValue(typeof(T), out value))
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
		/// <returns>a truly read only list; the contents will never be modified</returns>
		public ReadOnlyCollection<IMyTerminalBlock> GetBlocksByDefinition(string definition)
		{
			using (lock_CubeBlocks.AcquireSharedUsing())
			{
				CleanList<IMyTerminalBlock> value;
				if (CubeBlocks_Definition.TryGetValue(definition, out value))
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
		/// <param name="contained"></param>
		/// <returns>a truly read only list; the contents will never be modified</returns>
		public ReadOnlyCollection<IMyTerminalBlock> GetBlocksByDefLooseContains(string contained)
		{
			using (lock_CubeBlocks.AcquireSharedUsing())
			{
				foreach (string key in CubeBlocks_Definition.Keys)
					if (key.looseContains(contained))
					{
						CleanList<IMyTerminalBlock> value = CubeBlocks_Definition[key];
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
		/// <returns></returns>
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
				if (registry.TryGetValue(grid, out value)) // CubeGridCache created while waiting for exclusive lock
					return value;
				try
				{ return new CubeGridCache(grid); }
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
