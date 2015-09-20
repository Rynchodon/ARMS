using System.Collections.Generic;
using Sandbox.ModAPI;

namespace Rynchodon.AntennaRelay
{
	public abstract class Receiver
	{
		/// <summary>Blocks shall not be registered here.</summary>
		public static readonly HashSet<Receiver> AllReceivers_NoBlock = new HashSet<Receiver>();

		/// <summary>Track LastSeen objects by entityId</summary>
		protected readonly Dictionary<long, LastSeen> myLastSeen = new Dictionary<long, LastSeen>();

		/// <summary>Not all Receivers will lock myLastSeen but some may.</summary>
		protected readonly FastResourceLock lock_myLastSeen = new FastResourceLock();

		public abstract object ReceiverObject { get; }

		private readonly Logger myLogger = new Logger("Receiver");

		/// <summary>
		/// does not check seen for isValid
		/// </summary>
		public virtual void receive(LastSeen seen)
		{
			using (lock_myLastSeen.AcquireExclusiveUsing())
			{
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

		// TODO: function canSendTo, which can be overriden instead of using extension

	}
}
