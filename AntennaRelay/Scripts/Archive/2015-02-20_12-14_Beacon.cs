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

		private const string SubTypeRadarLarge = "LargeBlockRadar";
		private const string SubTypeRadarSmall = "SmallBlockRadar";

		private IMyCubeBlock CubeBlock;
		private Ingame.IMyBeacon myBeacon;

		protected override void DelayedInit()
		{
			CubeBlock = Entity as IMyCubeBlock;
			myBeacon = Entity as Ingame.IMyBeacon;

			switch (CubeBlock.BlockDefinition.SubtypeName)
			{
				case SubTypeRadarLarge:
				case SubTypeRadarSmall:
					isRadar = true;
					log("init as radar: " + CubeBlock.BlockDefinition.SubtypeName, "Init()", Logger.severity.TRACE);
					break;
				default:
					log("init as beacon: " + CubeBlock.BlockDefinition.SubtypeName, "Init()", Logger.severity.TRACE);
					break;
			}
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
			if (Closed) return;
			try
			{
				if (!myBeacon.IsWorking)
					return;

				LinkedList<Antenna> canSeeMe = new LinkedList<Antenna>(); // friend and foe alike

				float radiusSquared = myBeacon.Radius * myBeacon.Radius;
				foreach (Antenna ant in Antenna.registry)
					if (CubeBlock.canSendTo(ant.CubeBlock, radiusSquared, true))
						canSeeMe.AddLast(ant);

				LastSeen self = new LastSeen(CubeBlock.CubeGrid);
				foreach (Antenna ant in canSeeMe)
					ant.receive(self);

				//log("beacon made self known to "+canSeeMe.Count+" antennas", "UpdateAfterSimulation100()", Logger.severity.TRACE);

				if (!isRadar)
					return;

				// Radar

				// figure out what radar sees
				HashSet<IMyEntity> allEntitiesInWorld = new HashSet<IMyEntity>();
				MyAPIGateway.Entities.GetEntities(allEntitiesInWorld);
				foreach (IMyEntity ent in allEntitiesInWorld)
					if (ent is IMyCubeGrid)
					{
						// get detection distance
						float mass = ent.Physics.Mass;
						float power = (mass + radarPower_A) / (mass + radarPower_B) / radarPower_C * myBeacon.Radius;
						if (!CubeBlock.canSendTo(ent, power)) // not in range
							continue;

						log("radar found a grid: " + (ent as IMyCubeGrid).DisplayName, "UpdateAfterSimulation100()", Logger.severity.TRACE);

						// report to attached antennas and remotes
						LastSeen seen = new LastSeen(ent);
						foreach (Antenna ant in Antenna.registry)
							if (CubeBlock.canConsiderFriendly(ant.CubeBlock) && CubeBlock.canSendTo(ant.CubeBlock))
								ant.receive(seen);
						foreach (ARRemoteControl rem in ARRemoteControl.registry.Values)
							if (CubeBlock.canConsiderFriendly(rem.CubeBlock) && CubeBlock.canSendTo(rem.CubeBlock))
								rem.receive(seen);
					}
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