// TODO:
// determine if a turret can point at a target. MinElevationDegrees is unreliable
// Test missiles are going to pass near turret. Both to avoid shooting the wrong missiles and to avoid targeting missiles the turret cannot hit. Possibly within (projectile speed / 10) metres.

using System;
using System.Collections.Generic;
//using System.Linq;
//using System.Text;

using Sandbox.Common.Components;
using Sandbox.Common.ObjectBuilders;
using Sandbox.Definitions;
using Sandbox.ModAPI;
using Ingame = Sandbox.ModAPI.Ingame;
using Sandbox.ModAPI.Interfaces;

using VRage;
using VRageMath;

namespace Rynchodon.Autopilot.Turret
{
	[MyEntityComponentDescriptor(typeof(MyObjectBuilder_LargeGatlingTurret))]
	public class TurretLargeGatling : TurretBase { }

	[MyEntityComponentDescriptor(typeof(MyObjectBuilder_LargeMissileTurret))]
	public class TurretLargeRocket : TurretBase { }

	[MyEntityComponentDescriptor(typeof(MyObjectBuilder_InteriorTurret))]
	public class TurretInterior : TurretBase { }

	public class TurretBase : UpdateEnforcer
	{
		private IMyCubeBlock myCubeBlock;
		private Ingame.IMyLargeTurretBase myTurretBase;

		// definition limits, will be needed to determine whether or not a turret can face a target
		//private int MinElevationDegrees, MaxElevationDegrees, MinAzimuthDegrees, MaxAzimuthDegrees;

		private float missileRange;

		protected override void DelayedInit()
		{
			if (!Settings.boolSettings[Settings.BoolSetName.bAllowTurretControl])
				return;

			this.myCubeBlock = Entity as IMyCubeBlock;
			this.myTurretBase = Entity as Ingame.IMyLargeTurretBase;
			this.myLogger = new Logger(myCubeBlock.CubeGrid.DisplayName, "TurretBase", myCubeBlock.DisplayNameText);

			myLogger.log("created for: " + myCubeBlock.DisplayNameText, "DelayedInit()");
			EnforcedUpdate = Sandbox.Common.MyEntityUpdateEnum.EACH_10TH_FRAME; // missiles travel >13m per update, need to test often
			if (!(myTurretBase.DisplayNameText.Contains("[") || myTurretBase.DisplayNameText.Contains("]")))
				myTurretBase.SetCustomName(myTurretBase.DisplayNameText + " []");

			TurretBase_CustomNameChanged(null);
			(myCubeBlock as IMyTerminalBlock).CustomNameChanged += TurretBase_CustomNameChanged;

			// definition limits
			//var definition = MyDefinitionManager.Static.GetCubeBlockDefinition(myCubeBlock.getSlimObjectBuilder()) as MyLargeTurretBaseDefinition;
			//this.MinElevationDegrees = definition.MinElevationDegrees;
			//this.MaxElevationDegrees = definition.MaxElevationDegrees;
			//this.MinAzimuthDegrees = definition.MinAzimuthDegrees;
			//this.MaxAzimuthDegrees = definition.MaxAzimuthDegrees;
			//myLogger.debugLog("definition limits = " +definition.MinElevationDegrees+", "+definition.MaxElevationDegrees+", "+definition.MinAzimuthDegrees+", "+definition.MaxAzimuthDegrees, "DelayedInit()");
		}

		public override void Close()
		{
			IMyTerminalBlock asTerm = myCubeBlock as IMyTerminalBlock;
			if (asTerm != null)
				asTerm.CustomNameChanged -= TurretBase_CustomNameChanged;
			myCubeBlock = null;
			myTurretBase = null;
		}

		private bool enabled = false;

		private byte updateCount = 0;

		private bool targetMissiles, targetMeteors, targetCharacters;

		private enum priorities : byte { MISSILE, METEOR, PLAYER, BLOCK };

		private IMyEntity lastTarget;

		private static bool needToRelease = true;
		/// <summary>
		/// acquire a shared lock to perform actions on MyAPIGateway or use GetObjectBuilder()
		/// </summary>
		private static VRage.FastResourceLock lock_notMyUpdate = new VRage.FastResourceLock(); // must be static if we want to run one thread at a time

