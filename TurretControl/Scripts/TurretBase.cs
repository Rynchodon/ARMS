#define LOG_ENABLED //remove on build

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

using Rynchodon.AntennaRelay;

namespace Rynchodon.Autopilot.Turret
{
	[MyEntityComponentDescriptor(typeof(MyObjectBuilder_LargeGatlingTurret))]
	public class TurretLargeGatling : TurretBase { }

	[MyEntityComponentDescriptor(typeof(MyObjectBuilder_LargeMissileTurret))]
	public class TurretLargeRocket : TurretBase { }

	[MyEntityComponentDescriptor(typeof(MyObjectBuilder_InteriorTurret))]
	public class TurretInterior : TurretBase { }

	// TODO:
	// determine if a turret can point at a target, and is unobstructed. MinElevationDegrees is unreliable
	// Use projectile speed to determine turretMissileBubble radius.
	/// <summary>
	/// Turrets will be forced on initially, it is necissary for normal targeting to run briefly before we take control.
	/// </summary>
	public class TurretBase : UpdateEnforcer
	{
		private IMyCubeBlock myCubeBlock;
		private IMyTerminalBlock myTerminal;
		private Ingame.IMyLargeTurretBase myTurretBase;

		// definition limits, will be needed to determine whether or not a turret can face a target
		//private int MinElevationDegrees, MaxElevationDegrees, MinAzimuthDegrees, MaxAzimuthDegrees;

		private float missileRange;

		protected override void DelayedInit()
		{
			if (!Settings.boolSettings[Settings.BoolSetName.bTestingTurretControl])
				return;

			this.myCubeBlock = Entity as IMyCubeBlock;
			this.myTerminal = Entity as IMyTerminalBlock;
			this.myTurretBase = Entity as Ingame.IMyLargeTurretBase;
			this.myLogger = new Logger(myCubeBlock.CubeGrid.DisplayName, "TurretBase", myCubeBlock.DisplayNameText);

			myLogger.debugLog("created for: " + myCubeBlock.DisplayNameText, "DelayedInit()");
			EnforcedUpdate = Sandbox.Common.MyEntityUpdateEnum.EACH_FRAME; // want as many opportunities to lock main thread as possible
			if (!(myTurretBase.DisplayNameText.Contains("[") || myTurretBase.DisplayNameText.Contains("]")))
			{
				if (myTurretBase.OwnerId.Is_ID_NPC())
					myTurretBase.SetCustomName(myTurretBase.DisplayNameText + " " + Settings.stringSettings[Settings.StringSetName.sTurretDefaultNPC]);
				else
					myTurretBase.SetCustomName(myTurretBase.DisplayNameText + " " + Settings.stringSettings[Settings.StringSetName.sTurretDefaultPlayer]);
			}

			TurretBase_CustomNameChanged(null);
			myTerminal.CustomNameChanged += TurretBase_CustomNameChanged;
			myTerminal.OwnershipChanged += myTerminal_OwnershipChanged;

			myTurretBase.SyncAzimuth();
			myTurretBase.SyncElevation();
			myTurretBase.SyncEnableIdleRotation();

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
			base.Close();
			if (needToRelease)
				lock_notMyUpdate.ReleaseExclusive();

			IMyTerminalBlock asTerm = myCubeBlock as IMyTerminalBlock;
			if (asTerm != null)
				asTerm.CustomNameChanged -= TurretBase_CustomNameChanged;
			myCubeBlock = null;
			myTurretBase = null;
			controlEnabled = false;
		}

		private void TurretBase_CustomNameChanged(IMyTerminalBlock obj)
		{
			string instructions = myCubeBlock.getInstructions();
			//myLogger.debugLog("name changed to " + myCubeBlock.DisplayNameText + ", instructions = " + instructions, "TurretBase_CustomNameChanged()", Logger.severity.DEBUG);
			if (instructions == null)
			{
				requestedBlocks = null;
				return;
			}
			requestedBlocks = instructions.Split(',');
			//myLogger.debugLog("requestedBlocks = " + requestedBlocks, "TurretBase_CustomNameChanged()");
		}

		void myTerminal_OwnershipChanged(IMyTerminalBlock obj)
		{
			try { myTurretBase.ResetTargetingToDefault(); }
			catch (Exception e) { myLogger.debugLog("Exception: " + e, "myTerminal_OwnershipChanged()", Logger.severity.ERROR); }
		}

