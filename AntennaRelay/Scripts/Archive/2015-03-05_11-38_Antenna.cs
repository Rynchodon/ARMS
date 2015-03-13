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
	public class Antenna : Receiver
	{
		internal static LinkedList<Antenna> registry = new LinkedList<Antenna>(); // to iterate over

		private Ingame.IMyRadioAntenna myRadioAntenna;

		private IMyCubeBlock TextPanel;

		protected override void DelayedInit()
		{
			base.DelayedInit();
			myRadioAntenna = Entity as Ingame.IMyRadioAntenna;
			registry.AddLast(this);

			//myLastSeen = new Dictionary<IMyEntity, LastSeen>();
			//myMessage = new HashSet<Message>();

			log("init as antenna: " + CubeBlock.BlockDefinition.SubtypeName, "Init()", Logger.severity.TRACE);
			EnforcedUpdate = MyEntityUpdateEnum.EACH_100TH_FRAME;
		}

		//private bool isClosed = false;
		public override void Close()
		{
			try
			{
				if (CubeBlock != null && registry.Contains(this))
					registry.Remove(this);
			}
			catch (Exception e)
			{ alwaysLog("exception on removing from registry: " + e, "Close()", Logger.severity.WARNING); }
			CubeBlock = null;
			myRadioAntenna = null;
			MyObjectBuilder = null;
			myLastSeen = null;
			myMessages = null;
			//isClosed = true;
		}

		public override void UpdateAfterSimulation100()
		{
			if (!IsInitialized) return;
			if (Closed) return;
			try
			{
				if (!myRadioAntenna.IsWorking)
					return;

				Showoff.doShowoff(CubeBlock, myLastSeen.Values.GetEnumerator(), myLastSeen.Count);

				LinkedList<Antenna> canSeeMe = new LinkedList<Antenna>(); // enemies list (also neutral)
				LinkedList<Antenna> listCanSendTo = new LinkedList<Antenna>(); // friends list

				// antennas in broadcast range
				float radiusSquared;
				MyObjectBuilder_RadioAntenna antBuilder = CubeBlock.GetObjectBuilderCubeBlock() as MyObjectBuilder_RadioAntenna;
				if (!antBuilder.EnableBroadcasting)
					radiusSquared = 0;
				else 
					radiusSquared = myRadioAntenna.Radius * myRadioAntenna.Radius;
				foreach (Antenna ant in registry)
					if (CubeBlock.canSendTo(ant.CubeBlock, radiusSquared, true))
					{
						if (CubeBlock.canConsiderFriendly(ant.CubeBlock))
							listCanSendTo.AddLast(ant);
						else
							canSeeMe.AddLast(ant);
					}

				// remotes attached
				LinkedList<Receiver> attachedReceiver = new LinkedList<Receiver>();
				foreach (KeyValuePair<IMyCubeBlock, ARRemoteControl> pair in ARRemoteControl.registry)
					if (CubeBlock.canSendTo(pair.Key))
						attachedReceiver.AddLast(pair.Value);
				foreach (KeyValuePair<IMyCubeBlock, ProgrammableBlock> pair in ProgrammableBlock.registry)
					if (CubeBlock.canSendTo(pair.Key))
						attachedReceiver.AddLast(pair.Value);

				//log("checking counts", "UpdateAfterSimulation100()", Logger.severity.TRACE);
				if (canSeeMe.Count != 0 || listCanSendTo.Count != 0)
					sendTransmission(canSeeMe, listCanSendTo, attachedReceiver);
			}
			catch (Exception e)
			{ alwaysLog("Exception: " + e, "UpdateAfterSimulation100()", Logger.severity.ERROR); }
		}

		///// <summary>
		///// does not check mes for received
		///// </summary>
		///// <param name="mes"></param>
		//public override void receive(Message mes)
		//{
		//	if (myMessages.Contains(mes))
		//		return;
		//	myMessages.AddLast(mes);
		//	log("got a new message: " + mes.Content, "receive()", Logger.severity.TRACE);
		//}

		private void sendTransmission(LinkedList<Antenna> canSeeMe, LinkedList<Antenna> listCanSendTo, LinkedList<Receiver> canSendToReceiver)
		{
			//log("sending to " + canSeeMe.Count + " enemies, " + listCanSendTo.Count + " friendly antennae, and " + canSendToRemote.Count+" attached remotes", "sendTransmission()", Logger.severity.TRACE);

			LastSeen self = new LastSeen(CubeBlock.CubeGrid);
			this.receive(self, true); // can see self, will send to friends
			foreach (Antenna enemy in canSeeMe)
				enemy.receive(self);

			if (listCanSendTo.Count == 0 && canSendToReceiver.Count == 0) // no friends in range
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
				{
					//log("sending seen for " + seen.Entity.getBestName(), "sendTransmission()", Logger.severity.TRACE);
					foreach (Receiver receiver in canSendToReceiver)
						receiver.receive(seen);
					foreach (Antenna ant in listCanSendTo)
						ant.receive(seen);
				}
			foreach (LastSeen seen in removeLastSeen)
				myLastSeen.Remove(seen.Entity);

			// send out messages
			LinkedList<Message> removeMessage = new LinkedList<Message>();
			foreach (Message mes in myMessages)
				if (!mes.isValid)
					removeMessage.AddLast(mes);
				else // message is valid
				{
					//log("sending message out: " + mes.Content, "sendTransmission()", Logger.severity.TRACE);
					foreach (Receiver receiver in canSendToReceiver)
						if (receiver.CubeBlock == mes.DestCubeBlock)
						{
							receiver.receive(mes);
							log("message delivered to " + receiver.CubeBlock.gridBlockName(), "sendTransmission()", Logger.severity.DEBUG);
							break;
						}
					foreach (Antenna ant in listCanSendTo)
						ant.receive(mes);
				}
			foreach (Message mes in removeMessage)
				myMessages.Remove(mes);
		}


		public override string ToString()
		{ return CubeBlock.CubeGrid.DisplayName + "-" + myRadioAntenna.DisplayNameText; }

		private Logger myLogger;
		[System.Diagnostics.Conditional("LOG_ENABLED")]
		protected override void log(string toLog, string method = null, Logger.severity level = Logger.severity.DEBUG)
		{ alwaysLog(toLog, method, level); }
		protected override void alwaysLog(string toLog, string method = null, Logger.severity level = Logger.severity.DEBUG)
		{
			if (myLogger == null)
				myLogger = new Logger(CubeBlock.CubeGrid.DisplayName, "Antenna");
			myLogger.log(level, method, toLog, CubeBlock.DisplayNameText);
		}
	}
}
