using System;
using System.Collections.Generic;
//using System.Linq;
//using System.Text;

using Sandbox.Common.Components;
using Sandbox.Common.ObjectBuilders;
using Sandbox.ModAPI;
using Ingame = Sandbox.ModAPI.Ingame;

using VRageMath;

namespace Rynchodon.Autopilot.Turret
{
	[MyEntityComponentDescriptor(typeof(MyObjectBuilder_LargeGatlingTurret))]
	public class TurretLargeGatling : TurretBase { }

	//[MyEntityComponentDescriptor(typeof(MyObjectBuilder_LargeMissileTurret))]
	//public class TurretLargeRocket : TurretBase { }

	[MyEntityComponentDescriptor(typeof(MyObjectBuilder_LargeGatlingTurret))]
	public class TurretBase : UpdateEnforcer
	{
		private IMyCubeBlock myCubeBlock;
		private Ingame.IMyLargeTurretBase myTurretBase;

		private bool targetMissiles, targetMeteors, targetPlayers;

		protected override void DelayedInit()
		{
			myCubeBlock = Entity as IMyCubeBlock;
			myTurretBase = Entity as Ingame.IMyLargeTurretBase;
			myLogger = new Logger(myCubeBlock.CubeGrid.DisplayName, "TurretBase", myCubeBlock.DisplayNameText);

			myLogger.log("created for: " + myCubeBlock.DisplayNameText, ".ctor()");
			EnforcedUpdate = Sandbox.Common.MyEntityUpdateEnum.EACH_FRAME; // missiles travel >13m per update, need to test often
		}

		public override void Close()
		{
			myCubeBlock = null;
		}

		public override void UpdateAfterSimulation()
		{
			try
			{
				myLogger.debugLog("elevation = " + myTurretBase.Elevation + ", azimuth = " + myTurretBase.Azimuth, "UpdateAfterSimulation100()", Logger.severity.TRACE, myCubeBlock.DisplayNameText);
				//IMyTerminalBlock bestTarget = getBestTarget();
				//if (bestTarget == null)
				//{
				//	//myLogger.debugLog("no target", "UpdateAfterSimulation100()", Logger.severity.TRACE);
				//	myTurretBase.RequestEnable(false);
				//}
				//else
				//{
				//	//myLogger.debugLog("engaging: " + bestTarget.DisplayNameText, "UpdateAfterSimulation100()", Logger.severity.TRACE);
				//	myTurretBase.RequestEnable(true);
				//	myTurretBase.TrackTarget(bestTarget);
				//}
			}
			catch (Exception e) { alwaysLog("Exception: " + e, "UpdateAfterSimulation100()", Logger.severity.ERROR); }
		}

		/// <summary>
		/// Priorities shall be Missiles, Meteors, Players, Decoys, Given Priorities
		/// </summary>
		/// <returns></returns>
		private void getPriorities()
		{
			MyObjectBuilder_TurretBase builder = myCubeBlock.getSlimObjectBuilder() as MyObjectBuilder_TurretBase;
			targetMissiles = builder.TargetMissiles;
			targetMeteors = builder.TargetMeteors;
			targetPlayers = builder.TargetMoving;


		}

		// easiest course of action is too just change targets if guns have not fired for x time
		private IMyTerminalBlock getBestTarget()
		{
			// get priorities

			// find grids
			BoundingSphereD toSearch = new BoundingSphereD(myCubeBlock.GetPosition(), 500);
			List<IMyEntity> inRange = MyAPIGateway.Entities.GetEntitiesInSphere_Safe(ref toSearch);

			IMyTerminalBlock bestTarget = null;
			foreach (IMyEntity entity in inRange)
			{
				//myLogger.debugLog("got entitiy: " + entity.getBestName(), "getBestTarget()", Logger.severity.TRACE);
				IMyCubeGrid grid = entity as IMyCubeGrid;
				if (grid == null)
					continue;

				// find targets
				IReadOnlyCollection<IMyTerminalBlock> targets = CubeGridCache.GetFor(grid).GetBlocksByDefLooseContains("Radar");
				if (targets == null)
					continue;

				// get best target (line-of-sight, highest priority, closest)
				foreach (IMyTerminalBlock target in targets)
				{
					if (!target.IsWorking)
						continue;

					//myLogger.debugLog("got target: " + target.DisplayNameText, "getBestTarget()", Logger.severity.TRACE);
					bestTarget = target;
					return bestTarget;
				}
			}
			return bestTarget;
		}



		private Logger myLogger;
		protected override void alwaysLog(string toLog, string method = null, Logger.severity level = Logger.severity.DEBUG)
		{
			if (myLogger == null)
				myLogger = new Logger(null, "TurretBase");
			myLogger.log(toLog, method, level);
		}
	}
}
