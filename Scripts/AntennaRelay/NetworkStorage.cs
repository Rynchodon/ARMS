using System;
using System.Collections.Generic;
using Sandbox.ModAPI;
using VRage.Collections;

namespace Rynchodon.AntennaRelay
{
	/// <summary>
	/// Holds transmissions for one or more connected Node
	/// </summary>
	public class NetworkStorage
	{

		private static FastResourceLock lock_sendToSet = new FastResourceLock();
		private static HashSet<NetworkStorage> s_sendToSet = new HashSet<NetworkStorage>();

		static NetworkStorage()
		{
			MyAPIGateway.Entities.OnCloseAll += Entities_OnCloseAll;
		}

		private static void Entities_OnCloseAll()
		{
			MyAPIGateway.Entities.OnCloseAll -= Entities_OnCloseAll;
			lock_sendToSet = null;
			s_sendToSet = null;
		}

		/// <summary>
		/// Send a LastSeen to one or more NetworkStorage. Faster than looping through the collection and inoking Receive() for each one.
		/// </summary>
		public static void Receive(ICollection<NetworkStorage> storage, LastSeen seen)
		{
			lock_sendToSet.AcquireExclusive();
			try
			{
				foreach (NetworkStorage sto in storage)
					AddStorage(sto);

				foreach (NetworkStorage sto in s_sendToSet)
					using (sto.lock_m_lastSeen.AcquireExclusiveUsing())
						sto.in_Receive(seen);
			}
			finally
			{
				s_sendToSet.Clear();
				lock_sendToSet.ReleaseExclusive();
			}
		}

		/// <summary>
		/// Send a Message to one or more NetworkStorage. Faster than looping through the collection and inoking Receive() for each one.
		/// </summary>
		public static void Receive(ICollection<NetworkStorage> storage, Message msg)
		{
			lock_sendToSet.AcquireExclusive();
			try
			{
				foreach (NetworkStorage sto in storage)
					AddStorage(sto);

				foreach (NetworkStorage sto in s_sendToSet)
					using (sto.lock_m_messages.AcquireExclusiveUsing())
						sto.in_Receive(msg);
			}
			finally
			{
				s_sendToSet.Clear();
				lock_sendToSet.ReleaseExclusive();
			}
		}

		/// <summary>
		/// <para>Adds a NetworkStorage to s_sendToSet and, if it is not already present, all NetworkStorage it will push to.</para>
		/// <para>lock_sendToSet should be exclusively locked before invoking this method.</para>
		/// </summary>
		/// <param name="storage">NetworkStorage to add to s_sendToSet</param>
		private static void AddStorage(NetworkStorage storage)
		{
			if (s_sendToSet.Add(storage))
				using (storage.lock_m_pushTo_count.AcquireSharedUsing())
					foreach (NetworkNode connected in storage.m_pushTo_count.Keys)
						if (connected.Storage != null)
							AddStorage(connected.Storage);
		}

		private readonly Logger m_logger;

		private readonly FastResourceLock lock_m_lastSeen = new FastResourceLock();
		private readonly Dictionary<long, LastSeen> m_lastSeen = new Dictionary<long, LastSeen>();
		private readonly FastResourceLock lock_m_messages = new FastResourceLock();
		private readonly HashSet<Message> m_messages = new HashSet<Message>();
		private readonly FastResourceLock lock_m_pushTo_count = new FastResourceLock();
		/// <summary>For one way radio communication.</summary>
		private readonly Dictionary<NetworkNode, int> m_pushTo_count = new Dictionary<NetworkNode, int>();

		public readonly NetworkNode PrimaryNode;

		/// <summary>Total number of transmissions stored.</summary>
		public int Size { get { return m_lastSeen.Count + m_messages.Count; } }

		public NetworkStorage(NetworkNode primary)
		{
			this.m_logger = new Logger(GetType().Name, () => primary.LoggingName);
			this.PrimaryNode = primary;

			m_logger.debugLog("Created", "NetworkStorage()", Logger.severity.DEBUG);
		}

		/// <summary>
		/// Creates a new NetworkStorage with all the same LastSeen and Message. Used when nodes lose connection.
		/// </summary>
		/// <param name="primary">The node that will be the primary for the new storage.</param>
		/// <returns>A new storage that is a copy of this one.</returns>
		public NetworkStorage Clone(NetworkNode primary)
		{
			m_logger.debugLog("cloning, primary " + primary.LoggingName, "Clone()", Logger.severity.DEBUG);

			NetworkStorage clone = new NetworkStorage(primary);

			// locks need not be held on clone
			ForEachLastSeen(clone.m_lastSeen.Add);
			ForEachMessage(msg => clone.m_messages.Add(msg));

			return clone;
		}

