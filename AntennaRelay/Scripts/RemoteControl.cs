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
	[MyEntityComponentDescriptor(typeof(MyObjectBuilder_RemoteControl))]
	public class RemoteControl :  Receiver
	{
		internal static Dictionary<IMyCubeBlock, RemoteControl> registry = new Dictionary<IMyCubeBlock, RemoteControl>();

		private Ingame.IMyRemoteControl myRemote;

		protected override void DelayedInit()
		{
			base.DelayedInit();
			myRemote = Entity as Ingame.IMyRemoteControl;
			registry.Add(CubeBlock, this);
			ClassName = "RemoteControl";

			//log("init as remote: " + CubeBlock.BlockDefinition.SubtypeName, "Init()", Logger.severity.TRACE);

			// for my German friends...
			if (!myRemote.DisplayNameText.Contains('[') && !myRemote.DisplayNameText.Contains(']'))
				myRemote.SetCustomName(myRemote.DisplayNameText + " []");
		}

		public override void Close()
		{
			try
			{
				if (CubeBlock != null && registry.ContainsKey(CubeBlock))
					registry.Remove(CubeBlock);
			}
			catch (Exception e)
			{ alwaysLog("exception on removing from registry: " + e, "Close()", Logger.severity.WARNING); }
			CubeBlock = null;
			myRemote = null;
			MyObjectBuilder = null;
		}

		public IEnumerator<LastSeen> lastSeenEnumerator()
		{
			LinkedList<LastSeen> removeList = new LinkedList<LastSeen>();
			foreach (LastSeen seen in myLastSeen.Values)
				if (!seen.isValid)
					removeList.AddLast(seen);
			foreach (LastSeen seen in removeList)
				myLastSeen.Remove(seen.Entity.EntityId);

			log("enumerator has " + myLastSeen.Count + " values", "lastSeenEnumerator()", Logger.severity.TRACE);
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
			log("sorted " + sorted.Count + " messages", "popMessageOrderedByDate()", Logger.severity.TRACE);
			return sorted;
		}

		// isValid = false, set by Receiver.sendToAttached()
		///// <summary>
		///// does not check mes for received, or correct destination. Does set mes.isValid = false
		///// </summary>
		///// <param name="mes"></param>
		//public override void receive(Message mes)
		//{
		//	mes.isValid = false; // final dest
		//	base.receive(mes);
		//}

		public static bool TryGet(IMyCubeBlock block, out RemoteControl result)
		{ return registry.TryGetValue(block, out result); }

		//public bool lastSeenByEntity(IMyEntity key, out LastSeen result)
		//{ return myLastSeen.TryGetValue(key, out result); }

		//public LastSeen lastSeenByEntity(IMyEntity key)
		//{ return myLastSeen[key]; }

		//public override string ToString()
		//{ return CubeBlock.CubeGrid.DisplayName + "-" + myRemote.DisplayNameText; }

		//private Logger myLogger;
		//[System.Diagnostics.Conditional("LOG_ENABLED")]
		//private void log(string toLog, string method = null, Logger.severity level = Logger.severity.DEBUG)
		//{ alwaysLog(toLog, method, level); }
		//protected override void alwaysLog(string toLog, string method = null, Logger.severity level = Logger.severity.DEBUG)
		//{
		//	if (myLogger == null)
		//		myLogger = new Logger(CubeBlock.CubeGrid.DisplayName, "AR.RemoteControl");
		//	myLogger.log(level, method, toLog, CubeBlock.DisplayNameText);
		//}
	}
}
