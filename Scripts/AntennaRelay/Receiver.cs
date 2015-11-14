using System;
using System.Collections.Generic;
using VRage.Collections;
using VRage.ModAPI;

namespace Rynchodon.AntennaRelay
{
	public class Receiver
	{

		public readonly IMyEntity Entity;

		private readonly Logger m_logger;

		private Dictionary<long, LastSeen> m_lastSeen = new Dictionary<long, LastSeen>();
		private FastResourceLock lock_m_lastSeen = new FastResourceLock("Receiver.myLastSeen");
		private MyUniqueList<Message> m_messages = new MyUniqueList<Message>();
		private FastResourceLock lock_m_messages = new FastResourceLock("Receiver.myMessages");

		public Receiver(IMyEntity entity)
		{
			m_logger = new Logger("Receiver", () => entity.getBestName());
			Entity = entity;
		}

		/// <summary>
		/// Executes a function on each valid LastSeen and removes every invalid one encountered.
		/// </summary>
		/// <param name="toInvoke">Function executed on LastSeen. Iff it returns true, short-curcuit.</param>
		public void ForEachLastSeen(Func<LastSeen, bool> toInvoke)
		{
			List<long> removeList = new List<long>();
			using (lock_m_lastSeen.AcquireSharedUsing())
				foreach (var pair in m_lastSeen)
				{
					if (pair.Value.IsValid)
					{
						if (toInvoke(pair.Value))
							break;
					}
					else
						removeList.Add(pair.Key);
				}

			if (removeList.Count != 0)
				using (lock_m_lastSeen.AcquireExclusiveUsing())
					foreach (long entityId in removeList)
						m_lastSeen.Remove(entityId);
		}

		/// <summary>
		/// Executes a function on each valid Message and removes every invalid one encountered.
		/// </summary>
		/// <param name="toInvoke">Function executed on Message. Iff it returns true, short-curcuit.</param>
		public void ForEachMessage(Func<Message, bool> toInvoke)
		{
			List<Message> removeList = new List<Message>();
			using (lock_m_messages.AcquireSharedUsing())
				foreach (Message mes in m_messages)
				{
					if (mes.IsValid)
					{
						if (toInvoke(mes))
							break;
					}
					else
						removeList.Add(mes);
				}

			if (removeList.Count != 0)
				using (lock_m_messages.AcquireExclusiveUsing())
					foreach (Message mes in removeList)
						m_messages.Remove(mes);
		}

		/// <summary>number of messages currently held</summary>
		public int messageCount { get { return m_messages.Count; } }

		public int lastSeenCount { get { return m_lastSeen.Count; } }

		public LastSeen getLastSeen(long entityId)
		{
			using (lock_m_lastSeen.AcquireSharedUsing())
				return m_lastSeen[entityId];
		}

		public bool tryGetLastSeen(long entityId, out LastSeen result)
		{
			bool retreived;
			using (lock_m_lastSeen.AcquireSharedUsing())
				retreived = m_lastSeen.TryGetValue(entityId, out result);
			if (!retreived)
				return false;
			if (result.IsValid)
				return true;
			using (lock_m_lastSeen.AcquireExclusiveUsing())
				m_lastSeen.Remove(entityId);
			return false;
		}

		/// <summary>
		/// does not check seen for isValid
		/// </summary>
		/// <param name="mes">message to receive</param>
		public void Receive(Message mes)
		{
			using (lock_m_messages.AcquireExclusiveUsing())
			{
				if (m_messages.Contains(mes))
					return;
				m_messages.Add(mes);
			}
			m_logger.debugLog("got a new message: " + mes.Content + ", count is now " + m_messages.Count, "receive()", Logger.severity.TRACE);
		}

		/// <summary>
		/// does not check seen for isValid
		/// </summary>
		/// <param name="forced">for receiving LastSeen for self</param>
		public void Receive(LastSeen seen, bool forced = false)
		{
			if (seen.Entity == Entity && !forced)
				return;

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

		protected void ClearMessages()
		{
			using (lock_m_messages.AcquireExclusiveUsing())
				m_messages.Clear();
		}

		protected Message RemoveOneMessage()
		{
			Message result;
			using (lock_m_messages.AcquireExclusiveUsing())
			{
				result = m_messages[0];
				m_messages.Remove(result);
			}
			return result;
		}

		/// <summary>
		/// Copies all LastSeen and Messages to the other Receiver. Does not check if a connection is possible.
		/// Removes all invalid LastSeen and Message
		/// </summary>
		/// <param name="other">Receiving receiver</param>
		protected void Relay(ReceiverBlock other)
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

	}
}
