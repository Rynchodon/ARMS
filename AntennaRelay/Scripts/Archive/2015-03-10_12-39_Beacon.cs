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
	public class Beacon : UpdateEnforcer
	{
		internal bool isRadar = false;

		private const string SubTypeSearchRadar = "Radar";

		private IMyCubeBlock CubeBlock;
		private Ingame.IMyBeacon myBeacon;

		protected override void DelayedInit()
		{
			CubeBlock = Entity as IMyCubeBlock;
			myBeacon = Entity as Ingame.IMyBeacon;

			if (Settings.boolSettings[Settings.BoolSetName.bAllowRadar] && CubeBlock.BlockDefinition.SubtypeName.Contains(SubTypeSearchRadar))
			{
				isRadar = true;
				log("init as radar: " + CubeBlock.BlockDefinition.SubtypeName, "Init()", Logger.severity.TRACE);
			}
			else
				log("init as beacon: " + CubeBlock.BlockDefinition.SubtypeName, "Init()", Logger.severity.TRACE);
			EnforcedUpdate = MyEntityUpdateEnum.EACH_100TH_FRAME;
		}

		//private bool isClosed = false;
		public override void Close()
		{
			//radarCanSee = null;
			CubeBlock = null;
			myBeacon = null;
			MyObjectBuilder = null;
			//isClosed = true;
		}

		/// <summary>
		/// power ratio is (mass + A) / (mass + B) / C
		/// </summary>
		private const float radarPower_A = 10000, radarPower_B = 100000, radarPower_C = 2;

		public override void UpdateAfterSimulation100()
		{
			if (!IsInitialized) return;
			if (Closed || CubeBlock == null || CubeBlock.CubeGrid == null) return;
			try
			{
				if (!myBeacon.IsWorking)
					return;

				// send beacon self to radio antenna
				LinkedList<RadioAntenna> canSeeMe = new LinkedList<RadioAntenna>(); // friend and foe alike

				float radiusSquared = myBeacon.Radius * myBeacon.Radius;
				foreach (RadioAntenna ant in RadioAntenna.registry)
					if (CubeBlock.canSendTo(ant.CubeBlock, false, radiusSquared, true))
						canSeeMe.AddLast(ant);

				LastSeen self = new LastSeen(CubeBlock.CubeGrid, isRadar);
				foreach (RadioAntenna ant in canSeeMe)
					ant.receive(self);

				//log("beacon made self known to "+canSeeMe.Count+" antennas", "UpdateAfterSimulation100()", Logger.severity.TRACE);

				if (!isRadar)
					return;

				// Radar

				// figure out what radar sees
				LinkedList<LastSeen> radarSees = new LinkedList<LastSeen>();

				HashSet<IMyEntity> allEntitiesInWorld = new HashSet<IMyEntity>();
				MyAPIGateway.Entities.GetEntities(allEntitiesInWorld);
				foreach (IMyEntity ent in allEntitiesInWorld)
					if (ent is IMyCubeGrid)
					{
						// get detection distance
						float volume = ent.LocalAABB.Volume();
						float power = (volume + radarPower_A) / (volume + radarPower_B) / radarPower_C * myBeacon.Radius;
						if (!CubeBlock.canSendTo(ent, false, power)) // not in range
							continue;

						//log("radar found a grid: " + (ent as IMyCubeGrid).DisplayName, "UpdateAfterSimulation100()", Logger.severity.TRACE);

						// report to attached antennas and remotes
						LastSeen seen = new LastSeen(ent, false, new RadarInfo(volume));
						radarSees.AddLast(seen);

						//foreach (RadioAntenna ant in RadioAntenna.registry)
						//	if (CubeBlock.canSendTo(ant.CubeBlock, true))
						//		ant.receive(seen);
						//foreach (RemoteControl rem in RemoteControl.registry.Values)
						//	if (CubeBlock.canSendTo(rem.CubeBlock, true))
						//		rem.receive(seen);
					}

				Receiver.sendToAttached(CubeBlock, radarSees);
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
		protected override void alwaysLog(string toLog, string method = null, Logger.severity level = Logger.severity.DEBUG)
		{
			if (myLogger == null)
				myLogger = new Logger(CubeBlock.CubeGrid.DisplayName, "Beacon");
			myLogger.log(level, method, toLog, CubeBlock.DisplayNameText);
		}
	}
}