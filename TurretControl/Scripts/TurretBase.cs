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

	/// <remarks>
	/// Turrets will be forced on initially, it is necissary for normal targeting to run briefly before we take control.
	/// </remarks>
	/// TODO:
	/// Use projectile speed to determine turretMissileBubble radius and to adjust for missile acceleration.
	/// Go through object builder to get ammo data?
	/// need to ignore target grid for thingiy
	public class TurretBase : UpdateEnforcer
	{
		private static bool MeteorsEnabled
		{ get { return MyAPIGateway.Session.EnvironmentHostility != MyEnvironmentHostilityEnum.SAFE; } }

		private IMyCubeBlock myCubeBlock;
		private IMyTerminalBlock myTerminal;
		private Ingame.IMyLargeTurretBase myTurretBase;

		/// <summary>
		/// limits to determine whether or not a turret can face a target
		/// </summary>
		private float minElevation, maxElevation, minAzimuth, maxAzimuth;

		protected override void DelayedInit()
		{
			if (!Settings.boolSettings[Settings.BoolSetName.bAllowTurretControl])
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
					myTurretBase.SetCustomName(myTurretBase.DisplayNameText + " " + Settings.stringSettings[Settings.StringSetName.sSmartTurretDefaultNPC]);
				else
					myTurretBase.SetCustomName(myTurretBase.DisplayNameText + " " + Settings.stringSettings[Settings.StringSetName.sSmartTurretDefaultPlayer]);
			}

			TurretBase_CustomNameChanged(null);
			myTerminal.CustomNameChanged += TurretBase_CustomNameChanged;
			myTerminal.OwnershipChanged += myTerminal_OwnershipChanged;

			// definition limits
			MyLargeTurretBaseDefinition definition = MyDefinitionManager.Static.GetCubeBlockDefinition(myCubeBlock.getSlimObjectBuilder()) as MyLargeTurretBaseDefinition;
			minElevation = (float)Math.Max(definition.MinElevationDegrees / 180 * Math.PI, -0.6); // -0.6 was determined empirically
			maxElevation = (float)Math.Min(definition.MaxElevationDegrees / 180 * Math.PI, Math.PI / 2);
			minAzimuth = (float)(definition.MinAzimuthDegrees / 180 * Math.PI);
			maxAzimuth = (float)(definition.MaxAzimuthDegrees / 180 * Math.PI);

			myLogger.debugLog("definition limits = " + definition.MinElevationDegrees + ", " + definition.MaxElevationDegrees + ", " + definition.MinAzimuthDegrees + ", " + definition.MaxAzimuthDegrees, "DelayedInit()");
			myLogger.debugLog("radian limits = " + minElevation + ", " + maxElevation + ", " + minAzimuth + ", " + maxAzimuth, "DelayedInit()");
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

		private string previousInstructions;

		private void TurretBase_CustomNameChanged(IMyTerminalBlock obj)
		{
			if (!controlEnabled)
				return;

			string instructions = myCubeBlock.getInstructions();
			if (instructions == previousInstructions)
				return;
			previousInstructions = instructions;

			//myLogger.debugLog("name changed to " + myCubeBlock.DisplayNameText + ", instructions = " + instructions, "TurretBase_CustomNameChanged()", Logger.severity.DEBUG);
			if (string.IsNullOrWhiteSpace(instructions))
			{
				requestedBlocks = null;
				CurrentState = State.OFF;
				return;
			}
			requestedBlocks = instructions.Split(',');
			myLogger.debugLog("requestedBlocks = " + instructions, "TurretBase_CustomNameChanged()");
			reset();
		}

		void myTerminal_OwnershipChanged(IMyTerminalBlock obj)
		{
			try
			{
				myLogger.debugLog("Ownership Changed", "myTerminal_OwnershipChanged()", Logger.severity.DEBUG);
				reset();
			}
			catch (Exception e) { myLogger.debugLog("Exception: " + e, "myTerminal_OwnershipChanged()", Logger.severity.ERROR); }
		}

		private bool controlEnabled = false;
		private int updateCount = 0;
		private bool targetMissiles, targetMeteors, targetCharacters, targetMoving, targetLargeGrids, targetSmallGrids, targetStations;
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

				if (updateCount % 100 == 0) // every 100 updates
				{
					if (updateCount == 1000)
					{
						myLogger.debugLog("update 1000", "UpdateAfterSimulation()");
						reset();
						return;
					}

					controlEnabled = myCubeBlock.DisplayNameText.Contains("[") && myCubeBlock.DisplayNameText.Contains("]");

					TurretBase_CustomNameChanged(null);

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
						//if (myAntenna == null)
						//myLogger.debugLog("did not find attached antenna", "UpdateAfterSimulation10()");
					}

					MyObjectBuilder_TurretBase builder = myCubeBlock.getSlimObjectBuilder() as MyObjectBuilder_TurretBase;
					targetMissiles = builder.TargetMissiles;
					targetMeteors = MeteorsEnabled && builder.TargetMeteors;
					targetCharacters = builder.TargetCharacters;
					targetMoving = builder.TargetMoving;
					targetLargeGrids = builder.TargetLargeGrids;
					targetSmallGrids = builder.TargetSmallGrids;
					targetStations = builder.TargetStations;

					if (possibleTargets())
					{
						if (CurrentState == State.NO_POSSIBLE)
						{
							myLogger.debugLog("now possible to target", "UpdateAfterSimulation()");
							CurrentState = State.OFF;
						}
					}
					else
						if (CurrentState != State.NO_POSSIBLE)
						{
							myLogger.debugLog("no longer possible to target", "UpdateAfterSimulation()");
							setNoTarget();
							CurrentState = State.NO_POSSIBLE;
						}
				}

				if (CurrentState == State.NO_POSSIBLE)
					return;

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

				// Wait for default targeting to acquire a target.
				if (CurrentState == State.WAIT_DTAT)
				{
					if (updateCount % 10 == 0)
					{
						turretPosition = myCubeBlock.GetPosition();
						MyObjectBuilder_TurretBase builder = myCubeBlock.getSlimObjectBuilder() as MyObjectBuilder_TurretBase;
						IMyEntity target;
						if (MyAPIGateway.Entities.TryGetEntityById(builder.Target, out target))
						{
							if ((target is IMyCubeBlock || target is IMyMeteor)
								&& canLase(target))
							{
								defaultTargetingAcquiredTarget = true;
								setNoTarget();
								myLogger.debugLog("default targeting acquired a target: " + target.getBestName(), "UpdateAfterSimulation()", Logger.severity.DEBUG);
							}
						}
					}
					return;
				}

				// start thread
				if (!queued && updateCount % 10 == 0)
				{
					queued = true;
					TurretThread.EnqueueAction(Update);
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

		private void Update()
		{
			try
			{
				if (!controlEnabled)
					return;

				turretPosition = myCubeBlock.GetPosition();
				//myLogger.debugLog("Turret Position: " + turretPosition, "Update()");
				turretMissileBubble = new BoundingSphereD(turretPosition, myTurretBase.Range / 10);

				double distToMissile;
				if (currentTargetIsMissile && canLase(lastTarget) && missileIsThreat(lastTarget, out distToMissile, false))
				{
					//myLogger.debugLog("no need to switch targets", "UpdateThread()");
					return; // no need to switch targets
				}

				IMyEntity bestTarget;
				if (getValidTargets() && getBestTarget(out bestTarget))
				{
					if (CurrentState != State.HAS_TARGET || lastTarget != bestTarget)
					{
						CurrentState = State.HAS_TARGET;
						lastTarget = bestTarget;
						myLogger.debugLog("new target = " + bestTarget.getBestName(), "UpdateThread()", Logger.severity.DEBUG);
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

		private List<IMyEntity> validTarget_missile, validTarget_meteor, validTarget_character, validTarget_block, validTarget_CubeGrid, ObstructingGrids;

		private static readonly MyObjectBuilderType Type_Decoy = new MyObjectBuilderType(typeof(MyObjectBuilder_Decoy));

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
			validTarget_CubeGrid = new List<IMyEntity>();
			ObstructingGrids = new List<IMyEntity>();

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
				IMyCubeBlock asBlock = entity as IMyCubeBlock;
				if (asBlock != null)
				{
					if (asBlock.IsWorking && enemyNear() && targetGridFlagSet(asBlock.CubeGrid) && myCubeBlock.canConsiderHostile(asBlock))
						validTarget_block.Add(entity);
					continue;
				}
				IMyCubeGrid asGrid = entity as IMyCubeGrid;
				if (asGrid != null)
				{
					if (myCubeBlock.canConsiderHostile(asGrid))
					{
						if (targetMoving && enemyNear() && targetGridFlagSet(asGrid))
							validTarget_CubeGrid.Add(entity);
					}
					else // not hostile
						ObstructingGrids.Add(entity);
					continue;
				}
				if (entity is IMyMeteor)
				{
					if (targetMeteors)
						validTarget_meteor.Add(entity);
					continue;
				}
				if (entity is IMyCharacter)
				{
					if (targetCharacters)
					{
						List<IMyPlayer> matchingPlayer = new List<IMyPlayer>();
						using (lock_notMyUpdate.AcquireSharedUsing())
							MyAPIGateway.Players.GetPlayers(matchingPlayer, player => { return player.DisplayName == entity.DisplayName; });
						if (matchingPlayer.Count != 1 || myCubeBlock.canConsiderHostile(matchingPlayer[0].PlayerID))
							validTarget_character.Add(entity);
					}
					continue;
				}
				if (entity is IMyFloatingObject || entity is IMyVoxelMap)
					continue;

				// entity could be missile
				if (enemyNear() && entity.ToString().StartsWith("MyMissile"))
					if (targetMissiles)
						validTarget_missile.Add(entity);
			}

			//myLogger.debugLog("target counts = " + validTarget_missile.Count + ", " + validTarget_meteor.Count + ", " + validTarget_character.Count + ", " + validTarget_block.Count, "getValidTargets()");
			return validTarget_missile.Count > 0 || validTarget_meteor.Count > 0 || validTarget_character.Count > 0 || validTarget_block.Count > 0 || validTarget_CubeGrid.Count > 0;
		}

		/// <summary>
		/// Responsible for prioritizing. Missile, Meteor, Character, Decoy, Other Blocks, Moving
		/// </summary>
		/// <param name="bestTarget"></param>
		/// <returns></returns>
		private bool getBestTarget(out IMyEntity bestTarget)
		{
			currentTargetIsMissile = getOneMissile(out bestTarget, validTarget_missile)
				|| getOneMissile(out bestTarget, validTarget_meteor);
			if (currentTargetIsMissile)
			{
				//myLogger.debugLog("found a missile: " + bestTarget.getBestName(), "getBestTarget()");
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
			if (getClosest(validTarget_CubeGrid, out bestTarget, true))
			{
				//myLogger.debugLog("closest approaching grid: " + bestTarget.getBestName(), "getBestTarget()");
				if (getClosestBlock(bestTarget as IMyCubeGrid, out bestTarget))
				{
					//myLogger.debugLog("closest block on grid: " + bestTarget.getBestName(), "getBestTarget()");
					return true;
				}
			}

			//myLogger.debugLog("no target", "getBestTarget()");
			return false;
		}

		private bool getClosest(List<IMyEntity> entities, out IMyEntity closest, bool threatTest = false)
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
					double temp;
					if (threatTest && !missileIsThreat(entity, out temp, false))
						continue;

					closestDistanceSquared = distanceSquared;
					closest = entity;
				}
			}

			return closest != null;
		}

		/// <summary>
		/// any threatening missile (does not have to be an actual missile)
		/// </summary>
		private bool getOneMissile(out IMyEntity bestMissile, List<IMyEntity> valid_missiles)
		{
			bestMissile = null;
			//double furthest = 0;
			foreach (IMyEntity missile in valid_missiles)
			{
				double toMissilePosition;
				if (canLase(missile) && missileIsThreat(missile, out toMissilePosition, false))// && toMissilePosition > furthest)
				{
					//furthest = toMissilePosition;
					bestMissile = missile;
					return true;
				}
			}
			//return (bestMissile != null);
			return false;
		}

		/// <summary>
		/// closest decoy, otherwise closest block of highest priority
		/// </summary>
		private bool getBestBlock(out IMyEntity bestMatch)
		{
			bestMatch = null;
			if (requestedBlocks == null || requestedBlocks.Length < 1)
				return false;

			double closestDistanceSquared = myTurretBase.Range * myTurretBase.Range;
			foreach (IMyCubeBlock block in validTarget_block)
			{
				if (!(block.BlockDefinition.TypeId == Type_Decoy))
					continue;

				double distanceSquared = (block.GetPosition() - turretPosition).LengthSquared();
				if (distanceSquared < closestDistanceSquared && canLase(block))
				{
					closestDistanceSquared = distanceSquared;
					bestMatch = block;
				}
			}

			if (bestMatch != null)
				return true;

			foreach (string requested in requestedBlocks)
			{
				foreach (IMyCubeBlock block in validTarget_block)
				{
					if (!block.DefinitionDisplayNameText.looseContains(requested))
						continue;

					double distanceSquared = (block.GetPosition() - turretPosition).LengthSquared();
					if (distanceSquared < closestDistanceSquared && canLase(block))
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
		/// Approaching, going to intersect turretMissileBubble.
		/// Meteors are also checked, they are after all, essentially missiles.
		/// </summary>
		/// <returns></returns>
		private bool missileIsThreat(IMyEntity missile, out double toP0_lengthSquared, bool allowInside)
		{
			toP0_lengthSquared = -1;
			if (missile.Closed || missile.Physics == null)
				return false;

			Vector3D P0 = missile.GetPosition();
			if (missile.Physics.LinearVelocity.LengthSquared() < 1)
				return false;
			Vector3D missileDirection = Vector3D.Normalize(missile.Physics.LinearVelocity);

			if (!allowInside)
			{
				toP0_lengthSquared = (P0 - turretPosition).LengthSquared();
				if (toP0_lengthSquared < turretMissileBubble.Radius * turretMissileBubble.Radius + 25) // missile is inside bubble
					return false;
			}

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

		private void reset()
		{
			myLogger.debugLog("reset", "reset()");
			myTurretBase.ResetTargetingToDefault();
			CurrentState = State.OFF;
			defaultTargetingAcquiredTarget = false;
		}

		private bool enemyNear()
		{
			//myLogger.debugLog("enemyNear: " + (myAntenna != null) + ", " + (myAntenna != null && myAntenna.EnemyNear), "enemyNear()");
			return myAntenna != null && myAntenna.EnemyNear;
		}

		private bool possibleTargets()
		{
			//myLogger.debugLog("possibleTargets: " + enemyNear() + ", " + targetMeteors + ", " + targetCharacters, "possibleTargets()");
			return enemyNear() || targetMeteors || targetCharacters;
		}

		/// <summary>
		/// Test required elev/azim against min/max
		/// Test line segment between turret and target for other entities
		/// </summary>
		private bool canLase(IMyEntity target)
		{
			Vector3D targetPos = target.GetPosition();

			// Test elev/azim
			Vector3 relativeToBlock = RelativeVector3F.createFromWorld(targetPos - turretPosition, myCubeBlock.CubeGrid).getBlock(myCubeBlock);
			float azimuth, elevation;
			Vector3.GetAzimuthAndElevation(Vector3.Normalize(relativeToBlock), out azimuth, out elevation);
			//myLogger.debugLog("for target = " + target.getBestName() + ", at " + target.GetPosition() + ", elevation = " + elevation + ", azimuth = " + azimuth, "canLase()");
			if (azimuth < minAzimuth || azimuth > maxAzimuth || elevation < minElevation || elevation > maxElevation)
				return false;

			// if we are waiting on default targeting, ObstructingGrids will not be up-to-date
			if (CurrentState == State.WAIT_DTAT)
			{
				// default targeting may acquire an unowned block on a friendly grid
				IMyCubeGrid targetGrid = target as IMyCubeGrid;
				if (targetGrid == null)
				{
					IMyCubeBlock targetAsBlock = target as IMyCubeBlock;
					if (targetAsBlock != null)
						targetGrid = targetAsBlock.CubeGrid;
				}


				List<IMyEntity> entitiesInRange;
				using (lock_notMyUpdate.AcquireSharedUsing())
				{
					BoundingSphereD range = new BoundingSphereD(turretPosition, myTurretBase.Range + 200);
					entitiesInRange = MyAPIGateway.Entities.GetEntitiesInSphere(ref range);
				}

				foreach (IMyEntity entity in entitiesInRange)
				{
					IMyCubeGrid asGrid = entity as IMyCubeGrid;
					//if (asGrid != null)
					//	myLogger.debugLog("checking entity: " + entity.getBestName(), "canLase()");
					if (asGrid != null)
					{
						// default targeting may acquire an unowned block on a friendly grid
						if (targetGrid != null && targetGrid == asGrid)
							continue;

						if (!myCubeBlock.canConsiderHostile(asGrid) && IsObstructing(asGrid, targetPos))
						{
							myLogger.debugLog("for target " + target.getBestName() + ", path from " + turretPosition + " to " + targetPos + ", obstructing entity = " + entity.getBestName(), "canLase()");
							return false;
						}
					}
				}
			}
			else 
			{
				foreach (IMyCubeGrid grid in ObstructingGrids)
				{
					//if (grid != null)
					//	myLogger.debugLog("checking entity: " + grid.getBestName(), "canLase()");
					if (IsObstructing(grid, targetPos))
					{
						myLogger.debugLog("for target " + target.getBestName() + ", path from " + turretPosition + " to " + targetPos + ", obstructing entity = " + grid.getBestName(), "canLase()");
						return false;
					}
				}
			}

			return true;
		}

		private bool IsObstructing(IMyCubeGrid grid, Vector3D target)
		{
			List<Vector3I> cells = new List<Vector3I>();
			grid.RayCastCells(turretPosition, target, cells, null, true);
			if (cells.Count == 0)
				return false;
			if (grid != myCubeBlock.CubeGrid)
				return true;

			//myLogger.debugLog("testing " + cells.Count + " of " + grid.getBestName() + " cells for obstruction", "IsObstructing()");
			foreach (Vector3I pos in cells)
			{
				//if (pos == myCubeBlock.Position)
				//{
				//	//myLogger.debugLog("my cell: " + pos + ", world:" + grid.GridIntegerToWorld(pos), "IsObstructing()");
				//	continue;
				//}

				IMySlimBlock block = grid.GetCubeBlock(pos);
				if (block != null)
				{
					if (block.FatBlock == null)
					{
						//myLogger.debugLog("obstructing slim pos = " + pos + ", world = " + grid.GridIntegerToWorld(pos), "IsObstructing()");
					}
					else
					{
						if (block.FatBlock == myCubeBlock)
							continue;
						//myLogger.debugLog("obstructing cube: " + block.FatBlock.DisplayNameText + ", pos = " + pos + ", world = " + grid.GridIntegerToWorld(pos), "IsObstructing()");
					}
					return true;
				}
				//myLogger.debugLog("empty cell: " + pos + ", world:" + grid.GridIntegerToWorld(pos), "IsObstructing()");
			}
			return false;
		}

		/// <summary>
		/// Test if the turret is set to target the type of grid
		/// </summary>
		private bool targetGridFlagSet(IMyCubeGrid grid)
		{
			switch (grid.GridSizeEnum)
			{
				case MyCubeSize.Small:
					return targetSmallGrids;
				case MyCubeSize.Large:
				default:
					if (grid.IsStatic)
						return targetStations;
					return targetLargeGrids;
			}
		}

		private bool getClosestBlock(IMyCubeGrid grid, out IMyEntity closestBlock)
		{
			List<IMySlimBlock> allBlocks = new List<IMySlimBlock>();
			using (lock_notMyUpdate.AcquireSharedUsing())
				grid.GetBlocks(allBlocks, slim => slim.FatBlock != null);

			double closestDistanceSquared = myTurretBase.Range * myTurretBase.Range;
			closestBlock = null;

			foreach (IMySlimBlock slim in allBlocks)
			{
				double distanceSquared = (slim.FatBlock.GetPosition() - turretPosition).LengthSquared();
				if (distanceSquared < closestDistanceSquared && canLase(slim.FatBlock))
				{
					closestDistanceSquared = distanceSquared;
					closestBlock = slim.FatBlock;
				}
			}

			return closestBlock != null;
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