		/// <summary>
		/// Copies all the transmissions to the target storage. Used when merging storages.
		/// </summary>
		/// <param name="recipient">NetworkStorage to copy transmissions to.</param>
		public void CopyTo(NetworkStorage recipient)
		{
			m_logger.debugLog("copying to, " + recipient.PrimaryNode.LoggingName, "CopyTo()", Logger.severity.DEBUG);

			using (recipient.lock_m_lastSeen.AcquireExclusiveUsing())
				ForEachLastSeen(recipient.in_Receive);
			using (recipient.lock_m_messages.AcquireExclusiveUsing())
				ForEachMessage(recipient.in_Receive);
		}

		/// <summary>
		/// Add a network connection that data will be pushed through.
		/// </summary>
		/// <param name="node">Node that will receive data.</param>
		public void AddPushTo(NetworkNode node)
		{
			int count;
			using (lock_m_pushTo_count.AcquireExclusiveUsing())
			{
				if (!m_pushTo_count.TryGetValue(node, out count))
					count = 0;
				m_pushTo_count[node] = count + 1;
			}

			m_logger.debugLog("added push to: " + node.LoggingName + ", count: " + (count + 1), "AddPushTo()", Logger.severity.DEBUG);

			if (count != 0)
				return;

			foreach (NetworkNode n in m_pushTo_count.Keys)
				if (node.Storage == n.Storage)
					return;

			CopyTo(node.Storage);
		}

		/// <summary>
		/// Remove a network connection that data was pushed through.
		/// </summary>
		/// <param name="node">Node that was receiving data.</param>
		public void RemovePushTo(NetworkNode node)
		{
			using (lock_m_pushTo_count.AcquireExclusiveUsing())
			{
				int count = m_pushTo_count[node];
				if (count == 1)
					m_pushTo_count.Remove(node);
				else
					m_pushTo_count[node] = count - 1;

				m_logger.debugLog("removed push to: " + node.LoggingName + ", count: " + (count - 1), "AddPushTo()", Logger.severity.DEBUG);
			}
		}

		/// <summary>
		/// <para>Add a LastSeen to this storage or updates an existing one. LastSeen will be pushed to connected storages.</para>
		/// <para>Not optimized for use within a loop.</para>
		/// </summary>
		/// <param name="seen">LastSeen data to receive.</param>
		public void Receive(LastSeen seen)
		{
			lock_sendToSet.AcquireExclusive();
			try
			{
				AddStorage(this);

				foreach (NetworkStorage sto in s_sendToSet)
					using (sto.lock_m_lastSeen.AcquireExclusiveUsing())
						sto.in_Receive(seen);
			}
			finally
			{
				s_sendToSet.Clear();
				lock_sendToSet.ReleaseExclusive();
			}
		}

		/// <summary>
		/// <para>Add a Message to this storage. Message will be pushed to connected storages.</para>
		/// <para>Not optimized for use within a loop.</para>
		/// </summary>
		/// <param name="msg">Message to receive.</param>
		public void Receive(Message msg)
		{
			lock_sendToSet.AcquireExclusive();
			try
			{
				AddStorage(this);

				foreach (NetworkStorage sto in s_sendToSet)
					using (sto.lock_m_messages.AcquireExclusiveUsing())
						sto.in_Receive(msg);
			}
			finally
			{
				s_sendToSet.Clear();
				lock_sendToSet.ReleaseExclusive();
			}
		}

		/// <summary>
		/// Perform an action on each LastSeen. Invalid LastSeen are removed by this method.
		/// </summary>
		/// <param name="method">Action invoked on each LastSeen.</param>
		public void ForEachLastSeen(Action<LastSeen> method)
		{
			List<LastSeen> invalid = null;

			using (lock_m_lastSeen.AcquireSharedUsing())
				foreach (LastSeen seen in m_lastSeen.Values)
				{
					if (seen.IsValid)
						method(seen);
					else
					{
						if (invalid == null)
							invalid = new List<LastSeen>();
						invalid.Add(seen);
					}
				}

			if (invalid != null)
			{
				m_logger.debugLog("Removing " + invalid.Count + " invalid LastSeen", "ForEachLastSeen()", Logger.severity.DEBUG);
				using (lock_m_lastSeen.AcquireExclusiveUsing())
					foreach (LastSeen seen in invalid)
						m_lastSeen.Remove(seen.Entity.EntityId);
			}
		}

		/// <summary>
		/// Perform an action on each LastSeen. Invalid LastSeen are removed by this method.
		/// </summary>
		/// <param name="method">Action invoked on each EntityId, LastSeen pair.</param>
		public void ForEachLastSeen(Action<long, LastSeen> method)
		{
			List<long> invalid = null;

			using (lock_m_lastSeen.AcquireSharedUsing())
				foreach (var pair in m_lastSeen)
				{
					if (pair.Value.IsValid)
						method(pair.Key, pair.Value);
					else
					{
						if (invalid == null)
							invalid = new List<long>();
						invalid.Add(pair.Key);
					}
				}

			if (invalid != null)
			{
				m_logger.debugLog("Removing " + invalid.Count + " invalid LastSeen", "ForEachLastSeen()", Logger.severity.DEBUG);
				using (lock_m_lastSeen.AcquireExclusiveUsing())
					foreach (long id in invalid)
						m_lastSeen.Remove(id);
			}
		}

