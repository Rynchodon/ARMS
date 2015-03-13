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

		private Dictionary<Type, List<Ingame.IMyTerminalBlock>> CubeBlocks = new Dictionary<Type, List<Ingame.IMyTerminalBlock>>();

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
			using (lock_CubeBlocks.AcquireExclusiveUsing())
			{
				// add block
			}
		}

		private void CubeGrid_OnBlockRemoved(IMySlimBlock obj)
		{
			using (lock_CubeBlocks.AcquireExclusiveUsing())
			{
				// remove block
			}
		}

		public ReadOnlyCollection<Ingame.IMyTerminalBlock> GetBlocksOfType<T>()
		{
			List<Ingame.IMyTerminalBlock> value = new List<Ingame.IMyTerminalBlock>();
			CubeBlocks.TryGetValue(typeof(T), out value);
			return value.AsReadOnly();
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