		private bool controlEnabled = false;

		private int updateCount = 1000;

		private bool targetMissiles, targetMeteors, targetCharacters;

		//private enum priorities : byte { MISSILE, METEOR, PLAYER, BLOCK };

		private IMyEntity lastTarget;

		private Receiver myAntenna;

		private static bool needToRelease = false;
		/// <summary>
		/// acquire a shared lock to perform actions on MyAPIGateway or use GetObjectBuilder()
		/// </summary>
		private static VRage.FastResourceLock lock_notMyUpdate = new VRage.FastResourceLock(); // static so work can be performed when any turret has main thread

		private enum State : byte { OFF, HAS_TARGET, NO_TARGET, NO_POSSIBLE, WAIT_DTAT }
		private State CurrentState = State.OFF;

		private bool defaultTargetingAcquiredTarget = false;

		public override void UpdateAfterSimulation()
		{
			if (!IsInitialized || myCubeBlock == null)
				return;

			if (needToRelease)
				lock_notMyUpdate.ReleaseExclusive();
			needToRelease = false;

			try
			{
				if (!myCubeBlock.IsWorking || myCubeBlock.Closed)
					return;

				if (updateCount >= 100) // every 100 updates
				{
					updateCount = 0;
					controlEnabled = myCubeBlock.DisplayNameText.Contains("[") && myCubeBlock.DisplayNameText.Contains("]");

					//myLogger.debugLog("enabled = " + enabled + ", idle = " + builder.EnableIdleRotation + ", shooting = " + builder.IsShooting, "UpdateAfterSimulation10()");

					//uint firstAmmoID = (myTurretBase as IMyInventoryOwner).GetInventory(0).GetItems()[0].ItemId;
					//float ammoSpeed = MyDefinitionManager.Static.GetAmmoDefinition(new MyDefinitionId(firstAmmoID)).DesiredSpeed;

					if (controlEnabled)
					{
						if (myAntenna == null || myAntenna.CubeBlock == null || !myAntenna.CubeBlock.canSendTo(myCubeBlock, true))
						{
							//myLogger.debugLog("searching for attached antenna", "UpdateAfterSimulation10()");

							myAntenna = null;
							foreach (Receiver antenna in RadioAntenna.registry)
								if (antenna.CubeBlock.canSendTo(myCubeBlock, true))
								{
									myLogger.debugLog("found attached antenna: " + antenna.CubeBlock.DisplayNameText, "UpdateAfterSimulation10()", Logger.severity.INFO);
									myAntenna = antenna;
									break;
								}
							if (myAntenna == null)
								foreach (Receiver antenna in LaserAntenna.registry)
									if (antenna.CubeBlock.canSendTo(myCubeBlock, true))
									{
										myLogger.debugLog("found attached antenna: " + antenna.CubeBlock.DisplayNameText, "UpdateAfterSimulation10()", Logger.severity.INFO);
										myAntenna = antenna;
										break;
									}
						}
						//if (myAntenna == null)
							//myLogger.debugLog("did not find attached antenna", "UpdateAfterSimulation10()");

						MyObjectBuilder_TurretBase builder = myCubeBlock.getSlimObjectBuilder() as MyObjectBuilder_TurretBase;
						targetMissiles = builder.TargetMissiles;
						targetMeteors = builder.TargetMeteors;
						targetCharacters = builder.TargetMoving;

						if (!possibleTargets())
						{
							setNoTarget();
							CurrentState = State.NO_POSSIBLE;
						}
					}
				}

				if (!controlEnabled)
				{
					if (CurrentState != State.OFF)
					{
						CurrentState = State.OFF;
						//myTurretBase.TrackTarget(null);
						myTurretBase.ResetTargetingToDefault();
					}
					return;
				}

				if (CurrentState == State.OFF)
					if (defaultTargetingAcquiredTarget)
						setNoTarget();
					else
						CurrentState = State.WAIT_DTAT;

				// Wait for default targeting to acquire a target. This is not an exhaustive test but it is the best we have.
				if (CurrentState == State.WAIT_DTAT)
				{
					if (updateCount % 10 == 0)
					{
						MyObjectBuilder_TurretBase builder = myCubeBlock.getSlimObjectBuilder() as MyObjectBuilder_TurretBase;
						if (builder.Target > 0)
						{
							defaultTargetingAcquiredTarget = true;
							setNoTarget();
						}
					}
					return;
				}

				//using (lock_newTarget.AcquireSharedUsing())
				//	if (newTarget != lastTarget)
				//		myTurretBase.TrackTarget(newTarget);

				// start thread
				if (!queued && CurrentState != State.NO_POSSIBLE && updateCount % 10 == 0)
				{
					queued = true;
					//MyAPIGateway.Parallel.Start(Update);
					//Update();
					TurretThread.EnqueueAction(Update);

					//if (newTarget != lastTarget)
					//	myTurretBase.TrackTarget(newTarget);
				}
			}
			catch (Exception e) { alwaysLog("Exception: " + e, "UpdateAfterSimulation10()", Logger.severity.ERROR); }
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

		private bool currentTargetIsMissile = false;

		private bool queued = false;

		//private IMyEntity newTarget;
		//private FastResourceLock lock_newTarget = new FastResourceLock();

		private void Update()
		{
			try
			{
				if (!controlEnabled)
					return;

				turretPosition = myCubeBlock.GetPosition();
				turretMissileBubble = new BoundingSphereD(turretPosition, 100);

				double distToMissile;
				if (currentTargetIsMissile && missileIsThreat(lastTarget, out distToMissile, true))
				{
					//myLogger.debugLog("no need to switch targets", "UpdateThread()");
					return; // no need to switch targets
				}

				//if (currentTargetIsMissile && !lastTarget.Closed)
				//	return;

				IMyEntity bestTarget;
				if (getValidTargets() && getBestTarget(out bestTarget))
				{
					if (CurrentState != State.HAS_TARGET || lastTarget != bestTarget)
					{
						CurrentState = State.HAS_TARGET;
						lastTarget = bestTarget;
						myLogger.debugLog("new target = " + bestTarget.getBestName(), "UpdateThread()", Logger.severity.DEBUG);
						//using (lock_newTarget.AcquireExclusiveUsing())
						//	newTarget = bestTarget;
						//myTurretBase.ResetTargetingToDefault();
						//myTurretBase.SetTarget(bestTarget);
						//myTurretBase.TrackTarget(bestTarget.GetPosition(), bestTarget.Physics.LinearVelocity);
						using (lock_notMyUpdate.AcquireSharedUsing())
							myTurretBase.TrackTarget(bestTarget);
					}
				}
				else // no target
					if (CurrentState == State.HAS_TARGET && !currentTargetIsMissile)
						setNoTarget();
			}
			catch (Exception e) { alwaysLog("Exception: " + e, "UpdateThread()", Logger.severity.ERROR); }
			finally { queued = false; }
		}

		private List<IMyEntity> validTarget_missile, validTarget_meteor, validTarget_character, validTarget_block;

		/// <summary>
		/// Fills validTarget_*
		/// </summary>
		private bool getValidTargets()
		{
			List<IMyEntity> entitiesInRange;
			using (lock_notMyUpdate.AcquireSharedUsing())
			{
				BoundingSphereD range = new BoundingSphereD(turretPosition, myTurretBase.Range + 200);
				entitiesInRange = MyAPIGateway.Entities.GetEntitiesInSphere(ref range);
			}

			validTarget_missile = new List<IMyEntity>();
			validTarget_meteor = new List<IMyEntity>();
			validTarget_character = new List<IMyEntity>();
			validTarget_block = new List<IMyEntity>();

			if (!possibleTargets())
			{
				myLogger.debugLog("no possible targets", "getValidTargets()", Logger.severity.WARNING);
				return false;
			}
			//if (EnemyNear)
			//	myLogger.debugLog("enemy near", "getValidTargets()");

			//myLogger.debugLog("initial entity count is " + entitiesInRange.Count, "getValidTargets()");

			foreach (IMyEntity entity in entitiesInRange)
			{
				if (entity is IMyCubeBlock)
				{
					if (enemyNear() && myCubeBlock.canConsiderHostile(entity as IMyCubeBlock))
						validTarget_block.Add(entity);
				}
				else if (entity is IMyCubeGrid) { } // do not shoot grids, it is not nice!
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
						if (matchingPlayer.Count != 1 || myCubeBlock.canConsiderHostile(matchingPlayer[0].PlayerID))
							validTarget_character.Add(entity);
					}
				}
				else if (enemyNear() && entity.ToString().StartsWith("MyMissile"))
					if (targetMissiles)
						validTarget_missile.Add(entity);
			}

