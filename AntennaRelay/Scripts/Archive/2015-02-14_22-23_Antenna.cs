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
	public class Antenna : MyGameLogicComponent, Broadcaster
	{
		internal static LinkedList<Antenna> registry = new LinkedList<Antenna>(); // to iterate over
		//private HashSet<Antenna> canSendTo = new HashSet<Antenna>();
		//private HashSet<Broadcaster> canSee = new HashSet<Broadcaster>(); // for tracking broadcasters that are not friendly or not antennas

		private IMyCubeBlock CubeBlock;
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
			Entity.NeedsUpdate |= MyEntityUpdateEnum.EACH_100TH_FRAME;
			log("initialized, updating: "+Entity.NeedsUpdate, "Init()", Logger.severity.INFO);
		}

		private bool isClosed = false;
		public override void Close()
		{
			//log("closed, updating: " + Entity.NeedsUpdate, "Init()", Logger.severity.INFO);
			registry.Remove(this);
			off();
			myRadioAntenna = null;
			isClosed = true;
		}

		private bool wasOff = true;
		private void off()
		{
			if (wasOff)
				return;
			wasOff = true;

			// throw out Broadcaster
			//canSendTo = new HashSet<Antenna>();
			//canSee = new HashSet<Broadcaster>();
			// throw out Transmission
			myLastSeen = new Dictionary<IMyEntity, LastSeen>();
			myMessage = new HashSet<Message>();
		}

		//private bool canSendTo_tryAdd(Antenna toAdd)
		//{
		//	if (canSendTo.Contains(toAdd)) // already in set
		//		return false;
		//	canSendTo.Add(toAdd);
		//	log("can send to: " + toAdd, "UpdateAfterSimulation100()", Logger.severity.DEBUG);
		//	return true;
		//}

		//private bool canSendTo_tryRem(Antenna toRem)
		//{
		//	if (!canSendTo.Contains(toRem)) // not in set
		//		return false;
		//	canSendTo.Remove(toRem);
		//	log("can no longer send to: " + toRem, "UpdateAfterSimulation100()", Logger.severity.DEBUG);
		//	return true;
		//}

		//private bool canSee_tryAdd(Broadcaster toAdd)
		//{
		//	if (canSee.Contains(toAdd)) // already in set
		//		return false;
		//	canSee.Add(toAdd);
		//	log("can see: " + toAdd, "UpdateAfterSimulation100()", Logger.severity.DEBUG);
		//	return true;
		//}

		//private bool canSee_tryRem(Broadcaster toRem)
		//{
		//	if (!canSee.Contains(toRem)) // not in set
		//		return false;
		//	canSee.Remove(toRem);
		//	log("can no longer see: " + toRem, "UpdateAfterSimulation100()", Logger.severity.DEBUG);
		//	return true;
		//}

		public override void UpdateAfterSimulation100()
		{
			if (isClosed || Closed)
				return;

			try
			{
				if (!myRadioAntenna.IsWorking)
				{
					log("antenna is off", "UpdateAfterSimulation100()", Logger.severity.TRACE);
					off();
					return;
				}
				if (wasOff)
					log("antenna is now on", "UpdateAfterSimulation100()", Logger.severity.DEBUG);
				wasOff = false;

				LinkedList<Antenna> canSeeMe = new LinkedList<Antenna>(); // enemies list
				LinkedList<Antenna> canSendTo = new LinkedList<Antenna>(); // friends list

				float radiusSquared = myRadioAntenna.Radius * myRadioAntenna.Radius;
				foreach (Antenna ant in registry)
					if (canSendToAnt(CubeBlock, ant, radiusSquared))
					{
						if (isFriendly(CubeBlock, ant.CubeBlock))
							canSendTo.AddLast(ant);
						else
							canSeeMe.AddLast(ant);
					}

				//foreach (Antenna ant in registry)
				//{
				//	if (canSendToAnt(CubeBlock, ant, radiusSquared))
				//	{
				//		ant.canSee_tryAdd(this);
				//		if (isFriendly(CubeBlock, ant.CubeBlock))
				//			canSendTo_tryAdd(ant);
				//		else
				//			canSendTo_tryRem(ant); // might have changed owner
				//	}
				//	else
				//	{
				//		ant.canSee_tryRem(this);
				//		canSendTo_tryRem(ant);
				//	}
				//}
				log("checking counts", "UpdateAfterSimulation100()", Logger.severity.TRACE);
				if (canSeeMe.Count != 0 || canSendTo.Count != 0)
					sendTransmission(canSeeMe, canSendTo);
			}
			catch (Exception e)
			{ alwaysLog("Exception: " + e, "UpdateAfterSimulation100()", Logger.severity.ERROR); }
		}

		public static bool isFriendly(IMyCubeBlock block1, IMyCubeBlock block2)
		{
			switch (block1.GetUserRelationToOwner(block2.OwnerId))
			{
				case MyRelationsBetweenPlayerAndBlock.Enemies:
				case MyRelationsBetweenPlayerAndBlock.Neutral:
					return false;
			}
			return true;
		}

		/// <summary>
		/// tests for sendFrom and sendTo are working and same grid or in radius
		/// </summary>
		/// <param name="sendTo"></param>
		/// <param name="rangeSquared"></param>
		/// <returns></returns>
		public static bool canSendToAnt(IMyCubeBlock sendFrom, Antenna sendTo, float rangeSquared)
		{
			if (!sendFrom.IsWorking || !sendTo.myRadioAntenna.IsWorking)
				return false;

			if (sendFrom.CubeGrid == sendTo.myRadioAntenna.CubeGrid)
				return true;

			double distanceSquared = (sendFrom.GetPosition() - sendTo.myRadioAntenna.GetPosition()).LengthSquared();
			return distanceSquared < rangeSquared;
		}

		// Transmission

		private Dictionary<IMyEntity, LastSeen> myLastSeen = new Dictionary<IMyEntity, LastSeen>();
		private HashSet<Message> myMessage = new HashSet<Message>();

		/// <summary>
		/// does not check seen for isValid
		/// </summary>
		/// <param name="seen"></param>
		public void receive(LastSeen seen)
		{
			LastSeen value;
			if (myLastSeen.TryGetValue(seen.Entity, out value))
			{
				if (value == seen)
					return; // already have this one, ignore
				if (seen.isNewerThan(value))
					myLastSeen.Remove(value.Entity);
				else
					return; // no need to update others
			}
			
			myLastSeen.Add(seen.Entity, seen);
			//log("got a new last seen: " + seen.Entity.DisplayName, "receive()", Logger.severity.TRACE);
		}

		/// <summary>
		/// does not check mes for received
		/// </summary>
		/// <param name="mes"></param>
		public void receive(Message mes)
		{
			if (myMessage.Contains(mes))
				return; // already have this one, ignore
			myMessage.Add(mes);
			log("got a new message: " + mes.Content, "receive()", Logger.severity.DEBUG);
		}

		private FastResourceLock lock_sendTransmission = new FastResourceLock();

		// TODO: time this method for busy space, it could be very expensive
		// might be a good candidate for background, not sure how to lock though
		private void sendTransmission(LinkedList<Antenna> canSeeMe, LinkedList<Antenna> canSendTo)
		{
			DateTime start = DateTime.UtcNow;
			log("function start: " + start, "sendTransmission()", Logger.severity.TRACE);
			if (!lock_sendTransmission.TryAcquireExclusive())
			{
				log("failed to get a lock", "sendTransmission()", Logger.severity.TRACE);
				return;
			}
			try
			{
				LastSeen self = new LastSeen(CubeBlock.CubeGrid);
				receive(self); // can see self
				foreach (Antenna enemy in canSeeMe)
					enemy.receive(self);

				if (canSendTo.Count == 0) // no friends in range
				{
					log("no friends in range", "sendTransmission()", Logger.severity.TRACE);
					return;
				}

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
			catch (Exception e)
			{
				alwaysLog("Exception: " + e, "sendTransmission()", Logger.severity.ERROR);
			}
			finally
			{
				lock_sendTransmission.ReleaseExclusive();
				log("function took: " + (DateTime.UtcNow - start).TotalSeconds + " seconds", "sendTransmission()", Logger.severity.TRACE);
			}
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
