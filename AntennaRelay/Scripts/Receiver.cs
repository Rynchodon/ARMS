#define LOG_ENABLED //remove on build

using System.Collections.Generic;
using Sandbox.ModAPI;
using VRage.ModAPI;
using VRageMath;

namespace Rynchodon.AntennaRelay
{
	public abstract class Receiver //: UpdateEnforcer
	{
		/// <summary>
		/// Track LastSeen objects by entityId
		/// </summary>
		protected Dictionary<long, LastSeen> myLastSeen = new Dictionary<long, LastSeen>();
		public IMyCubeBlock CubeBlock { get; internal set; }
		protected LinkedList<Message> myMessages = new LinkedList<Message>();

		protected Receiver(IMyCubeBlock block)
		{
			this.CubeBlock = block;
			CubeBlock.CubeGrid.OnBlockOwnershipChanged += CubeGrid_OnBlockOwnershipChanged;
			myLogger = new Logger("Receiver", () => CubeBlock.CubeGrid.DisplayName);
			CubeBlock.OnClosing += Close;
		}

		protected abstract void Close(IMyEntity entity);

		private void CubeGrid_OnBlockOwnershipChanged(IMyCubeGrid obj)
		{
			if (obj == CubeBlock)
				myMessages = new LinkedList<Message>();
		}

		/// <summary>
		/// does not check mes for isValid
		/// </summary>
		/// <param name="mes">message to receive</param>
		public virtual void receive(Message mes)
		{
			if (myMessages.Contains(mes))
				return;
			myMessages.AddLast(mes);
			myLogger.debugLog("got a new message: " + mes.Content + ", count is now " + myMessages.Count, "receive()", Logger.severity.TRACE);
		}

		/// <summary>
		/// number of messages currently held
		/// </summary>
		/// <returns>number of messages</returns>
		public int messageCount()
		{ return myMessages.Count; }

		/// <summary>
		/// does not check seen for isValid
		/// </summary>
		/// <param name="seen"></param>
		/// <param name="forced">for receiving LastSeen for self</param>
		public void receive(LastSeen seen, bool forced = false)
		{
			if (seen.Entity == CubeBlock.CubeGrid && !forced)
				return;

			LastSeen toUpdate;
			if (myLastSeen.TryGetValue(seen.Entity.EntityId, out toUpdate))
			{
				if (seen.update(ref toUpdate))
					myLastSeen[toUpdate.Entity.EntityId] = toUpdate;
			}
			else
				myLastSeen.Add(seen.Entity.EntityId, seen);
		}

		/// <summary>
		/// Sends LastSeen to attached all attached friendly antennae and to remote controls.
		/// removes invalids from the list
		/// </summary>
		public static void sendToAttached(IMyCubeBlock sender, ICollection<LastSeen> list)
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
		private static void sendToAttached(IMyCubeBlock sender, ICollection<LastSeen> list = null, Dictionary<long, LastSeen> dictionary = null)
		{
			ICollection<LastSeen> toSend;
			if (list != null)
				toSend = list;
			else
				toSend = dictionary.Values;

			if (dictionary != null || !toSend.IsReadOnly)
			{
				LinkedList<LastSeen> removeList = new LinkedList<LastSeen>();
				foreach (LastSeen seen in toSend)
					if (!seen.isValid)
						removeList.AddLast(seen);
				foreach (LastSeen seen in removeList)
					if (dictionary != null)
						dictionary.Remove(seen.Entity.EntityId);
					else
						toSend.Remove(seen);
			}

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
			foreach (ShipController remote in ShipController.registry.Values)
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
						ShipController remote;
						if (ShipController.registry.TryGetValue(mes.DestCubeBlock, out remote))
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

		public LastSeen getLastSeen(long entityId)
		{ return myLastSeen[entityId]; }

		public bool tryGetLastSeen(long entityId, out LastSeen result)
		{ return myLastSeen.TryGetValue(entityId, out result); }

		public IEnumerator<LastSeen> getLastSeenEnum()
		{ return myLastSeen.Values.GetEnumerator(); }

		private Logger myLogger = new Logger(null, "Receiver");
	}

	public static class ReceiverExtensions
	{
		public static bool IsOpen(this Receiver receiver)
		{ return receiver != null && receiver.CubeBlock != null && !receiver.CubeBlock.Closed; }
	}
}