			//myLogger.debugLog("target counts = " + validTarget_missile.Count + ", " + validTarget_meteor.Count + ", " + validTarget_character.Count + ", " + validTarget_block.Count, "getValidTargets()");
			return validTarget_missile.Count > 0 || validTarget_meteor.Count > 0 || validTarget_character.Count > 0 || validTarget_block.Count > 0;
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
			{
				//myLogger.debugLog("found a missile: " + bestTarget.getBestName(), "getBestTarget()");
				return true;
			}
			if (getClosest(validTarget_meteor, out bestTarget))
			{
				//myLogger.debugLog("found a meteor: " + bestTarget.getBestName(), "getBestTarget()");
				return true;
			}
			if (getClosest(validTarget_character, out bestTarget))
			{
				//myLogger.debugLog("found a character: " + bestTarget.getBestName(), "getBestTarget()");
				return true;
			}
			if (getBestBlock(out bestTarget))
			{
				//myLogger.debugLog("found a block: " + bestTarget.getBestName(), "getBestTarget()");
				return true;
			}
			//myLogger.debugLog("no target", "getBestTarget()");
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
				if (entity.Closed)
					continue;

				double distanceSquared;
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
		/// furthest hostile missile
		/// </summary>
		/// <param name="bestMissile"></param>
		/// <returns></returns>
		private bool getBestMissile(out IMyEntity bestMissile)
		{
			bestMissile = null;
			double furthest = 0;
			foreach (IMyEntity missile in validTarget_missile)
			{
				double toMissilePosition;
				if (missileIsThreat(missile, out toMissilePosition, false) && toMissilePosition > furthest)
				{
					furthest = toMissilePosition;
					bestMissile = missile;
				}
				//if (missileIsHostile(missile, out toMissilePosition, false))
				//{
				//	bestMissile = missile;
				//	return true;
				//}
			}
			return (bestMissile != null);
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

		/// <summary>
		/// approaching, going to intersect turretMissileBubble
		/// </summary>
		/// <returns></returns>
		private bool missileIsThreat(IMyEntity missile, out double toP0_lengthSquared, bool allowInside)
		{
			toP0_lengthSquared = -1;
			if (missile.Closed || missile.Physics == null)
				return false;

			Vector3D P0 = missile.GetPosition();
			Vector3D missileDirection = Vector3D.Normalize(missile.Physics.LinearVelocity);

			if (!allowInside)
			{
				toP0_lengthSquared = (P0 - turretPosition).LengthSquared();
				if (toP0_lengthSquared < turretMissileBubble.Radius * turretMissileBubble.Radius + 25) // missile is inside bubble
					return false;
			}

			// made redundant
			//double toP1_lengthSquared = (P0 + missileDirection - turretPosition).LengthSquared();
			//if (toP0_lengthSquared < toP1_lengthSquared) // moving away
			//	return false;

			RayD missileRay = new RayD(P0, missileDirection);
			//myLogger.debugLog("missileRay = " + missileRay, "missileIsHostile()");

			double? intersects = missileRay.Intersects(turretMissileBubble);
			if (intersects != null)
			{
				//myLogger.debugLog("got a missile intersection of " + intersects, "getBestMissile()");
				return true;
			}
			return false;
		}

		private void setNoTarget()
		{
			myLogger.debugLog("no target", "setNoTarget()");
			CurrentState = State.NO_TARGET;
			myTurretBase.TrackTarget(myTurretBase);
			myTurretBase.RequestEnable(false);
			myTurretBase.UpdateIsWorking();
			myTurretBase.RequestEnable(true);
			//myTurretBase.UpdateIsWorking();
		}

		private bool enemyNear()
		{ return myAntenna != null && myAntenna.EnemyNear; }

		private bool possibleTargets()
		{ return enemyNear() || targetMeteors || targetCharacters; }

		private Logger myLogger;
		protected override void alwaysLog(string toLog, string method = null, Logger.severity level = Logger.severity.DEBUG)
		{
			if (myLogger == null)
				myLogger = new Logger(null, "TurretBase");
			myLogger.log(toLog, method, level);
		}
	}
}
