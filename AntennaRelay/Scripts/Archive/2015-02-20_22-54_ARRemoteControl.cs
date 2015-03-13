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
	public class ARRemoteControl :  Receiver
	{
		internal static Dictionary<IMyCubeBlock, ARRemoteControl> registry = new Dictionary<IMyCubeBlock, ARRemoteControl>();

		internal IMyCubeBlock CubeBlock { get; private set; }
		private Ingame.IMyRemoteControl myRemote;

		protected override void DelayedInit()
		{
			base.DelayedInit();
			CubeBlock = Entity as IMyCubeBlock;
			myRemote = Entity as Ingame.IMyRemoteControl;
			registry.Add(CubeBlock, this);

			log("init as remote: " + CubeBlock.BlockDefinition.SubtypeName, "Init()", Logger.severity.TRACE);
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
			log("enumerator has " + myLastSeen.Count + " values", "lastSeenEnumerator()", Logger.severity.TRACE);
			return myLastSeen.Values.GetEnumerator();
		}

		private HashSet<Message> myMessage = new HashSet<Message>();

		/// <summary>
		///  this will clear all Message, be sure to process them all
		/// </summary>
		/// <returns></returns>
		public List<Message> popMessageOrderedByDate()
		{
			List<Message> sorted = myMessage.OrderBy(m => m.created).ToList();
			myMessage = new HashSet<Message>();
			log("sorted " + sorted.Count + " messages", "popMessageOrderedByDate()", Logger.severity.TRACE);
			return sorted;
		}

		/// <summary>
		/// does not check mes for received, or correct destination. Does set mes.received = true
		/// </summary>
		/// <param name="mes"></param>
		public override void receive(Message mes)
		{
			mes.isValid = false;
			if (myMessage.Contains(mes))
				return;
			myMessage.Add(mes);
			log("got a new message: " + mes.Content + ", count is now " + myMessage.Count, "receive()", Logger.severity.TRACE);
		}

		public static bool TryGet(IMyCubeBlock block, out ARRemoteControl result)
		{ return registry.TryGetValue(block, out result); }


		public override string ToString()
		{ return CubeBlock.CubeGrid.DisplayName + "-" + myRemote.DisplayNameText; }

		private Logger myLogger;
		[System.Diagnostics.Conditional("LOG_ENABLED")]
		private void log(string toLog, string method = null, Logger.severity level = Logger.severity.DEBUG)
		{ alwaysLog(toLog, method, level); }
		protected override void alwaysLog(string toLog, string method = null, Logger.severity level = Logger.severity.DEBUG)
		{
			if (myLogger == null)
				myLogger = new Logger(CubeBlock.CubeGrid.DisplayName, "RemoteControl");
			myLogger.log(level, method, toLog, CubeBlock.DisplayNameText);
		}
	}
}
