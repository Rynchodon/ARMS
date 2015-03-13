using System;
using System.Collections.Generic;
//using System.Linq;
//using System.Text;

using Sandbox.Common.Components;
using Sandbox.ModAPI;

namespace Rynchodon.AntennaRelay
{
	public abstract class Receiver : MyGameLogicComponent
	{
		protected Dictionary<IMyEntity, LastSeen> myLastSeen = new Dictionary<IMyEntity, LastSeen>();

		public abstract void receive(Message mes);

		/// <summary>
		/// does not check seen for isValid
		/// </summary>
		/// <param name="seen"></param>
		public virtual void receive(LastSeen seen)
		{
			LastSeen value;
			if (myLastSeen.TryGetValue(seen.Entity, out value))
			{
				if (value == seen)
					return; // already have this one, ignore
				if (seen.isNewerThan(value))
					myLastSeen.Remove(value.Entity);
				else
					return; // mine is newer (or same age)
			}

			myLastSeen.Add(seen.Entity, seen);
			//recipient.log("got a new last seen: " + seen.Entity.DisplayName, "receive()", Logger.severity.TRACE);
		}
	}
}
