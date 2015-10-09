using System;
using System.Collections.Generic;
using Rynchodon.Attached;
using Sandbox.ModAPI;
using VRage.Collections;
using VRage.ModAPI;

namespace Rynchodon.AntennaRelay
{
	public abstract class Receiver
	{

		/// <summary>
		/// For sending newly created LastSeen to all attached antennae and ShipController.
		/// </summary>
		/// <param name="sender">The block doing the sending.</param>
		/// <param name="toSend">The LastSeen to send.</param>
		public static void SendToAttached(IMyCubeBlock sender, ICollection<LastSeen> toSend)
		{
			foreach (RadioAntenna radioAnt in RadioAntenna.registry)
				if (sender.canSendTo(radioAnt.CubeBlock, true))
					foreach (LastSeen seen in toSend)
						radioAnt.Receive(seen);

			foreach (LaserAntenna laserAnt in LaserAntenna.registry)
				if (sender.canSendTo(laserAnt.CubeBlock, true))
					foreach (LastSeen seen in toSend)
						laserAnt.Receive(seen);

			foreach (ShipController controller in ShipController.registry.Values)
				if (sender.canSendTo(controller.CubeBlock, true))
					foreach (LastSeen seen in toSend)
						controller.Receive(seen);
		}

		/// <summary>
		/// For sending newly created Message to all attached antennae.
		/// </summary>
		/// <param name="sender">The block doing the sending.</param>
		/// <param name="toSend">The Message to send.</param>
		public static void SendToAttached(IMyCubeBlock sender, ICollection<Message> toSend)
		{
			foreach (RadioAntenna radioAnt in RadioAntenna.registry)
				if (sender.canSendTo(radioAnt.CubeBlock, true))
					foreach (Message msg in toSend)
						radioAnt.Receive(msg);

			foreach (LaserAntenna laserAnt in LaserAntenna.registry)
				if (sender.canSendTo(laserAnt.CubeBlock, true))
					foreach (Message msg in toSend)
						laserAnt.Receive(msg);
		}

		private readonly Logger myLogger = new Logger(null, "Receiver");
		/// <summary>Track LastSeen objects by entityId</summary>
		private Dictionary<long, LastSeen> myLastSeen;
		private MyUniqueList<Message> myMessages;

		public IMyCubeBlock CubeBlock { get; private set; }

		protected Receiver(IMyCubeBlock block)
		{
			this.CubeBlock = block;
			CubeBlock.CubeGrid.OnBlockOwnershipChanged += CubeGrid_OnBlockOwnershipChanged;
			
			myLogger = new Logger("Receiver", () => CubeBlock.CubeGrid.DisplayName);
			myLastSeen = new Dictionary<long, LastSeen>();
			myMessages = new MyUniqueList<Message>();

			CubeBlock.OnClosing += in_Close;
		}

		private void in_Close(IMyEntity entity)
		{
			Close(entity); // derived should run first in case it needs CubeBlock
			CubeBlock = null;
			myLastSeen = null;
			myMessages = null;
		}

		protected virtual void Close(IMyEntity entity) { }

		private void CubeGrid_OnBlockOwnershipChanged(IMyCubeGrid obj)
		{
			if (obj == CubeBlock)
				myMessages.Clear();
		}

		/// <summary>
		/// Executes a function on each valid LastSeen and removes every invalid one encountered.
		/// </summary>
		/// <param name="toInvoke">Function executed on LastSeen. Iff it returns true, short-curcuit.</param>
		public void ForEachLastSeen(Func<LastSeen, bool> toInvoke)
		{
			List<long> removeList = new List<long>();
			foreach (var pair in myLastSeen)
			{
				if (pair.Value.IsValid)
				{
					if (toInvoke(pair.Value))
						break;
				}
				else
					removeList.Add(pair.Key);
			}

			foreach (long entityId in removeList)
				myLastSeen.Remove(entityId);
		}

		/// <summary>
		/// Executes a function on each valid Message and removes every invalid one encountered.
		/// </summary>
		/// <param name="toInvoke">Function executed on Message. Iff it returns true, short-curcuit.</param>
		public void ForEachMessage(Func<Message, bool> toInvoke)
		{
			List<Message> removeList = new List<Message>();
			foreach (Message mes in myMessages)
			{
				if (mes.IsValid)
				{
					if (toInvoke(mes))
						break;
				}
				else
					removeList.Add(mes);
			}

			foreach (Message mes in removeList)
				myMessages.Remove(mes);
		}

