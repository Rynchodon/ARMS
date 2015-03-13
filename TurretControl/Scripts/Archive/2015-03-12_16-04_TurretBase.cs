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

	public class TurretBase : UpdateEnforcer
	{
		private IMyCubeBlock myCubeBlock;
		private Ingame.IMyLargeTurretBase myTurretBase;
		/// <summary>
		/// updated every Update
		/// </summary>
		private Vector3D turretPosition;

		private List<Func<Vector3D, IMyEntity>> targetGetters = new List<Func<Vector3D, IMyEntity>>();

		protected override void DelayedInit()
		{
			if (!Settings.boolSettings[Settings.BoolSetName.bAllowTurretControl])
				return;

			myCubeBlock = Entity as IMyCubeBlock;
			myTurretBase = Entity as Ingame.IMyLargeTurretBase;
			myLogger = new Logger(myCubeBlock.CubeGrid.DisplayName, "TurretBase", myCubeBlock.DisplayNameText);

			myLogger.log("created for: " + myCubeBlock.DisplayNameText, ".ctor()");
			EnforcedUpdate = Sandbox.Common.MyEntityUpdateEnum.EACH_FRAME; // missiles travel >13m per update, need to test often
			myTurretBase.SetCustomName(myTurretBase.DisplayNameText + " []");
		}

		public override void Close()
		{
			myCubeBlock = null;
		}

		private byte updateCount = 0;

		private float
			limit_el_min = 0,
			limit_el_max = 0,
			limit_az_min = 0,
			limit_az_max = 0;

		private bool targetMissiles, targetMeteors, targetPlayers;

		private enum priorities : byte { MISSILE, METEOR, PLAYER, BLOCK };

		public override void UpdateAfterSimulation()
		{
			if (!IsInitialized)
				return;
			try
			{
				//turretPosition = myCubeBlock.GetPosition();

				if (updateCount % 10 == 0)
					getValidTargets();

				IMyEntity bestTarget;
				if (getBestTarget(out bestTarget))
				{
					myLogger.debugLog("best target = " + bestTarget.getBestName(), "UpdateAfterSimulation100()", Logger.severity.TRACE);
					myTurretBase.RequestEnable(true);
					myTurretBase.TrackTarget(bestTarget);
				}
				else
				{
					myLogger.debugLog("no target", "UpdateAfterSimulation100()", Logger.severity.TRACE);
					myTurretBase.RequestEnable(false);
				}

				//IMyEntity target = null;
				//foreach (Func<Vector3D, IMyEntity> function in targetGetters)
				//{
				//	target = function.Invoke(turretPosition);
				//	if (target != null)
				//		break;
				//}
				//if (target != null)
				//	myLogger.debugLog("target = " + target.getBestName(), "UpdateAfterSimulation100()");


				//myLogger.debugLog("elevation = " + myTurretBase.Elevation + ", azimuth = " + myTurretBase.Azimuth, "UpdateAfterSimulation100()", Logger.severity.TRACE, myCubeBlock.DisplayNameText);
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
			updateCount++;
		}

		//private IMyEntity closestInRange(ICollection<IMyEntity> registry)
		//{
		//	IMyEntity closest = null;
		//	double closestDistanceSquared = myTurretBase.Range * myTurretBase.Range;
		//	foreach (IMyEntity entity in registry)
		//	{
		//		double distanceSquared = (entity.GetPosition() - turretPosition).LengthSquared();
		//		if (distanceSquared < closestDistanceSquared)
		//		{
		//			closestDistanceSquared = distanceSquared;
		//			closest = entity;
		//		}
		//	}
		//	return closest;
		//}

		private List<IMyEntity> validTarget_missile, validTarget_meteor, validTarget_player, validTarget_block;

		/// <summary>
		/// Fills validTarget_*
		/// </summary>
		private void getValidTargets()
		{
			MyObjectBuilder_TurretBase builder = myCubeBlock.getSlimObjectBuilder() as MyObjectBuilder_TurretBase;
			targetMissiles = builder.TargetMissiles;
			targetMeteors = builder.TargetMeteors;
			targetPlayers = builder.TargetMoving;

			BoundingSphereD range = new BoundingSphereD(turretPosition, myTurretBase.Range + 200);
			List<IMyEntity> entitiesInRange = MyAPIGateway.Entities.GetEntitiesInSphere(ref range);

			foreach (IMyEntity entity in entitiesInRange)
			{
				if (entity is IMyMeteor)
					if (targetMeteors)
						validTarget_meteor.Add(entity);
				else if (entity is IMyPlayer)
					if (targetPlayers)
						validTarget_player.Add(entity);
				else if (entity is IMyCubeGrid) { } // don't shoot grids, it's not nice!
				else if (entity is IMyCubeBlock)
					validTarget_block.Add(entity);
				else if (entity.ToString().StartsWith("MyMissile"))
					if (targetMissiles)
						validTarget_missile.Add(entity);
			}

			//MyObjectBuilder_TurretBase builder = myCubeBlock.getSlimObjectBuilder() as MyObjectBuilder_TurretBase;
			//if (builder.TargetMissiles)
			//{
			//	myLogger.debugLog("targeting missiles currently "+TrackerMissile.registry.Count+" exist", "getPriorities()");
			//	targetGetters.Add(turretPosition => { return closestInRange(turretPosition, TrackerMissile.registry); });
			//}
			//if (builder.TargetMeteors)
			//{
			//	myLogger.debugLog("targeting meteors currently " + TrackerMeteor.registry.Count + " exist", "getPriorities()");
			//	targetGetters.Add(turretPosition => { return closestInRange(turretPosition, TrackerMeteor.registry); });
			//}
			//if (builder.TargetMoving)
			//{
			//	myLogger.debugLog("targeting players currently " + TrackerPlayer.registry.Count + " exist", "getPriorities()");
			//	targetGetters.Add(turretPosition => { return closestInRange(turretPosition, TrackerPlayer.registry); });
			//}
		}

		/// <summary>
		/// Responsible for prioritizing. Missile, Meteor, Player, Decoy, Other Blocks
		/// </summary>
		/// <param name="bestTarget"></param>
		/// <returns></returns>
		private bool getBestTarget(out IMyEntity bestTarget)
		{
			if (getClosest(validTarget_missile, out bestTarget))
				return true;
			if (getClosest(validTarget_meteor, out bestTarget))
				return true;
			if (getClosest(validTarget_player, out bestTarget))
				return true;

			// blocks

			return false;
		}

		private bool getClosest(List<IMyEntity> entities, out IMyEntity closest)
		{
			closest = null;
			if (entities.Count == 0)
				return false;

			double closestDistanceSquared = myTurretBase.Range * myTurretBase.Range;

			foreach (IMyEntity entity in entities)
			{
				double distanceSquared = (entity.GetPosition() - turretPosition).LengthSquared();
				if (distanceSquared < closestDistanceSquared)
				{
					closestDistanceSquared = distanceSquared;
					closest = entity;
				}
			}

			return closest != null;
		}

		//// easiest course of action is too just change targets if guns have not fired for x time
		//private IMyTerminalBlock getBestTarget()
		//{
		//	// get priorities

		//	// find grids
		//	BoundingSphereD toSearch = new BoundingSphereD(myCubeBlock.GetPosition(), 500);
		//	List<IMyEntity> inRange = MyAPIGateway.Entities.GetEntitiesInSphere_Safe(ref toSearch);

		//	IMyTerminalBlock bestTarget = null;
		//	foreach (IMyEntity entity in inRange)
		//	{
		//		//myLogger.debugLog("got entitiy: " + entity.getBestName(), "getBestTarget()", Logger.severity.TRACE);
		//		IMyCubeGrid grid = entity as IMyCubeGrid;
		//		if (grid == null)
		//			continue;

		//		// find targets
		//		IReadOnlyCollection<IMyTerminalBlock> targets = CubeGridCache.GetFor(grid).GetBlocksByDefLooseContains("Radar");
		//		if (targets == null)
		//			continue;

		//		// get best target (line-of-sight, highest priority, closest)
		//		foreach (IMyTerminalBlock target in targets)
		//		{
		//			if (!target.IsWorking)
		//				continue;

		//			//myLogger.debugLog("got target: " + target.DisplayNameText, "getBestTarget()", Logger.severity.TRACE);
		//			bestTarget = target;
		//			return bestTarget;
		//		}
		//	}
		//	return bestTarget;
		//}

		private void logElAz()
		{
			if (myTurretBase.Elevation < limit_el_min)
			{
				limit_el_min = myTurretBase.Elevation;
				myLogger.debugLog("limit_el_min = " + limit_el_min + ", limit_el_max = " + limit_el_max + ", limit_az_min = " + limit_az_min + ", limit_az_max = " + limit_az_max, "UpdateAfterSimulation100()", Logger.severity.TRACE, myCubeBlock.DisplayNameText);
			}
			else if (myTurretBase.Elevation > limit_el_max)
			{
				limit_el_max = myTurretBase.Elevation;
				myLogger.debugLog("limit_el_min = " + limit_el_min + ", limit_el_max = " + limit_el_max + ", limit_az_min = " + limit_az_min + ", limit_az_max = " + limit_az_max, "UpdateAfterSimulation100()", Logger.severity.TRACE, myCubeBlock.DisplayNameText);
			}
			if (myTurretBase.Azimuth < limit_az_min)
			{
				limit_az_min = myTurretBase.Azimuth;
				myLogger.debugLog("limit_el_min = " + limit_el_min + ", limit_el_max = " + limit_el_max + ", limit_az_min = " + limit_az_min + ", limit_az_max = " + limit_az_max, "UpdateAfterSimulation100()", Logger.severity.TRACE, myCubeBlock.DisplayNameText);
			}
			else if (myTurretBase.Azimuth > limit_az_max)
			{
				limit_az_max = myTurretBase.Azimuth;
				myLogger.debugLog("limit_el_min = " + limit_el_min + ", limit_el_max = " + limit_el_max + ", limit_az_min = " + limit_az_min + ", limit_az_max = " + limit_az_max, "UpdateAfterSimulation100()", Logger.severity.TRACE, myCubeBlock.DisplayNameText);
			}
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
