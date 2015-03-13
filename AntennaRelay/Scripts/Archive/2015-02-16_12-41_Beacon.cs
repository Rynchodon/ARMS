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

		//private HashSet<IMyEntity> radarCanSee; // only init if this is radar

		private IMyCubeBlock CubeBlock;
		private Ingame.IMyBeacon myBeacon;

		public override void Init(MyObjectBuilder_EntityBase objectBuilder)
		{
			MyObjectBuilder = objectBuilder;
			CubeBlock = Entity as IMyCubeBlock;
			myBeacon = Entity as Ingame.IMyBeacon;

			switch (CubeBlock.BlockDefinition.SubtypeName)
			{
				case SubTypeRadarLarge:
				case SubTypeRadarSmall:
					isRadar = true;
					//radarCanSee = new HashSet<IMyEntity>();
					return;
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

		public override void UpdateAfterSimulation100()
		{
			if (Closed)
				return;
			try
			{
				if (!myBeacon.IsWorking)
					return;

				LinkedList<Antenna> canSeeMe = new LinkedList<Antenna>(); // friend and foe alike

				float radiusSquared = myBeacon.Radius * myBeacon.Radius;
				foreach (Antenna ant in Antenna.registry)
					if (CubeBlock.canSendTo(ant.CubeBlock, radiusSquared))
						canSeeMe.AddLast(ant);

				LastSeen self = new LastSeen(CubeBlock.CubeGrid);
				foreach (Antenna ant in canSeeMe)
					ant.receive(self);

				//log("beacon made self known to "+canSeeMe.Count+" antennas", "UpdateAfterSimulation100()", Logger.severity.TRACE);

				if (!isRadar)
					return;

				// figure out what radar sees
				LinkedList<IMyEntity> radarCanSee = new LinkedList<IMyEntity>();

				HashSet<IMyEntity> allEntitiesInWorld = new HashSet<IMyEntity>();
				MyAPIGateway.Entities.GetEntities(allEntitiesInWorld);
				foreach (IMyEntity e in allEntitiesInWorld)
					if (e is IMyCubeGrid || e is IMyPlayer)
						radarCanSee.AddLast(e);

				// report to attached antennas and remotes
				foreach (Antenna ant in Antenna.registry)
					if (AttachedGrids.isGridAttached(CubeBlock.CubeGrid, ant.CubeBlock.CubeGrid))

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