		/// <summary>
		/// Copies all LastSeen and Messages to the other Receiver. Does not check if a connection is possible.
		/// Removes all invalid LastSeen and Message
		/// </summary>
		/// <param name="other">Receiving receiver</param>
		protected void Relay(Receiver other)
		{
			ForEachLastSeen(seen => {
				other.Receive(seen);
				return false;
			});

			ForEachMessage(msg => {
				other.Receive(msg);
				return false;
			});
		}

		/// <summary>
		/// Copies all LastSeen to all attached Receiver.
		/// If attached to the destination of a message, sends the message to the destination. Otherwise, sends it to all attached antennae.
		/// Removes invalid LastSeen and Message
		/// </summary>
		protected void RelayAttached()
		{
			ForEachLastSeen(seen => {
				foreach (RadioAntenna radioAnt in RadioAntenna.registry)
					if (CubeBlock.canSendTo(radioAnt.CubeBlock, true))
						radioAnt.Receive(seen);

				foreach (LaserAntenna laserAnt in LaserAntenna.registry)
					if (CubeBlock.canSendTo(laserAnt.CubeBlock, true))
						laserAnt.Receive(seen);

				foreach (ShipController remote in ShipController.registry.Values)
					if (CubeBlock.canSendTo(remote.CubeBlock, true))
						remote.Receive(seen);

				return false;
			});

			ForEachMessage(msg => {
				if (AttachedGrid.IsGridAttached(CubeBlock.CubeGrid, msg.DestCubeBlock.CubeGrid, AttachedGrid.AttachmentKind.Terminal))
				{
					// get receiver for block
					ShipController remote;
					if (ShipController.registry.TryGetValue(msg.DestCubeBlock, out remote))
					{
						remote.Receive(msg);
						msg.IsValid = false;
						return false;
					}
					ProgrammableBlock progBlock;
					if (ProgrammableBlock.registry.TryGetValue(msg.DestCubeBlock, out progBlock))
					{
						progBlock.Receive(msg);
						msg.IsValid = false;
						return false;
					}

					myLogger.alwaysLog("Message has invalid destination: " + msg.ToString(), "RelayAttached()", Logger.severity.WARNING);
					msg.IsValid = false;
					return false;
				}

				// send to all radio antenna
				foreach (RadioAntenna radioAnt in RadioAntenna.registry)
					if (CubeBlock.canSendTo(radioAnt.CubeBlock, true))
						radioAnt.Receive(msg);

				foreach (LaserAntenna laserAnt in LaserAntenna.registry)
					if (CubeBlock.canSendTo(laserAnt.CubeBlock, true))
						laserAnt.Receive(msg);

				return false;
			});
		}

		/// <summary>number of messages currently held</summary>
		public int messageCount { get { return myMessages.Count; } }

		public LastSeen getLastSeen(long entityId)
		{ return myLastSeen[entityId]; }

		public bool tryGetLastSeen(long entityId, out LastSeen result)
		{ return myLastSeen.TryGetValue(entityId, out result); }

		protected Message RemoveOneMessage()
		{
			Message result = myMessages[0];
			myMessages.Remove(result);
			return result;
		}

		/// <summary>
		/// does not check seen for isValid
		/// </summary>
		/// <param name="mes">message to receive</param>
		public void Receive(Message mes)
		{
			if (myMessages.Contains(mes))
				return;
			myMessages.Add(mes);
			myLogger.debugLog("got a new message: " + mes.Content + ", count is now " + myMessages.Count, "receive()", Logger.severity.TRACE);
		}

		/// <summary>
		/// does not check seen for isValid
		/// </summary>
		/// <param name="forced">for receiving LastSeen for self</param>
		public void Receive(LastSeen seen, bool forced = false)
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

	}

	public static class ReceiverExtensions
	{
		public static bool IsOpen(this Receiver receiver)
		{ return receiver != null && receiver.CubeBlock != null && !receiver.CubeBlock.Closed; }
	}
}
