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

namespace Rynchodon.AntennaRelay
{
	[MyEntityComponentDescriptor(typeof(MyObjectBuilder_Beacon))]
	public class Beacon : MyGameLogicComponent, Broadcaster
	{
		internal bool isRadar = false;

		//internal static HashSet<Beacon> registry = new HashSet<Beacon>(); // not actually used, we instead report to Antenna
		//private HashSet<Antenna> canSendTo = new HashSet<Antenna>(); // might need this for radar
		private HashSet<IMyEntity> radarCanSee; // only init if this is radar

		private IMyCubeBlock CubeBlock;
		private Ingame.IMyBeacon myBeacon;

		private MyObjectBuilder_EntityBase builder;
		public override MyObjectBuilder_EntityBase GetObjectBuilder(bool copy = false)
		{ return builder; }

		public override void Init(MyObjectBuilder_EntityBase objectBuilder)
		{
			builder = objectBuilder;
			CubeBlock = Entity as IMyCubeBlock;
			myBeacon = Entity as Ingame.IMyBeacon;

			//if (isRadar) // TODO test for is radar
			//{
			//	isRadar = true;
			//	//radarCanSee = new HashSet<IMyEntity>();
			//}

			//registry.Add(this);
			Entity.NeedsUpdate |= MyEntityUpdateEnum.EACH_100TH_FRAME;
			log("initialized, updating: " + Entity.NeedsUpdate, "Init()", Logger.severity.INFO);
		}

		private bool isClosed = false;
		public override void Close()
		{
			//log("closed, updating: " + Entity.NeedsUpdate, "Init()", Logger.severity.INFO);
			myBeacon = null;
			//registry.Remove(this);
			isClosed = true;
		}

		//private bool wasOff = true;
		//private void off()
		//{
		//	if (wasOff)
		//		return;
		//	wasOff = true;

		//	//canSendTo = new HashSet<Antenna>();
		//}

		public override void UpdateAfterSimulation10()
		{
			if (isClosed || Closed)
				return;

			try
			{
				if (!myBeacon.IsWorking)
				{
					log("beacon is off", "UpdateAfterSimulation100()", Logger.severity.TRACE);
				//	off();
					return;
				}
				log("beacon is on", "UpdateAfterSimulation100()", Logger.severity.TRACE);
				//wasOff = false;

				LinkedList<Antenna> canSeeMe = new LinkedList<Antenna>(); // friend and foe alike

				float radiusSquared = myBeacon.Radius * myBeacon.Radius;
				foreach (Antenna ant in Antenna.registry)
					if (Antenna.canSendToAnt(CubeBlock, ant, radiusSquared))
						canSeeMe.AddLast(ant);

				LastSeen self = new LastSeen(CubeBlock.CubeGrid);
				foreach (Antenna ant in canSeeMe)
					ant.receive(self);

				//if (canSeeMe.Count != 0)
				//{
				//	// send
				//}
			}
			catch (Exception e)
			{ alwaysLog("Exception: " + e, "UpdateAfterSimulation100()", Logger.severity.ERROR); }
		}



		public override string ToString()
		{ return CubeBlock.CubeGrid.DisplayName + "-" + myBeacon.DisplayNameText; }

		private Logger myLogger;
		[System.Diagnostics.Conditional("LOG_ENABLED")]
		private void log(string toLog, string method = null, Logger.severity level = Logger.severity.DEBUG)
		{ alwaysLog(toLog, method, level); }
		private void alwaysLog(string toLog, string method = null, Logger.severity level = Logger.severity.DEBUG)
		{
			if (myLogger == null)
				myLogger = new Logger(CubeBlock.CubeGrid.DisplayName, "Beacon");
			myLogger.log(level, method, toLog, CubeBlock.DisplayNameText);
		}
	}
}