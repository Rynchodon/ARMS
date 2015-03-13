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

		private IMyCubeBlock CubeBlock;
		private Ingame.IMyRemoteControl myRemote;

		public override void Init(MyObjectBuilder_EntityBase objectBuilder)
		{
			CubeBlock = Entity as IMyCubeBlock;
			myRemote = Entity as Ingame.IMyRemoteControl;
			registry.Add(CubeBlock, this);
			builder = objectBuilder;

			//myLastSeen = new Dictionary<IMyEntity, LastSeen>();
			//myMessage = new HashSet<Message>();
		}

		public override void Close()
		{
			registry.Remove(CubeBlock);
			CubeBlock = null;
			myRemote = null;
			builder = null;
		}

		public IEnumerator<LastSeen> lastSeenEnumerator()
		{ return myLastSeen.Values.GetEnumerator(); }

		/// <summary>
		///  this will clear all Message, be sure to process them all
		/// </summary>
		/// <returns></returns>
		public List<Message> popMessageOrderedByDate()
		{
			List<Message> sorted = myMessage.OrderBy(m => m.created).ToList();
			myMessage = new HashSet<Message>();
			return sorted;
		}

		//internal Dictionary<IMyEntity, LastSeen> myLastSeen { get; set; }
		private HashSet<Message> myMessage = new HashSet<Message>();

		public override void receive(LastSeen seen)
		{
			base.receive(seen);
			log("now aware of " + myLastSeen.Count+" entities", "receive()", Logger.severity.TRACE);
		}

		/// <summary>
		/// does not check mes for received, or correct destination
		/// </summary>
		/// <param name="mes"></param>
		public override void receive(Message mes)
		{
			if (myMessage.Contains(mes))
				return;
			myMessage.Add(mes);
			mes.received = true;
			log("got a new message: " + mes.Content, "receive()", Logger.severity.TRACE);
		}



		private MyObjectBuilder_EntityBase builder;
		public override MyObjectBuilder_EntityBase GetObjectBuilder(bool copy = false)
		{ return builder; }

		public override string ToString()
		{ return CubeBlock.CubeGrid.DisplayName + "-" + myRemote.DisplayNameText; }

		private Logger myLogger;
		[System.Diagnostics.Conditional("LOG_ENABLED")]
		private void log(string toLog, string method = null, Logger.severity level = Logger.severity.DEBUG)
		{ alwaysLog(toLog, method, level); }
		private void alwaysLog(string toLog, string method = null, Logger.severity level = Logger.severity.DEBUG)
		{
			if (myLogger == null)
				myLogger = new Logger(CubeBlock.CubeGrid.DisplayName, "RemoteControl");
			myLogger.log(level, method, toLog, CubeBlock.DisplayNameText);
		}
	}
}
