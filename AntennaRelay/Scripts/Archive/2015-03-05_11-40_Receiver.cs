#define LOG_ENABLED

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
			CubeBlock.CubeGrid.OnBlockOwnershipChanged += CubeGrid_OnBlockOwnershipChanged;
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
		public void receive(LastSeen seen, bool forced = false)
		{
			if (seen.Entity == CubeBlock.CubeGrid && !forced)
			//{
			//	alwaysLog("do not tell me about myself: ", "receive()", Logger.severity.TRACE);
				return;
			//}

			LastSeen value;
			if (myLastSeen.TryGetValue(seen.Entity, out value))
				value.updateWith(seen);
			else
				myLastSeen.Add(seen.Entity, seen);
			//recipient.log("got a new last seen: " + seen.Entity.DisplayName, "receive()", Logger.severity.TRACE);
		}

		///// <summary>
		///// Update a LastSeen if possible, otherwise does not change toUpdate.
		///// </summary>
		///// <param name="toUpdate"></param>
		///// <returns>true iff updated</returns>
		//public bool Update(ref LastSeen toUpdate)
		//{
		//	LastSeen updated;
		//	if (myLastSeen.TryGetValue(toUpdate.Entity, out updated) && toUpdate != updated)
		//	{
		//		toUpdate = updated;
		//		return true;
		//	}
		//	return false;
		//}

		[System.Diagnostics.Conditional("LOG_ENABLED")]
		protected abstract void log(string toLog, string method = null, Logger.severity level = Logger.severity.DEBUG);
	}
}
