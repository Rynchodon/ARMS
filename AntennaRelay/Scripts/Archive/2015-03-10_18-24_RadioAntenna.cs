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
	public class RadioAntenna : Receiver
	{
		internal static LinkedList<RadioAntenna> registry = new LinkedList<RadioAntenna>(); // to iterate over

		private Ingame.IMyRadioAntenna myRadioAntenna;

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

		public override void Close()
		{
			base.Close();
			try
			{ registry.Remove(this); }
			catch (Exception e)
			{ alwaysLog("exception on removing from registry: " + e, "Close()", Logger.severity.WARNING); }
			myRadioAntenna = null;
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

				float radiusSquared;
				MyObjectBuilder_RadioAntenna antBuilder = CubeBlock.GetObjectBuilderCubeBlock() as MyObjectBuilder_RadioAntenna;
				if (!antBuilder.EnableBroadcasting)
					radiusSquared = 0;
				else
					radiusSquared = myRadioAntenna.Radius * myRadioAntenna.Radius;

				// send antenna self to radio antennae
				LinkedList<RadioAntenna> canSeeMe = new LinkedList<RadioAntenna>(); // friend and foe alike
				foreach (RadioAntenna ant in RadioAntenna.registry)
					if (CubeBlock.canSendTo(ant.CubeBlock, false, radiusSquared, true))
						canSeeMe.AddLast(ant);

				LastSeen self = new LastSeen(CubeBlock.CubeGrid);
				foreach (RadioAntenna ant in canSeeMe)
					ant.receive(self);

				// relay information to friendlies
				//LinkedList<Receiver> canSendTo = new LinkedList<Receiver>(); // friends list
				foreach (RadioAntenna ant in registry)
					if (CubeBlock.canSendTo(ant.CubeBlock, true, radiusSquared, true))
					{
						foreach (LastSeen seen in myLastSeen.Values)
							ant.receive(seen);
						foreach (Message mes in myMessages)
							ant.receive(mes);
					}

				Receiver.sendToAttached(CubeBlock, myLastSeen);
				Receiver.sendToAttached(CubeBlock, myMessages);

				//// remotes attached
				//LinkedList<Receiver> attachedReceiver = new LinkedList<Receiver>();
				//foreach (KeyValuePair<IMyCubeBlock, RemoteControl> pair in RemoteControl.registry)
				//	if (CubeBlock.canSendTo(pair.Key))
				//		attachedReceiver.AddLast(pair.Value);
				//foreach (KeyValuePair<IMyCubeBlock, ProgrammableBlock> pair in ProgrammableBlock.registry)
				//	if (CubeBlock.canSendTo(pair.Key))
				//		attachedReceiver.AddLast(pair.Value);

				////log("checking counts", "UpdateAfterSimulation100()", Logger.severity.TRACE);
				//if (canSeeMe.Count != 0 || listCanSendTo.Count != 0)
				//	sendTransmission(canSeeMe, listCanSendTo, attachedReceiver);
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

		//private void sendTransmission(LinkedList<RadioAntenna> canSeeMe, LinkedList<RadioAntenna> listCanSendTo, LinkedList<Receiver> canSendToReceiver)
		//{
		//	//log("sending to " + canSeeMe.Count + " enemies, " + listCanSendTo.Count + " friendly antennae, and " + canSendToRemote.Count+" attached remotes", "sendTransmission()", Logger.severity.TRACE);

		//	LastSeen self = new LastSeen(CubeBlock.CubeGrid);
		//	this.receive(self, true); // can see self, will send to friends
		//	foreach (RadioAntenna enemy in canSeeMe)
		//		enemy.receive(self);

		//	if (listCanSendTo.Count == 0 && canSendToReceiver.Count == 0) // no friends in range
		//	//{
		//		//log("no friends in range", "sendTransmission()", Logger.severity.TRACE);
		//		return;
		//	//}

		//	// send out last seen
		//	LinkedList<LastSeen> removeLastSeen = new LinkedList<LastSeen>();
		//	foreach (LastSeen seen in myLastSeen.Values)
		//		if (!seen.isValid)
		//			removeLastSeen.AddLast(seen);
		//		else // seen is valid
		//		{
		//			//log("sending seen for " + seen.Entity.getBestName(), "sendTransmission()", Logger.severity.TRACE);
		//			foreach (Receiver receiver in canSendToReceiver)
		//				receiver.receive(seen);
		//			foreach (RadioAntenna ant in listCanSendTo)
		//				ant.receive(seen);
		//		}
		//	foreach (LastSeen seen in removeLastSeen)
		//		myLastSeen.Remove(seen.Entity);

		//	// send out messages
		//	LinkedList<Message> removeMessage = new LinkedList<Message>();
		//	foreach (Message mes in myMessages)
		//		if (!mes.isValid)
		//			removeMessage.AddLast(mes);
		//		else // message is valid
		//		{
		//			//log("sending message out: " + mes.Content, "sendTransmission()", Logger.severity.TRACE);
		//			foreach (Receiver receiver in canSendToReceiver)
		//				if (receiver.CubeBlock == mes.DestCubeBlock)
		//				{
		//					receiver.receive(mes);
		//					log("message delivered to " + receiver.CubeBlock.gridBlockName(), "sendTransmission()", Logger.severity.DEBUG);
		//					break;
		//				}
		//			foreach (RadioAntenna ant in listCanSendTo)
		//				ant.receive(mes);
		//		}
		//	foreach (Message mes in removeMessage)
		//		myMessages.Remove(mes);
		//}


		//public override string ToString()
		//{ return CubeBlock.CubeGrid.DisplayName + "-" + myRadioAntenna.DisplayNameText; }

		//private Logger myLogger;
		//[System.Diagnostics.Conditional("LOG_ENABLED")]
		//private void log(string toLog, string method = null, Logger.severity level = Logger.severity.DEBUG)
		//{ alwaysLog(toLog, method, level); }
		//protected override void alwaysLog(string toLog, string method = null, Logger.severity level = Logger.severity.DEBUG)
		//{
		//	if (myLogger == null)
		//		myLogger = new Logger(CubeBlock.CubeGrid.DisplayName, "Antenna");
		//	myLogger.log(level, method, toLog, CubeBlock.DisplayNameText);
		//}
	}
}
