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

//using Rynchodon.Update;

namespace Rynchodon.AntennaRelay
{
	//[MyEntityComponentDescriptor(typeof(MyObjectBuilder_Beacon))]
	//public class Beacon : UpdateEnforcer
	public class Beacon //: EntityScript<IMyCubeBlock>
	{
		internal readonly bool isRadar = false;

		private const string SubTypeSearchRadar = "Radar";

		private IMyCubeBlock CubeBlock;
		private Ingame.IMyBeacon myBeacon;

		public Beacon(IMyCubeBlock block)
		{
			CubeBlock = block;
			myBeacon = block as Ingame.IMyBeacon;

			isSmallBlock =  block.CubeGrid.GridSizeEnum == MyCubeSize.Small;
			if (Settings.boolSettings[Settings.BoolSetName.bAllowRadar] && CubeBlock.BlockDefinition.SubtypeName.Contains(SubTypeSearchRadar))
			{
				isRadar = true;
				log("init as radar: " + CubeBlock.BlockDefinition.SubtypeName, "Init()", Logger.severity.TRACE);
			}
			else
				log("init as beacon: " + CubeBlock.BlockDefinition.SubtypeName, "Init()", Logger.severity.TRACE);
			//UpdateManager.RegisterForUpdates(100, UpdateAfterSimulation100);

			CubeBlock.OnClosing += Close;
		}

		//private bool isClosed = false;
		private void Close(IMyEntity myEntity)
		{
			//radarCanSee = null;
			CubeBlock = null;
			myBeacon = null;
			//MyObjectBuilder = null;
			//isClosed = true;
		}

		/// <summary>Is this a small block?</summary>
		private readonly bool isSmallBlock;
		/// <summary>radius of small radar will be forced down to this number</summary>
		private const float maxRadiusSmallRadar = 20000;

		/// <summary>While radar has power, its powerLevel will increase up to its radius</summary>
		private float powerLevel = 0;
		/// <summary>Power change per 100 updates while on</summary>
		private const float powerIncrease = 1000;
		/// <summary>Power change per 100 updates while off</summary>
		private const float powerDecrease = -2000;

		/// <summary>
		/// power ratio is (mass + A) / (mass + B) / C
		/// </summary>
		private const float radarPower_A = 10000, radarPower_B = 100000, radarPower_C = 2;

		public void UpdateAfterSimulation100()
		{
			//if (!IsInitialized) return;
			if (CubeBlock == null || CubeBlock.Closed || CubeBlock.CubeGrid == null) return;
			try
			{
				if (!myBeacon.IsWorking)
				{
					if (isRadar && powerLevel > 0)
						powerLevel += powerDecrease;
					return;
				}

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

				float Radius = myBeacon.Radius;

				// cap small radar
				if (isSmallBlock)
				{
					if (Radius > maxRadiusSmallRadar)
					{
						myLogger.debugLog("Reduce radius from " + Radius + " to " + maxRadiusSmallRadar, "UpdateAfterSimulation100()");
						myBeacon.SetValueFloat("Radius", maxRadiusSmallRadar);
						Radius = maxRadiusSmallRadar;
					}
				}

				// adjust power level
				if (powerLevel < 0)
					powerLevel = 0;
				powerLevel += powerIncrease;
				if (powerLevel > Radius)
					powerLevel = Radius;
				myLogger.debugLog("Radius = " + Radius + ", power level = " + powerLevel, "UpdateAfterSimulation100()");

				// figure out what radar sees
				LinkedList<LastSeen> radarSees = new LinkedList<LastSeen>();

				HashSet<IMyEntity> allGrids = new HashSet<IMyEntity>();
				MyAPIGateway.Entities.GetEntities_Safe(allGrids, (entity) => { return entity is IMyCubeGrid; });
				foreach (IMyEntity ent in allGrids)
				//if (ent is IMyCubeGrid || ent is IMyCharacter)
				{
					if (!ent.Save)
						continue;

					// get detection distance
					float volume = ent.LocalAABB.Volume();
					float power = (volume + radarPower_A) / (volume + radarPower_B) / radarPower_C * powerLevel;
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
		private void alwaysLog(string toLog, string method = null, Logger.severity level = Logger.severity.DEBUG)
		{
			if (myLogger == null)
				myLogger = new Logger(CubeBlock.CubeGrid.DisplayName, "Beacon");
			myLogger.log(level, method, toLog, CubeBlock.DisplayNameText);
		}
	}
}