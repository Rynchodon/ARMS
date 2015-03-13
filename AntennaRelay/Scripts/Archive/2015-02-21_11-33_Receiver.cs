using System;
using System.Collections.Generic;
//using System.Linq;
//using System.Text;

using Sandbox.Common.Components;
using Sandbox.ModAPI;

namespace Rynchodon.AntennaRelay
{
	public abstract class Receiver : UpdateEnforcer
	{
		protected Dictionary<IMyEntity, LastSeen> myLastSeen = new Dictionary<IMyEntity, LastSeen>();
		protected IMyCubeBlock CubeBlock;
		protected HashSet<Message> myMessages = new HashSet<Message>();

		/// <summary>
		/// Do not forget to call this!
		/// </summary>
		/// <param name="objectBuilder"></param>
		protected override void DelayedInit()
		{
			//(new Logger(null, "Receiver")).log("init", "DelayedInit()", Logger.severity.TRACE);
			CubeBlock = Entity as IMyCubeBlock;
			CubeBlock.CubeGrid.OnBlockOwnershipChanged += CubeGrid_OnBlockOwnershipChanged;
		}

		//public abstract void Init(Sandbox.Common.ObjectBuilders.MyObjectBuilder_EntityBase objectBuilder);

		private void CubeGrid_OnBlockOwnershipChanged(IMyCubeGrid obj)
		{
			if (obj == CubeBlock)
				myMessages = new HashSet<Message>();
		}

		public abstract void receive(Message mes);

		/// <summary>
		/// does not check seen for isValid
		/// </summary>
		/// <param name="seen"></param>
		public void receive(LastSeen seen, bool forced = false)
		{
			if (seen.Entity == CubeBlock.CubeGrid && !forced)
			//{
			//	alwaysLog("do not tell me about myself: ", "receive()", Logger.severity.TRACE);
				return;
			//}

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

		/// <summary>
		/// Update a LastSeen if possible, otherwise does not change toUpdate.
		/// </summary>
		/// <param name="toUpdate"></param>
		/// <returns>true iff updated</returns>
		public bool Update(ref LastSeen toUpdate)
		{
			LastSeen updated;
			if (myLastSeen.TryGetValue(toUpdate.Entity, out updated) && toUpdate != updated)
			{
				toUpdate = updated;
				return true;
			}
			return false;
		}
	}
}