		/// <summary>
		/// Perform an action on each LastSeen. Invalid LastSeen are removed by this method.
		/// </summary>
		/// <param name="method">Function invoked on each LastSeen. If it returns true, short-curcuit.</param>
		public void SearchLastSeen(Func<LastSeen, bool> method)
		{
			List<LastSeen> invalid = null;

			using (lock_m_lastSeen.AcquireSharedUsing())
				foreach (LastSeen seen in m_lastSeen.Values)
				{
					if (seen.IsValid)
					{
						if (method(seen))
							break;
					}
					else
					{
						if (invalid == null)
							invalid = new List<LastSeen>();
						invalid.Add(seen);
					}
				}

			if (invalid != null)
			{
				m_logger.debugLog("Removing " + invalid.Count + " invalid LastSeen", "SearchLastSeen()", Logger.severity.DEBUG);
				using (lock_m_lastSeen.AcquireExclusiveUsing())
					foreach (LastSeen seen in invalid)
						m_lastSeen.Remove(seen.Entity.EntityId);
			}
		}

		/// <summary>
		/// Perform an action on each LastSeen. Invalid LastSeen are removed by this method.
		/// </summary>
		/// <param name="method">Function invoked on each EntityId, LastSeen pair. If it returns true, short-curcuit.</param>
		public void SearchLastSeen(Func<long, LastSeen, bool> method)
		{
			List<long> invalid = null;

			using (lock_m_lastSeen.AcquireSharedUsing())
				foreach (var pair in m_lastSeen)
				{
					if (pair.Value.IsValid)
					{
						if (method(pair.Key, pair.Value))
							break;
					}
					else
					{
						if (invalid == null)
							invalid = new List<long>();
						invalid.Add(pair.Key);
					}
				}

			if (invalid != null)
			{
				m_logger.debugLog("Removing " + invalid.Count + " invalid LastSeen", "SearchLastSeen()", Logger.severity.DEBUG);
				using (lock_m_lastSeen.AcquireExclusiveUsing())
					foreach (long id in invalid)
						m_lastSeen.Remove(id);
			}
		}

		/// <summary>
		/// Try to get a LastSeen by EntitiyId.
		/// </summary>
		public bool TryGetLastSeen(long entityId, out LastSeen seen)
		{
			using (lock_m_lastSeen.AcquireSharedUsing())
				return m_lastSeen.TryGetValue(entityId, out seen);
		}

		/// <summary>
		/// Perform an action on each Message. Invalid Message are removed by this method.
		/// </summary>
		/// <param name="method">Action to invoke on each message.</param>
		private void ForEachMessage(Action<Message> method)
		{
			List<Message> invalid = null;

			using (lock_m_messages.AcquireSharedUsing())
				foreach (Message msg in m_messages)
				{
					if (msg.IsValid)
						method(msg);
					else
					{
						if (invalid == null)
							invalid = new List<Message>();
						invalid.Add(msg);
					}
				}

			if (invalid != null)
			{
				m_logger.debugLog("Removing " + invalid.Count + " invalid Message", "ForEachMessage()", Logger.severity.DEBUG);
				using (lock_m_messages.AcquireExclusiveUsing())
					foreach (Message msg in invalid)
						m_messages.Remove(msg);
			}
		}

		/// <summary>
		/// <para>Internal receive method. Adds the LastSeen to this storage or updates an existing one.</para>
		/// <para>lock_m_lastSeen should be exclusively locked before inovoking this method.</para>
		/// </summary>
		private void in_Receive(LastSeen seen)
		{
			LastSeen toUpdate;
			if (m_lastSeen.TryGetValue(seen.Entity.EntityId, out toUpdate))
			{
				if (seen.update(ref toUpdate))
					m_lastSeen[toUpdate.Entity.EntityId] = toUpdate;
			}
			else
				m_lastSeen.Add(seen.Entity.EntityId, seen);
		}

		/// <summary>
		/// <para>Internal receive method. Adds the Message to this storage.</para>
		/// <para>lock_m_messages should be exclusively locked before invoking this method.</para>
		/// </summary>
		private void in_Receive(Message msg)
		{
			if (m_messages.Add(msg))
				m_logger.debugLog("got a new message: " + msg.Content + ", count is now " + m_messages.Count, "receive()", Logger.severity.DEBUG);
			else
				m_logger.debugLog("already have message: " + msg.Content + ", count is now " + m_messages.Count, "receive()", Logger.severity.DEBUG);
		}

	}
}
