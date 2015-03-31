#define LOG_ENABLED //remove on build

using System;
using System.Collections.Generic;
//using System.Linq;
//using System.Text;

using Sandbox.Common.Components;
using Sandbox.ModAPI;
using VRage.Collections;
using VRageMath;

namespace Rynchodon.AntennaRelay
{
	public abstract class Receiver : UpdateEnforcer
	{
		/// <summary>
		/// Track LastSeen objects by entityId
		/// </summary>
		protected Dictionary<long, LastSeen> myLastSeen = new Dictionary<long, LastSeen>();
		public IMyCubeBlock CubeBlock { get; internal set; }
		protected LinkedList<Message> myMessages = new LinkedList<Message>();

		/// <summary>
		/// Do not forget to call this!
		/// </summary>
		protected override void DelayedInit()
		{
			//(new Logger(null, "Receiver")).log("init", "DelayedInit()", Logger.severity.TRACE);
			CubeBlock = Entity as IMyCubeBlock;
			CubeBlock.CubeGrid.OnBlockOwnershipChanged += CubeGrid_OnBlockOwnershipChanged;
			EnemyNear = false;
		}

		public override void Close()
		{
			MyObjectBuilder = null;
			myLastSeen = null;
			CubeBlock = null;
			myMessages = null;
		}

		//public abstract void Init(Sandbox.Common.ObjectBuilders.MyObjectBuilder_EntityBase objectBuilder);

		private void CubeGrid_OnBlockOwnershipChanged(IMyCubeGrid obj)
		{
			if (obj == CubeBlock)
				myMessages = new LinkedList<Message>();
		}

		/// <summary>
		/// does not check mes for isValid
		/// </summary>
		/// <param name="mes"></param>
		public virtual void receive(Message mes)
		{
			if (myMessages.Contains(mes))
				return;
			myMessages.AddLast(mes);
			log("got a new message: " + mes.Content + ", count is now " + myMessages.Count, "receive()", Logger.severity.TRACE);
		}

		/// <summary>
		/// does not check seen for isValid
		/// </summary>
		/// <param name="seen"></param>
		/// <param name="forced">for receiving LastSeen for self</param>
		public void receive(LastSeen seen, bool forced = false)
		{
			if (seen.Entity == CubeBlock.CubeGrid && !forced)
				//{
				//	alwaysLog("do not tell me about myself: ", "receive()", Logger.severity.TRACE);
				return;
			//}

			LastSeen toUpdate;
			if (myLastSeen.TryGetValue(seen.Entity.EntityId, out toUpdate))
			{
				if (seen.update(ref toUpdate))
				{
					//if (toUpdate.Entity.getBestName().looseContains("Leo"))// toUpdate.LastSeenAt.secondsSince() > 3)
					//	log("updating: " + seen.Entity.getBestName(), "receive()", Logger.severity.TRACE);
					myLastSeen.Remove(toUpdate.Entity.EntityId);
					myLastSeen.Add(toUpdate.Entity.EntityId, toUpdate);
				}
				//else
				//	if (toUpdate.Entity.getBestName().looseContains("Leo"))
				//		log("not updating: " + seen.Entity.getBestName(), "receive()", Logger.severity.TRACE);
			}
			else
				//{
				myLastSeen.Add(seen.Entity.EntityId, seen);
			//	log("got a new last seen: " + seen.Entity.DisplayName, "receive()", Logger.severity.TRACE);
			//}
		}

		/// <summary>
		/// Sends LastSeen to attached all attached friendly antennae and to remote controls.
		/// removes invalids from the list
		/// </summary>
		public static void sendToAttached(IMyCubeBlock sender, LinkedList<LastSeen> list)
		{ sendToAttached(sender, list, null); }

		/// <summary>
		/// Sends LastSeen to attached all attached friendly antennae and to remote controls.
		/// removes invalids from the list
		/// </summary>
		public static void sendToAttached(IMyCubeBlock sender, Dictionary<long, LastSeen> dictionary)
		{ sendToAttached(sender, null, dictionary); }