		public override void UpdateAfterSimulation10()
		{
			if (!IsInitialized)
				return;
			if (!myCubeBlock.IsFunctional)
				return;

			if (needToRelease)
				lock_notMyUpdate.ReleaseExclusive();
			needToRelease = false;

			try
			{
				if (updateCount >= 10)
				{
					MyObjectBuilder_TurretBase builder = myCubeBlock.getSlimObjectBuilder() as MyObjectBuilder_TurretBase;
					enabled = !builder.EnableIdleRotation && myCubeBlock.DisplayNameText.Contains("[") && myCubeBlock.DisplayNameText.Contains("]");

					//myLogger.debugLog("enabled = " + enabled + ", idle = " + builder.EnableIdleRotation + ", shooting = " + builder.IsShooting, "UpdateAfterSimulation10()");

					//uint firstAmmoID = (myTurretBase as IMyInventoryOwner).GetInventory(0).GetItems()[0].ItemId;
					//float ammoSpeed = MyDefinitionManager.Static.GetAmmoDefinition(new MyDefinitionId(firstAmmoID)).DesiredSpeed;

					//if (enabled)
					//	getValidTargets();

					updateCount = 0;
				}

				if (!enabled)
				{
					if (lastTarget != null)
					{
						lastTarget = null;
						myTurretBase.ResetTargetingToDefault();
						//myTurretBase.TrackTarget(null);
					}
					return;
				}

				// start thread
				MyAPIGateway.Parallel.Start(UpdateThread);
				//UpdateThread();
			}
			catch { }
			finally
			{
				updateCount++;
				needToRelease = true;
				lock_notMyUpdate.AcquireExclusive();
			}
		}

		/// <summary>
		/// updated every Update
		/// </summary>
		private Vector3D turretPosition;
		/// <summary>
		/// Missile ray must intersect bubble for turret to target it. updated every update
		/// </summary>
		private BoundingSphereD turretMissileBubble;

		private static VRage.FastResourceLock lock_update = new VRage.FastResourceLock(); // one at a time, please

		private bool currentTargetIsMissile = false;

		private void UpdateThread()
		{
			if (!lock_update.TryAcquireExclusive()) // too many Smart Turrets
			{
				myLogger.debugLog("too many smart turrets", "UpdateThread()");
				return;
			}

			try
			{
				turretPosition = myCubeBlock.GetPosition();
				turretMissileBubble = new BoundingSphereD(turretPosition, 30);

				double distToMissile;
				if (currentTargetIsMissile && missileIsHostile(lastTarget, out distToMissile))
					return; // no need to switch targets

				getValidTargets();

				IMyEntity bestTarget;
				if (getBestTarget(out bestTarget))
				{
					if (lastTarget != bestTarget)
					{
						lastTarget = bestTarget;
						myLogger.debugLog("new target = " + bestTarget.getBestName(), "UpdateThread()", Logger.severity.TRACE);
					}
					myTurretBase.RequestEnable(true);
					myTurretBase.TrackTarget(bestTarget);
				}
				else
				{
					//myLogger.debugLog("no target", "UpdateThread()", Logger.severity.TRACE);
					myTurretBase.RequestEnable(false);
				}
			}
			catch (Exception e) { alwaysLog("Exception: " + e, "UpdateThread()", Logger.severity.ERROR); }
			finally { lock_update.ReleaseExclusive(); }
		}

		private List<IMyEntity> validTarget_missile, validTarget_meteor, validTarget_character, validTarget_block;

