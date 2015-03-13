using System;
using System.Collections.Generic;
//using System.Linq;
//using System.Text;

using Sandbox.ModAPI;

namespace Rynchodon.AntennaRelay
{
	public interface IReceiver
	{
		Dictionary<IMyEntity, LastSeen> myLastSeen { get; set; }

		void receive(Message mes);
	}

	public static class ReceiverExtensions
	{
		/// <summary>
		/// does not check seen for isValid
		/// </summary>
		/// <param name="seen"></param>
		public static void receive(this IReceiver recipient, LastSeen seen)
		{
			LastSeen value;
			if (recipient.myLastSeen.TryGetValue(seen.Entity, out value))
			{
				if (value == seen)
					return; // already have this one, ignore
				if (seen.isNewerThan(value))
					recipient.myLastSeen.Remove(value.Entity);
				else
					return; // mine is newer (or same age)
			}

			recipient.myLastSeen.Add(seen.Entity, seen);
			//recipient.log("got a new last seen: " + seen.Entity.DisplayName, "receive()", Logger.severity.TRACE);
		}
	}
}