		/// <summary>
		/// Sends LastSeen to attached all attached friendly antennae and to remote controls.
		/// removes invalids from the list
		/// </summary>
		private static void sendToAttached(IMyCubeBlock sender, LinkedList<LastSeen> list = null, Dictionary<long, LastSeen> dictionary = null)
		{
			ICollection<LastSeen> toSend;
			if (list != null)
				toSend = list;
			else
				toSend = dictionary.Values;

			LinkedList<LastSeen> removeList = new LinkedList<LastSeen>();
			foreach (LastSeen seen in toSend)
				if (!seen.isValid)
					removeList.AddLast(seen);
			foreach (LastSeen seen in removeList)
				if (dictionary != null)
					dictionary.Remove(seen.Entity.EntityId);
				else
					toSend.Remove(seen);

			// to radio antenna
			foreach (RadioAntenna radioAnt in RadioAntenna.registry)
				if (sender.canSendTo(radioAnt.CubeBlock, true))
					foreach (LastSeen seen in toSend)
						radioAnt.receive(seen);

			// to laser antenna
			foreach (LaserAntenna laserAnt in LaserAntenna.registry)
				if (sender.canSendTo(laserAnt.CubeBlock, true))
					foreach (LastSeen seen in toSend)
						laserAnt.receive(seen);

			// to remote control
			foreach (RemoteControl remote in RemoteControl.registry.Values)
				if (sender.canSendTo(remote.CubeBlock, true))
					foreach (LastSeen seen in toSend)
						remote.receive(seen);
		}

		/// <summary>
		/// If attached to final destination, send message to it. Otherwise sends Message to all attached friendly antennae.
		/// removes invalids from the list
		/// </summary>
		/// <param name="additional">also sends to this collection, without testing</param>
		public static void sendToAttached(IMyCubeBlock sender, ICollection<Message> toSend)
		{
			LinkedList<Message> removeList = new LinkedList<Message>();
			foreach (Message mes in toSend)
			{
				if (mes.isValid)
				{
					if (AttachedGrids.isGridAttached(sender.CubeGrid, mes.DestCubeBlock.CubeGrid))
					{
						// get receiver for block
						RemoteControl remote;
						if (RemoteControl.registry.TryGetValue(mes.DestCubeBlock, out remote))
						{
							remote.receive(mes);
							mes.isValid = false;
							removeList.AddLast(mes);
						}
						else
						{
							ProgrammableBlock progBlock;
							if (ProgrammableBlock.registry.TryGetValue(mes.DestCubeBlock, out progBlock))
							{
								progBlock.receive(mes);
								mes.isValid = false;
								removeList.AddLast(mes);
							}
						}
					}
				}
				else // not valid
					removeList.AddLast(mes);
			}
			foreach (Message mes in removeList)
				toSend.Remove(mes);

			// to radio antenna
			foreach (RadioAntenna radioAnt in RadioAntenna.registry)
				if (sender.canSendTo(radioAnt.CubeBlock, true))
					foreach (Message mes in toSend)
						radioAnt.receive(mes);

			// to laser antenna
			foreach (LaserAntenna laserAnt in LaserAntenna.registry)
				if (sender.canSendTo(laserAnt.CubeBlock, true))
					foreach (Message mes in toSend)
						laserAnt.receive(mes);
		}

		/// <summary>
		/// Is there any enemy within 10km, seen in the past 10 seconds? Not all Receiver update this; for Receiver that do not update, false.
		/// </summary>
		public bool EnemyNear { get; private set; }

		protected void UpdateEnemyNear()
		{
			Vector3D myPosition = CubeBlock.GetPosition();
			EnemyNear = false;
			foreach (LastSeen seen in myLastSeen.Values)
				if (seen.isRecent() && (seen.LastKnownPosition - myPosition).LengthSquared() < 9000000) // 3km, squared = 9Mm
				{
					EnemyNear = true;
					return;
				}
		}

		public LastSeen getLastSeen(long entityId)
		{ return myLastSeen[entityId]; }

		public bool tryGetLastSeen(long entityId, out LastSeen result)
		{ return myLastSeen.TryGetValue(entityId, out result); }

		public IEnumerator<LastSeen> getLastSeenEnum()
		{ return myLastSeen.Values.GetEnumerator(); }

		protected string ClassName = "Receiver";
		private Logger myLogger;
		[System.Diagnostics.Conditional("LOG_ENABLED")]
		protected void log(string toLog, string method = null, Logger.severity level = Logger.severity.DEBUG)
		{ alwaysLog(toLog, method, level); }
		protected override void alwaysLog(string toLog, string method = null, Logger.severity level = Logger.severity.DEBUG)
		{
			if (myLogger == null)
				myLogger = new Logger(CubeBlock.CubeGrid.DisplayName, ClassName);
			myLogger.log(level, method, toLog, CubeBlock.getNameOnly());
		}
	}
}
