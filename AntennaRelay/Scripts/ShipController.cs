#define LOG_ENABLED //remove on build

using System;
using System.Collections.Generic;
using System.Linq;
using Sandbox.ModAPI;
using Ingame = Sandbox.ModAPI.Ingame;

namespace Rynchodon.AntennaRelay
{
	/// <summary>
	/// Keeps track of transmissions for a remote control. A remote control cannot relay, so it should only receive messages for iteself.
	/// </summary>
	public class ShipController : Receiver
	{
		internal static Dictionary<IMyCubeBlock, ShipController> registry = new Dictionary<IMyCubeBlock, ShipController>();

		private Ingame.IMyShipController myController;
		private Logger myLogger;

		public ShipController(IMyCubeBlock block)
			: base(block)
		{
			myLogger = new Logger("ShipController", () => CubeBlock.CubeGrid.DisplayName);
			myController = CubeBlock as Ingame.IMyShipController;
			registry.Add(CubeBlock, this);

			//log("init as remote: " + CubeBlock.BlockDefinition.SubtypeName, "Init()", Logger.severity.TRACE);

			// for my German friends...
			if (!myController.DisplayNameText.Contains('[') && !myController.DisplayNameText.Contains(']'))
				myController.SetCustomName(myController.DisplayNameText + " []");
		}

		protected override void Close(IMyEntity entity)
		{
			try
			{
				if (CubeBlock != null && registry.ContainsKey(CubeBlock))
					registry.Remove(CubeBlock);
			}
			catch (Exception e)
			{ myLogger.alwaysLog("exception on removing from registry: " + e, "Close()", Logger.severity.WARNING); }
			CubeBlock = null;
			myController = null;
		}

		public IEnumerator<LastSeen> lastSeenEnumerator()
		{
			LinkedList<LastSeen> removeList = new LinkedList<LastSeen>();
			foreach (LastSeen seen in myLastSeen.Values)
				if (!seen.isValid)
					removeList.AddLast(seen);
			foreach (LastSeen seen in removeList)
				myLastSeen.Remove(seen.Entity.EntityId);

			myLogger.debugLog("enumerator has " + myLastSeen.Count + " values", "lastSeenEnumerator()", Logger.severity.TRACE);
			return myLastSeen.Values.GetEnumerator();
		}

		/// <summary>
		///  This will clear all Message, be sure to process them all. All messages will be marked as invalid(received).
		/// </summary>
		/// <returns></returns>
		public List<Message> popMessageOrderedByDate()
		{
			List<Message> sorted = myMessages.OrderBy(m => m.created).ToList();
			myMessages = new LinkedList<Message>();
			myLogger.debugLog("sorted " + sorted.Count + " messages", "popMessageOrderedByDate()", Logger.severity.TRACE);
			return sorted;
		}

		public static bool TryGet(IMyCubeBlock block, out ShipController result)
		{ return registry.TryGetValue(block, out result); }
	}
}
