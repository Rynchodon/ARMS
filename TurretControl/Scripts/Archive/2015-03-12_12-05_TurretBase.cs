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

		public override void UpdateAfterSimulation()
		{
			if (!IsInitialized)
				return;
			try
			{
				if (updateCount % 100 == 0)
					getPriorities();

				IMyEntity target = null;
				Vector3D turretPosition = myCubeBlock.GetPosition();
				foreach (Func<Vector3D, IMyEntity> function in targetGetters)
				{
					target = function.Invoke(turretPosition);
					if (target != null)
						break;
				}
				if (target != null)
					myLogger.debugLog("target = " + target.getBestName(), "UpdateAfterSimulation100()");

				if (myTurretBase.Elevation < limit_el_min)
				{
					limit_el_min = myTurretBase.Elevation;
					myLogger.debugLog("elevation = " + myTurretBase.Elevation + ", azimuth = " + myTurretBase.Azimuth, "UpdateAfterSimulation100()", Logger.severity.TRACE, myCubeBlock.DisplayNameText);
				}
				else if (myTurretBase.Elevation > limit_el_max)
				{
					limit_el_max = myTurretBase.Elevation;
					myLogger.debugLog("elevation = " + myTurretBase.Elevation + ", azimuth = " + myTurretBase.Azimuth, "UpdateAfterSimulation100()", Logger.severity.TRACE, myCubeBlock.DisplayNameText);
				}
				if (myTurretBase.Azimuth < limit_az_min)
				{
					limit_az_min = myTurretBase.Azimuth;
					myLogger.debugLog("elevation = " + myTurretBase.Elevation + ", azimuth = " + myTurretBase.Azimuth, "UpdateAfterSimulation100()", Logger.severity.TRACE, myCubeBlock.DisplayNameText);
				}
				else if (myTurretBase.Azimuth > limit_az_max)
				{
					limit_az_max = myTurretBase.Azimuth;
					myLogger.debugLog("elevation = " + myTurretBase.Elevation + ", azimuth = " + myTurretBase.Azimuth, "UpdateAfterSimulation100()", Logger.severity.TRACE, myCubeBlock.DisplayNameText);
				}

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

		private IMyEntity closestInRange(Vector3D turretPosition, ICollection<IMyEntity> registry)
		{
			IMyEntity closest = null;
			double closestDistanceSquared = myTurretBase.Range * myTurretBase.Range;
			foreach (IMyEntity entity in registry)
			{
				double distanceSquared = (entity.GetPosition() - turretPosition).LengthSquared();
				if (distanceSquared < closestDistanceSquared)
				{
					closestDistanceSquared = distanceSquared;
					closest = entity;
				}
			}
			return closest;
		}

		/// <summary>
		/// Priorities shall be Missiles, Meteors, Players, Decoys, Given Priorities
		/// </summary>
		/// <returns></returns>
		private void getPriorities()
		{
			MyObjectBuilder_TurretBase builder = myCubeBlock.getSlimObjectBuilder() as MyObjectBuilder_TurretBase;
			if (builder.TargetMissiles)
			{
				myLogger.debugLog("targeting missiles", "getPriorities()");
				targetGetters.Add(turretPosition => { return closestInRange(turretPosition, TrackerMissile.registry); });
			}
			if (builder.TargetMeteors)
			{
				myLogger.debugLog("targeting meteors", "getPriorities()");
				targetGetters.Add(turretPosition => { return closestInRange(turretPosition, TrackerMeteor.registry); });
			}
			if (builder.TargetMoving)
			{
				myLogger.debugLog("targeting players", "getPriorities()");
				targetGetters.Add(turretPosition => { return closestInRange(turretPosition, TrackerPlayer.registry); });
			}
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
