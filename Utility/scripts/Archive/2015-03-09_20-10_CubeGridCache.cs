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
	/// A better way to get blocks of type from a grid.
	/// </summary>
	[MyEntityComponentDescriptor(typeof(MyObjectBuilder_CubeGrid))]
	public class CubeGridCache : UpdateEnforcer
	{
		// HashMap that using string lookups?
		// DIctionary <string, Heap>?

		private static Dictionary<IMyCubeGrid, CubeGridCache> registry = new Dictionary<IMyCubeGrid, CubeGridCache>();

		private Dictionary<Type, List<IMyTerminalBlock>> CubeBlocks = new Dictionary<Type, List<IMyTerminalBlock>>();

		//private Dictionary<string, List<Ingame.IMyTerminalBlock>> CubeBlocks = new Dictionary<string, List<Ingame.IMyTerminalBlock>>();

		//private HashSet<HashSet<IMyCubeBlock>> CubeBlocks = new HashSet<HashSet<IMyCubeBlock>>();
		private FastResourceLock lock_CubeBlocks = new FastResourceLock();

		private IMyCubeGrid CubeGrid;

		protected override void DelayedInit()
		{
			CubeGrid = Entity as IMyCubeGrid;
			List<IMySlimBlock> allSlims = new List<IMySlimBlock>();
			CubeGrid.GetBlocks_Safe(allSlims);
			foreach (IMySlimBlock slim in allSlims)
				CubeGrid_OnBlockAdded(slim);

			CubeGrid.OnBlockAdded += CubeGrid_OnBlockAdded;
			CubeGrid.OnBlockRemoved += CubeGrid_OnBlockRemoved;
		}

		public override void Close()
		{
			CubeGrid = null;
			CubeBlocks = null;
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

				List<IMyTerminalBlock> setBlocks;
				if (!CubeBlocks.TryGetValue(termType, out setBlocks))
				{
					setBlocks = new List<IMyTerminalBlock>();
					CubeBlocks.Add(termType, setBlocks);
				}

				setBlocks.Add(asTerm);
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

				List<IMyTerminalBlock> setBlocks = CubeBlocks[termType];

				setBlocks.Remove(asTerm);
			}
			catch (Exception e) { alwaysLog("Exception: " + e, "CubeGrid_OnBlockAdded()", Logger.severity.ERROR); }
			finally { lock_CubeBlocks.ReleaseExclusive(); }
		}

		public ReadOnlyCollection<IMyTerminalBlock> GetBlocksOfType<T>() where T : IMyTerminalBlock
		{
			using (lock_CubeBlocks.AcquireSharedUsing())
			{
				List<IMyTerminalBlock> value;
				if (CubeBlocks.TryGetValue(typeof(T), out value))
					return value.AsReadOnly();
				return null;
			}
		}

		/// <summary>
		/// false generally indicates that the Cache is not yet initialized
		/// </summary>
		/// <param name="key"></param>
		/// <param name="value"></param>
		/// <returns></returns>
		public static bool TryGet(IMyCubeGrid key, out CubeGridCache value)
		{ return registry.TryGetValue(key, out value); }


		private Logger myLogger;
		[System.Diagnostics.Conditional("LOG_ENABLED")]
		private void log(string toLog, string method = null, Logger.severity level = Logger.severity.DEBUG)
		{ alwaysLog(toLog, method, level); }
		protected override void alwaysLog(string toLog, string method = null, Logger.severity level = Logger.severity.DEBUG)
		{
			if (myLogger == null)
				myLogger = new Logger(CubeGrid.DisplayName, "CubeGridMonitor");
			myLogger.log(level, method, toLog);
		}
	}
}
