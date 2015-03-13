#define LOG_ENABLED //remove on build

using System;
using System.Collections.Generic;
//using System.Linq;
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
	[MyEntityComponentDescriptor(typeof(MyObjectBuilder_RadioAntenna))]
	public class Antenna : Receiver //, Broadcaster
	{
		internal static LinkedList<Antenna> registry = new LinkedList<Antenna>(); // to iterate over

		internal IMyCubeBlock CubeBlock { get; private set; }
		private Ingame.IMyRadioAntenna myRadioAntenna;

		private MyObjectBuilder_EntityBase builder;
		public override MyObjectBuilder_EntityBase GetObjectBuilder(bool copy = false)
		{ return builder; }

		public override void Init(MyObjectBuilder_EntityBase objectBuilder)
		{
			builder = objectBuilder;
			CubeBlock = Entity as IMyCubeBlock;
			myRadioAntenna = Entity as Ingame.IMyRadioAntenna;
			registry.AddLast(this);

			//myLastSeen = new Dictionary<IMyEntity, LastSeen>();
			//myMessage = new HashSet<Message>();

			Entity.NeedsUpdate |= MyEntityUpdateEnum.EACH_100TH_FRAME;
		}

		//private bool isClosed = false;
		public override void Close()
		{
			registry.Remove(this);
			CubeBlock = null;
			myRadioAntenna = null;
			builder = null;
			myLastSeen = null;
			myMessage = null;
			//isClosed = true;
		}

		private bool wasOff = true;
		private void off()
		{
			if (wasOff)
				return;
			wasOff = true;

			// throw out Transmission
			myLastSeen = new Dictionary<IMyEntity, LastSeen>();
			myMessage = new HashSet<Message>();
		}

		private byte updateCount = 0;

		public override void UpdateAfterSimulation()
		{
			updateCount++;
			if (updateCount >= 100)
			{
				UpdateAfterSimulation100();
				updateCount = 0;
			}
		}

		public override void UpdateAfterSimulation10()
		{
			updateCount += 10;
			if (updateCount >= 100)
			{
				UpdateAfterSimulation100();
				updateCount = 0;
			}
		}

		public override void UpdateAfterSimulation100()
		{
			if (Closed)
				return;

			try
			{
				if (!myRadioAntenna.IsWorking || CubeBlock.OwnerId == 0)
				{
					//log("antenna is off", "UpdateAfterSimulation100()", Logger.severity.TRACE);
					off();
					return;
				}
				//if (wasOff)
				//	log("antenna is now on", "UpdateAfterSimulation100()", Logger.severity.DEBUG);
				wasOff = false;

				LinkedList<Antenna> canSeeMe = new LinkedList<Antenna>(); // enemies list
				LinkedList<Antenna> canSendTo = new LinkedList<Antenna>(); // friends list

				float radiusSquared = myRadioAntenna.Radius * myRadioAntenna.Radius;
				foreach (Antenna ant in registry)
					if (CubeBlock.canSendTo(ant.CubeBlock, radiusSquared))
					{
						if (ant.CubeBlock.OwnerId != 0 && CubeBlock.canConsiderFriendly(ant.CubeBlock)) // unowned antennas could be exploited
							canSendTo.AddLast(ant);
						else
							canSeeMe.AddLast(ant);
					}

				//log("checking counts", "UpdateAfterSimulation100()", Logger.severity.TRACE);
				if (canSeeMe.Count != 0 || canSendTo.Count != 0)
					sendTransmission(canSeeMe, canSendTo);
			}
			catch (Exception e)
			{ alwaysLog("Exception: " + e, "UpdateAfterSimulation100()", Logger.severity.ERROR); }
		}

		//internal Dictionary<IMyEntity, LastSeen> myLastSeen { get; set; }
		private HashSet<Message> myMessage = new HashSet<Message>();

		/// <summary>
		/// does not check mes for received
		/// </summary>
		/// <param name="mes"></param>
		public override void receive(Message mes)
		{
			if (myMessage.Contains(mes))
				return;
			myMessage.Add(mes);
			//recipient.log("got a new message: " + mes.Content, "receive()", Logger.severity.TRACE);
		}

		private void sendTransmission(LinkedList<Antenna> canSeeMe, LinkedList<Antenna> canSendTo)
		{
			//log("sending to " + canSeeMe.Count + " enemies and " + canSendTo.Count + " friends", "sendTransmission()", Logger.severity.TRACE);

			LastSeen self = new LastSeen(CubeBlock.CubeGrid);
			this.receive(self); // can see self
			foreach (Antenna enemy in canSeeMe)
				enemy.receive(self);

			if (canSendTo.Count == 0) // no friends in range
			//{
				//log("no friends in range", "sendTransmission()", Logger.severity.TRACE);
				return;
			//}

			// send out last seen
			LinkedList<LastSeen> removeLastSeen = new LinkedList<LastSeen>();
			foreach (LastSeen seen in myLastSeen.Values)
				if (!seen.isValid)
					removeLastSeen.AddLast(seen);
				else // seen is valid
					foreach (Antenna ant in canSendTo)
						ant.receive(seen);
			foreach (LastSeen seen in removeLastSeen)
				myLastSeen.Remove(seen.Entity);

			// send out messages
			LinkedList<Message> removeMessage = new LinkedList<Message>();
			foreach (Message mes in myMessage)
				if (mes.received)
					removeMessage.AddLast(mes);
				else // message not received
					foreach (Antenna ant in canSendTo)
						ant.receive(mes);
			foreach (Message mes in removeMessage)
				myMessage.Remove(mes);
		}



		public override string ToString()
		{ return CubeBlock.CubeGrid.DisplayName + "-" + myRadioAntenna.DisplayNameText; }

		private Logger myLogger;
		[System.Diagnostics.Conditional("LOG_ENABLED")]
		private void log(string toLog, string method = null, Logger.severity level = Logger.severity.DEBUG)
		{ alwaysLog(toLog, method, level); }
		private void alwaysLog(string toLog, string method = null, Logger.severity level = Logger.severity.DEBUG)
		{
			if (myLogger == null)
				myLogger = new Logger(CubeBlock.CubeGrid.DisplayName, "Antenna");
			myLogger.log(level, method, toLog, CubeBlock.DisplayNameText);
		}
	}
}