		/// <summary>
		/// Fills validTarget_*
		/// </summary>
		private void getValidTargets()
		{
			List<IMyEntity> entitiesInRange;
			using (lock_notMyUpdate.AcquireSharedUsing())
			{
				MyObjectBuilder_TurretBase builder = myCubeBlock.getSlimObjectBuilder() as MyObjectBuilder_TurretBase;
				targetMissiles = builder.TargetMissiles;
				targetMeteors = builder.TargetMeteors;
				targetCharacters = builder.TargetMoving;

				BoundingSphereD range = new BoundingSphereD(turretPosition, myTurretBase.Range + 200);
				entitiesInRange = MyAPIGateway.Entities.GetEntitiesInSphere(ref range);
			}

			validTarget_missile = new List<IMyEntity>();
			validTarget_meteor = new List<IMyEntity>();
			validTarget_character = new List<IMyEntity>();
			validTarget_block = new List<IMyEntity>();

			//myLogger.debugLog("initial entity count is " + entitiesInRange.Count, "getValidTargets()");

			foreach (IMyEntity entity in entitiesInRange)
			{
				if (entity is IMyCubeBlock)
				{
					if (myCubeBlock.canConsiderHostile(entity as IMyCubeBlock))
						validTarget_block.Add(entity);
				}
				else if (entity is IMyCubeGrid) { } // don't shoot grids, it is not nice!
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
						using (lock_notMyUpdate.AcquireSharedUsing())
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

			//myLogger.debugLog("target counts = " + validTarget_missile.Count + ", " + validTarget_meteor.Count + ", " + validTarget_character.Count + ", " + validTarget_block.Count, "getValidTargets()");
		}

		/// <summary>
		/// Responsible for prioritizing. Missile, Meteor, Character, Decoy, Other Blocks
		/// </summary>
		/// <param name="bestTarget"></param>
		/// <returns></returns>
		private bool getBestTarget(out IMyEntity bestTarget)
		{
			currentTargetIsMissile = getBestMissile(out bestTarget);
			if (currentTargetIsMissile)
				return true;
			if (getClosest(validTarget_meteor, out bestTarget))
				return true;
			if (getClosest(validTarget_character, out bestTarget))
				return true;
			if (getBestBlock(out bestTarget))
				return true;

			return false;
		}

		private bool getClosest(List<IMyEntity> entities, out IMyEntity closest)//, bool missileTest = false)
		{
			closest = null;
			if (entities.Count == 0)
				return false;

			double closestDistanceSquared = myTurretBase.Range * myTurretBase.Range;

			foreach (IMyEntity entity in entities)
			{
				double distanceSquared;
				//if (missileTest)
				//	if (!missileIsHostile(entity, out distanceSquared))
				//	//{
				//		//myLogger.debugLog("ignoring missile, not a threat", "getClosest()");
				//		continue;
				//	//}
				distanceSquared = (entity.GetPosition() - turretPosition).LengthSquared();
				if (distanceSquared < closestDistanceSquared)
				{
					closestDistanceSquared = distanceSquared;
					closest = entity;
				}
			}

			return closest != null;
		}

		/// <summary>
		/// any hostile missile
		/// </summary>
		/// <param name="bestMissile"></param>
		/// <returns></returns>
		private bool getBestMissile(out IMyEntity bestMissile)
		{
			foreach (IMyEntity missile in validTarget_missile)
			{
				if (missile.Closed)
					continue;

				double toMissilePosition;
				if (missileIsHostile(missile, out toMissilePosition))
				{
					bestMissile = missile;
					return true;
				}
			}
			bestMissile = null;
			return false;
		}

		private bool getBestBlock(out IMyEntity bestMatch)
		{
			bestMatch = null;
			if (requestedBlocks == null || requestedBlocks.Length < 1)
				return false;

			double closestDistanceSquared = myTurretBase.Range * myTurretBase.Range;
			foreach (string requested in requestedBlocks)
			{
				foreach (IMyCubeBlock block in validTarget_block)
				{
					if (!block.IsWorking)
						continue;

					if (!block.DefinitionDisplayNameText.looseContains(requested))
						continue;

					double distanceSquared = (block.GetPosition() - turretPosition).LengthSquared();
					if (distanceSquared < closestDistanceSquared)
					{
						closestDistanceSquared = distanceSquared;
						bestMatch = block;
					}
				}
				if (bestMatch != null)
					return true;
			}

			return false;
		}

		private string[] requestedBlocks;

		private void TurretBase_CustomNameChanged(IMyTerminalBlock obj)
		{
			string instructions = myCubeBlock.getInstructions();
			//myLogger.debugLog("name changed to " + myCubeBlock.DisplayNameText + ", instructions = " + instructions, "TurretBase_CustomNameChanged()");
			if (instructions == null)
			{
				requestedBlocks = null;
				return;
			}
			requestedBlocks = instructions.Split(',');
			//myLogger.debugLog("requestedBlocks = " + requestedBlocks, "TurretBase_CustomNameChanged()");
		}

		/// <summary>
		/// approaching, going to intersect turretMissileBubble
		/// </summary>
		/// <returns></returns>
		private bool missileIsHostile(IMyEntity missile, out double toP0_lengthSquared)
		{
			Vector3D P0 = missile.GetPosition();
			Vector3D missileDirection = Vector3D.Normalize(missile.Physics.LinearVelocity);

			toP0_lengthSquared = (P0 - turretPosition).LengthSquared();
			double toP1_lengthSquared = (P0 + missileDirection - turretPosition).LengthSquared();
			if (toP0_lengthSquared < toP1_lengthSquared) // moving away
				return false;

			RayD missileRay = new RayD(P0, missileDirection);

			double? interscts = missileRay.Intersects(turretMissileBubble);
			if (interscts != null)
			{
				myLogger.debugLog("got a missile intersection of " + interscts, "getBestMissile()");
				return true;
			}
			return false;
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
