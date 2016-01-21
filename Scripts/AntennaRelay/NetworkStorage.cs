using System.Collections.Generic;
using VRage.Collections;

namespace Rynchodon.AntennaRelay
{
	/// <summary>
	/// Holds data for one or more connected Receiver
	/// </summary>
	public class NetworkStorage
	{

		private static FastResourceLock lock_sendToSet = new FastResourceLock();
		private static HashSet<NetworkStorage> s_sendToSet = new HashSet<NetworkStorage>();

		/// <summary>
		/// Send a LastSeen to one or more NetworkStorage.
		/// </summary>
		public static void Receive(ICollection<NetworkStorage> storage, LastSeen seen)
		{
			using (lock_sendToSet.AcquireExclusiveUsing())
			{
				foreach (NetworkStorage sto in storage)
					AddStorage(sto);

				foreach (NetworkStorage sto in s_sendToSet)
					sto.in_Receive(seen);

				s_sendToSet.Clear();
			}
		}

		/// <summary>
		/// Send a Message to one or more NetworkStorage.
		/// </summary>
		public static void Receive(ICollection<NetworkStorage> storage, Message msg)
		{
			using (lock_sendToSet.AcquireExclusiveUsing())
			{
				foreach (NetworkStorage sto in storage)
					AddStorage(sto);

				foreach (NetworkStorage sto in s_sendToSet)
					sto.in_Receive(msg);

				s_sendToSet.Clear();
			}
		}

		/// <summary>
		/// Adds a NetworkStorage to s_sendToSet and, if it is not already present, all NetworkStorage it will push to.
		/// Does not lock lock_sendToSet.
		/// </summary>
		/// <param name="storage">NetworkStorage to add to s_sendToSet</param>
		private static void AddStorage(NetworkStorage storage)
		{
			if (s_sendToSet.Add(storage))
				using (storage.lock_m_pushTo_count.AcquireSharedUsing())
					foreach (NetworkStorage connected in storage.m_pushTo_count.Keys)
						AddStorage(connected);
		}

		private readonly Logger m_logger;

		private readonly FastResourceLock lock_m_lastSeen = new FastResourceLock();
		private readonly Dictionary<long, LastSeen> m_lastSeen = new Dictionary<long, LastSeen>();
		private readonly FastResourceLock lock_m_messages = new FastResourceLock();
		private readonly HashSet<Message> m_messages = new HashSet<Message>();
		private readonly FastResourceLock lock_m_pushTo_count = new FastResourceLock();
		/// <summary>For one way radio communication.</summary>
		private readonly Dictionary<NetworkStorage, int> m_pushTo_count = new Dictionary<NetworkStorage, int>();

		public readonly NetworkNode PrimaryNode;

		public int Size { get { return m_lastSeen.Count + m_messages.Count; } }

		public NetworkStorage(NetworkNode primary)
		{
			this.m_logger = new Logger(GetType().Name, primary.Entity);
			this.PrimaryNode = primary;

			m_logger.debugLog("Created", "NetworkStorage()");
		}

		public NetworkStorage Clone(NetworkNode primary)
		{
			m_logger.debugLog("cloning, primary " + primary.Entity.getBestName(), "Clone()", Logger.severity.DEBUG);

			NetworkStorage clone = new NetworkStorage(primary);

			using (lock_m_lastSeen.AcquireSharedUsing())
				foreach (LastSeen seen in m_lastSeen.Values)
					clone.m_lastSeen.Add(seen.Entity.EntityId, seen);
			using (lock_m_messages.AcquireSharedUsing())
				foreach (Message msg in m_messages)
					clone.m_messages.Add(msg);

			return clone;
		}

		/// <summary>
		/// Copies all the transmissions to the target storage.
		/// </summary>
		/// <param name="recipient">NetworkStorage to copy transmissions to.</param>
		public void CopyTo(NetworkStorage recipient)
		{
			m_logger.debugLog("copying to, " + recipient.PrimaryNode.Entity.getBestName(), "CopyTo()", Logger.severity.DEBUG);

			using (lock_m_lastSeen.AcquireSharedUsing())
				foreach (LastSeen seen in m_lastSeen.Values)
					recipient.in_Receive(seen);
			using (lock_m_messages.AcquireSharedUsing())
				foreach (Message msg in m_messages)
					recipient.in_Receive(msg);
		}

		/// <summary>
		/// Add a network connection that data will be pushed through.
		/// </summary>
		/// <param name="storage">Storage that will receive data.</param>
		public void AddPushTo(NetworkStorage storage)
		{
			int count;
			using (lock_m_pushTo_count.AcquireExclusiveUsing())
			{
				if (!m_pushTo_count.TryGetValue(storage, out count))
					count = 0;
				m_pushTo_count[storage] = count + 1;
			}

			if (count != 0)
				return;

			CopyTo(storage);
		}

		/// <summary>
		/// Remove a network connection that data was pushed through.
		/// </summary>
		/// <param name="storage">Storage that was receiving data.</param>
		public void RemovePushTo(NetworkStorage storage)
		{
			using (lock_m_pushTo_count.AcquireExclusiveUsing())
			{
				int count = m_pushTo_count[storage];
				if (count == 1)
					m_pushTo_count.Remove(storage);
				else
					m_pushTo_count[storage] = count - 1;
			}
		}

		public void Receive(LastSeen seen)
		{
			using (lock_sendToSet.AcquireExclusiveUsing())
			{
				AddStorage(this);

				foreach (NetworkStorage sto in s_sendToSet)
					sto.in_Receive(seen);

				s_sendToSet.Clear();
			}
		}

		public void Receive(Message msg)
		{
			using (lock_sendToSet.AcquireExclusiveUsing())
			{
				AddStorage(this);

				foreach (NetworkStorage sto in s_sendToSet)
					sto.in_Receive(msg);

				s_sendToSet.Clear();
			}
		}

		private void in_Receive(LastSeen seen)
		{
			LastSeen toUpdate;
			using (lock_m_lastSeen.AcquireExclusiveUsing())
				if (m_lastSeen.TryGetValue(seen.Entity.EntityId, out toUpdate))
				{
					if (seen.update(ref toUpdate))
						m_lastSeen[toUpdate.Entity.EntityId] = toUpdate;
				}
				else
					m_lastSeen.Add(seen.Entity.EntityId, seen);
		}

		private void in_Receive(Message msg)
		{
			using (lock_m_messages.AcquireExclusiveUsing())
				if (m_messages.Add(msg))
					m_logger.debugLog("got a new message: " + msg.Content + ", count is now " + m_messages.Count, "receive()", Logger.severity.DEBUG);
				else
					m_logger.debugLog("already have message: " + msg.Content + ", count is now " + m_messages.Count, "receive()", Logger.severity.DEBUG);
		}

	}
}
