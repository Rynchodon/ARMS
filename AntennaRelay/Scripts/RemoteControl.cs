#define LOG_ENABLED //remove on build

using System;
using System.Collections.Generic;
using System.Linq;
//using System.Text;

using Sandbox.Common;
using Sandbox.Common.Components;
using Sandbox.Common.ObjectBuilders;
using Sandbox.ModAPI;
using Ingame = Sandbox.ModAPI.Ingame;
using Sandbox.ModAPI.Interfaces;
using VRage;

namespace Rynchodon.AntennaRelay
{
	/// <summary>
	/// Keeps track of transmissions for a remote control. A remote control cannot relay, so it should only receive messages for iteself.
	/// </summary>
	public class RemoteControl :  Receiver
	{
		internal static Dictionary<IMyCubeBlock, RemoteControl> registry = new Dictionary<IMyCubeBlock, RemoteControl>();

		private Ingame.IMyRemoteControl myRemote;
		private Logger myLogger;

		public RemoteControl(IMyCubeBlock block)
			: base(block)
		{
			myLogger = new Logger("RemoteControl", () => CubeBlock.CubeGrid.DisplayName);
			myRemote = CubeBlock as Ingame.IMyRemoteControl;
			registry.Add(CubeBlock, this);

			//log("init as remote: " + CubeBlock.BlockDefinition.SubtypeName, "Init()", Logger.severity.TRACE);

			// for my German friends...
			if (!myRemote.DisplayNameText.Contains('[') && !myRemote.DisplayNameText.Contains(']'))
				myRemote.SetCustomName(myRemote.DisplayNameText + " []");
		}

		protected override void Close(IMyEntity entity)
		{
			try
			{
				if (CubeBlock != null && registry.ContainsKey(CubeBlock))
					registry.Remove(CubeBlock);
			}
			catch (Exception e)
			{ myLogger.log("exception on removing from registry: " + e, "Close()", Logger.severity.WARNING); }
			CubeBlock = null;
			myRemote = null;
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

		public static bool TryGet(IMyCubeBlock block, out RemoteControl result)
		{ return registry.TryGetValue(block, out result); }
	}
}
