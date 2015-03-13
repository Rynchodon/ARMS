#define LOG_ENABLED //remove on build

using System;
using System.Collections.Generic;
//using System.Linq;
//using System.Text;

using Sandbox.Common.Components;
using Sandbox.ModAPI;
using VRage.Collections;

namespace Rynchodon.AntennaRelay
{
	public abstract class Receiver : UpdateEnforcer
	{
		//private static LinkedList<Receiver> allReceivers = new LinkedList<Receiver>();

		protected Dictionary<IMyEntity, LastSeen> myLastSeen = new Dictionary<IMyEntity, LastSeen>();
		internal IMyCubeBlock CubeBlock { get; set; }
		protected LinkedList<Message> myMessages = new LinkedList<Message>();

		/// <summary>
		/// Do not forget to call this!
		/// </summary>
		/// <param name="objectBuilder"></param>
		protected override void DelayedInit()
		{
			//(new Logger(null, "Receiver")).log("init", "DelayedInit()", Logger.severity.TRACE);
			CubeBlock = Entity as IMyCubeBlock;
			//allReceivers.AddLast(this);
			CubeBlock.CubeGrid.OnBlockOwnershipChanged += CubeGrid_OnBlockOwnershipChanged;
		}

		public override void Close()
		{
			//try
			//{ allReceivers.Remove(this); }
			//catch (Exception e)
			//{ alwaysLog("exception on removing from allReceivers: " + e, "Close()", Logger.severity.WARNING); }
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
			if (myLastSeen.TryGetValue(seen.Entity, out toUpdate))
			{
				if (seen.update(ref toUpdate))
				{
					log("updating: " + seen.Entity.getBestName(), "receive()", Logger.severity.TRACE);
					myLastSeen.Remove(toUpdate.Entity);
					myLastSeen.Add(toUpdate.Entity, toUpdate);
				}
				else
					log("not updating: " + seen.Entity.getBestName(), "receive()", Logger.severity.TRACE);
			}
			else
			{
				myLastSeen.Add(seen.Entity, seen);
				log("got a new last seen: " + seen.Entity.DisplayName, "receive()", Logger.severity.TRACE);
			}
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
		public static void sendToAttached(IMyCubeBlock sender, Dictionary<IMyEntity, LastSeen> dictionary)
		{ sendToAttached(sender, null, dictionary); }

		/// <summary>
		/// Sends LastSeen to attached all attached friendly antennae and to remote controls.
		/// removes invalids from the list
		/// </summary>
		private static void sendToAttached(IMyCubeBlock sender, LinkedList<LastSeen> list = null, Dictionary<IMyEntity, LastSeen> dictionary=null)
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
					dictionary.Remove(seen.Entity);
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

		protected string ClassName = "Receiver";
		private Logger myLogger;
		[System.Diagnostics.Conditional("LOG_ENABLED")]
		protected void log(string toLog, string method = null, Logger.severity level = Logger.severity.DEBUG)
		{ alwaysLog(toLog, method, level); }
		protected override void alwaysLog(string toLog, string method = null, Logger.severity level = Logger.severity.DEBUG)
		{
			if (myLogger == null)
				myLogger = new Logger(CubeBlock.CubeGrid.DisplayName, ClassName);
			myLogger.log(level, method, toLog, CubeBlock.DisplayNameText);
		}
	}
}
