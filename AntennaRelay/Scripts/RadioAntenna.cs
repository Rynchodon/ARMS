#define LOG_ENABLED //remove on build

using System;
using System.Collections.Generic;
//using System.Linq;
//using System.Text;

using Sandbox.Common;
using Sandbox.Common.Components;
using Sandbox.Common.ObjectBuilders;
using Sandbox.ModAPI;
using Ingame = Sandbox.ModAPI.Ingame;
using Sandbox.ModAPI.Interfaces;
using VRage;

namespace Rynchodon.AntennaRelay
{
	[MyEntityComponentDescriptor(typeof(MyObjectBuilder_RadioAntenna))]
	public class RadioAntenna : Receiver
	{
		private static List<RadioAntenna> value_registry = new List<RadioAntenna>();
		public static IReadOnlyList<RadioAntenna> registry { get { return value_registry.AsReadOnly(); } }

		private Ingame.IMyRadioAntenna myRadioAntenna;

		protected override void DelayedInit()
		{
			base.DelayedInit();
			myRadioAntenna = Entity as Ingame.IMyRadioAntenna;
			value_registry.Add(this);

			//log("init as antenna: " + CubeBlock.BlockDefinition.SubtypeName, "Init()", Logger.severity.TRACE);
			EnforcedUpdate = MyEntityUpdateEnum.EACH_100TH_FRAME;
		}

		public override void Close()
		{
			base.Close();
			try
			{ value_registry.Remove(this); }
			catch (Exception e)
			{ alwaysLog("exception on removing from registry: " + e, "Close()", Logger.severity.WARNING); }
			myRadioAntenna = null;
		}

		public override void UpdateAfterSimulation100()
		{
			if (!IsInitialized) return;
			if (Closed) return;
			try
			{
				if (!myRadioAntenna.IsWorking)
					return;

				//Showoff.doShowoff(CubeBlock, myLastSeen.Values.GetEnumerator(), myLastSeen.Count);

				float radiusSquared;
				MyObjectBuilder_RadioAntenna antBuilder = CubeBlock.GetObjectBuilderCubeBlock() as MyObjectBuilder_RadioAntenna;
				if (!antBuilder.EnableBroadcasting)
					radiusSquared = 0;
				else
					radiusSquared = myRadioAntenna.Radius * myRadioAntenna.Radius;

				// send antenna self to radio antennae
				LinkedList<RadioAntenna> canSeeMe = new LinkedList<RadioAntenna>(); // friend and foe alike
				foreach (RadioAntenna ant in RadioAntenna.registry)
					if (CubeBlock.canSendTo(ant.CubeBlock, false, radiusSquared, true))
						canSeeMe.AddLast(ant);

				LastSeen self = new LastSeen(CubeBlock.CubeGrid);
				foreach (RadioAntenna ant in canSeeMe)
					ant.receive(self);

				// relay information to friendlies
				foreach (RadioAntenna ant in registry)
					if (CubeBlock.canSendTo(ant.CubeBlock, true, radiusSquared, true))
					{
						foreach (LastSeen seen in myLastSeen.Values)
							ant.receive(seen);
						foreach (Message mes in myMessages)
							ant.receive(mes);
					}

				Receiver.sendToAttached(CubeBlock, myLastSeen);
				Receiver.sendToAttached(CubeBlock, myMessages);

				UpdateEnemyNear();
			}
			catch (Exception e)
			{ alwaysLog("Exception: " + e, "UpdateAfterSimulation100()", Logger.severity.ERROR); }
		}
	}
}
