using System;
using System.Collections.Generic;
//using System.Linq;
//using System.Text;

using Sandbox.Common.Components;
using Sandbox.Common.ObjectBuilders;
using Sandbox.Definitions;
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
			EnforcedUpdate = Sandbox.Common.MyEntityUpdateEnum.EACH_10TH_FRAME; // missiles travel >13m per update, need to test often
			if (!(myTurretBase.DisplayNameText.Contains("[") || myTurretBase.DisplayNameText.Contains("]")))
				myTurretBase.SetCustomName(myTurretBase.DisplayNameText + " []");

			// definition limits
			var definition = MyDefinitionManager.Static.GetCubeBlockDefinition(myCubeBlock.getSlimObjectBuilder()) as MyLargeTurretBaseDefinition;
			myLogger.debugLog("definition limits = " +definition.MinElevationDegrees+", "+definition.MaxElevationDegrees+", "+definition.MinAzimuthDegrees+", "+definition.MaxAzimuthDegrees, "DelayedInit()");
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

		private bool targetMissiles, targetMeteors, targetCharacters;

		private enum priorities : byte { MISSILE, METEOR, PLAYER, BLOCK };

		public override void UpdateAfterSimulation10()
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
					//myLogger.debugLog("no target", "UpdateAfterSimulation100()", Logger.severity.TRACE);
					myTurretBase.RequestEnable(false);
				}
			}
			catch (Exception e) { alwaysLog("Exception: " + e, "UpdateAfterSimulation100()", Logger.severity.ERROR); }
			updateCount++;
		}

		private List<IMyEntity> validTarget_missile, validTarget_meteor, validTarget_character, validTarget_block;

		/// <summary>
		/// Fills validTarget_*
		/// </summary>
		private void getValidTargets()
		{
			MyObjectBuilder_TurretBase builder = myCubeBlock.getSlimObjectBuilder() as MyObjectBuilder_TurretBase;
			targetMissiles = builder.TargetMissiles;
			targetMeteors = builder.TargetMeteors;
			targetCharacters = builder.TargetMoving;

			BoundingSphereD range = new BoundingSphereD(turretPosition, myTurretBase.Range + 200);
			List<IMyEntity> entitiesInRange = MyAPIGateway.Entities.GetEntitiesInSphere(ref range);

			validTarget_missile = new List<IMyEntity>();
			validTarget_meteor = new List<IMyEntity>();
			validTarget_character = new List<IMyEntity>();
			validTarget_block = new List<IMyEntity>();

			myLogger.debugLog("initial entity count is " + entitiesInRange.Count, "getValidTargets()");

			foreach (IMyEntity entity in entitiesInRange)
			{
				if (entity is IMyCubeBlock)
				{
					if (myCubeBlock.canConsiderHostile(entity as IMyCubeBlock))
						validTarget_block.Add(entity);
				}
				else if (entity is IMyCubeGrid) { } // don't shoot grids, it's not nice!
				else if (entity is IMyMeteor)
				{
					if (targetMeteors)
						validTarget_meteor.Add(entity);
				}
				else if (entity is IMyCharacter)
				{
					//myLogger.debugLog("found a character: " + entity.DisplayName, "getValidTargets()");
					if (targetCharacters)
					{
						List<IMyPlayer> matchingPlayer = new List<IMyPlayer>();
						MyAPIGateway.Players.GetPlayers(matchingPlayer, player => { return player.DisplayName == entity.DisplayName; });
						//foreach (var player in matchingPlayer)
						//	myLogger.debugLog("found a player: " + player.DisplayName, "getValidTargets()");
						if (matchingPlayer.Count == 1 && myCubeBlock.canConsiderHostile(matchingPlayer[0].PlayerID))
							validTarget_character.Add(entity);
					}
				}
				else if (entity.ToString().StartsWith("MyMissile"))
					if (targetMissiles)
						validTarget_missile.Add(entity);
			}

			myLogger.debugLog("target counts = " + validTarget_missile.Count + ", " + validTarget_meteor.Count + ", " + validTarget_character.Count + ", " + validTarget_block.Count, "getValidTargets()");
		}

		/// <summary>
		/// Responsible for prioritizing. Missile, Meteor, Character, Decoy, Other Blocks
		/// </summary>
		/// <param name="bestTarget"></param>
		/// <returns></returns>
		private bool getBestTarget(out IMyEntity bestTarget)
		{
			if (getClosest(validTarget_missile, out bestTarget))
				return true;
			if (getClosest(validTarget_meteor, out bestTarget))
				return true;
			if (getClosest(validTarget_character, out bestTarget))
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
