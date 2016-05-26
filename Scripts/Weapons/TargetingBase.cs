using System;
using System.Collections.Generic;
using System.Linq;
using Rynchodon.AntennaRelay;
using Sandbox.Common.ObjectBuilders;
using Sandbox.Game.Entities;
using Sandbox.ModAPI;
using VRage.Collections;
using VRage.Game;
using VRage.Game.Entity;
using VRage.Game.ModAPI;
using VRage.ModAPI;
using VRage.ObjectBuilders;
using VRageMath;

namespace Rynchodon.Weapons
{
	public abstract class TargetingBase
	{

		#region Static

		public const short zero = 0, one = 1, guidedValue = 100, negOne = -one, negGuidVal = -guidedValue;

		private static Dictionary<long, short> WeaponsTargetingProjectile = new Dictionary<long, short>();
		//private static Logger s_logger = new Logger("TargetingBase");

		static TargetingBase()
		{
			MyAPIGateway.Entities.OnCloseAll += Entities_OnCloseAll;
		}

		private static void Entities_OnCloseAll()
		{
			MyAPIGateway.Entities.OnCloseAll -= Entities_OnCloseAll;
			WeaponsTargetingProjectile = null;
			//s_logger = null;
		}

		public static short GetWeaponsTargetingProjectile(IMyEntity entity)
		{
			short result;
			if (!WeaponsTargetingProjectile.TryGetValue(entity.EntityId, out result))
				result = 0;
			return result;
		}

		private static void AddWeaponsTargetingProjectile(IMyEntity entity, short number)
		{
			//s_logger.debugLog("entity: " + entity.getBestName() + ", number: " + number, "AddWeaponsTargetingProjectile()");

			short count;
			if (WeaponsTargetingProjectile.TryGetValue(entity.EntityId, out count))
			{
				//s_logger.debugLog("count: " + count, "AddWeaponsTargetingProjectile()");
				count += number;
				if (count == zero)
				{
					WeaponsTargetingProjectile.Remove(entity.EntityId);
					return;
				}
				//s_logger.debugLog(count < zero, "count is negative: " + count + ", for " + entity.getBestName(), "AddWeaponsTargetingProjectile()", Logger.severity.FATAL);
			}
			else
				count = number;

			//s_logger.debugLog(count > short.MaxValue / 2, "many weapons targeting: " + entity.getBestName() + ", count: " + count, "AddWeaponsTargetingProjectile()", Logger.severity.WARNING);
			WeaponsTargetingProjectile[entity.EntityId] = count;
		}

		#endregion Static

		private readonly Logger myLogger;
		public readonly IMyEntity MyEntity;
		/// <summary>Either the weapon block that is targeting or the block that created the weapon.</summary>
		public readonly IMyCubeBlock CubeBlock;
		public readonly IMyFunctionalBlock FuncBlock;

		/// <summary>Iff true, the projectile will attempt to chase-down a target.</summary>
		protected bool TryHard = false;
		protected bool SEAD = false;
		private ulong m_nextLastSeenSearch;
		private Target value_CurrentTarget;
		/// <summary>The target that is being processed.</summary>
		protected Target myTarget;

		protected List<IMyEntity> PotentialObstruction = new List<IMyEntity>();
		/// <summary>Targets that cannot be hit.</summary>
		private readonly MyUniqueList<IMyEntity> Blacklist = new MyUniqueList<IMyEntity>();
		private readonly Dictionary<TargetType, List<IMyEntity>> Available_Targets = new Dictionary<TargetType, List<IMyEntity>>();
		private List<MyEntity> nearbyEntities = new List<MyEntity>();

		/// <summary>Accumulation of custom terminal, vanilla terminal, and text commands.</summary>
		public TargetingOptions Options { get; protected set; }

		/// <summary>Custom terminal options for ARMS, does not include vanilla controls</summary>
		public TargetingOptions TerminalOptions { get; protected set; }

		/// <summary>The target that has been chosen.</summary>
		public Target CurrentTarget
		{
			get { return value_CurrentTarget; }
			set
			{
				if (value_CurrentTarget != null && value != null && value_CurrentTarget.Entity == value.Entity)
				{
					value_CurrentTarget = value;
					return;
				}

				if (value_CurrentTarget != null && (value_CurrentTarget.TType & TargetType.Projectile) != 0)
					AddWeaponsTargetingProjectile(value_CurrentTarget.Entity, this is Guided.GuidedMissile ? negGuidVal : negOne);
				if (value != null && (value.TType & TargetType.Projectile) != 0)
					AddWeaponsTargetingProjectile(value.Entity, this is Guided.GuidedMissile ? guidedValue : one);

				value_CurrentTarget = value;
			}
		}

		public TargetingBase(IMyEntity entity, IMyCubeBlock controllingBlock)
		{
			if (entity == null)
				throw new ArgumentNullException("entity");
			if (controllingBlock == null)
				throw new ArgumentNullException("controllingBlock");

			myLogger = new Logger("TargetingBase", entity);
			MyEntity = entity;
			CubeBlock = controllingBlock;
			FuncBlock = controllingBlock as IMyFunctionalBlock;

			myTarget = NoTarget.Instance;
			CurrentTarget = myTarget;
			TerminalOptions = new TargetingOptions();
			Options = new TargetingOptions();
			entity.OnClose += obj => {
				if (WeaponsTargetingProjectile != null)
					CurrentTarget = null;
			};

			//myLogger.debugLog("entity: " + MyEntity.getBestName() + ", block: " + CubeBlock.getBestName(), "TargetingBase()");
		}

		public TargetingBase(IMyCubeBlock block)
			: this(block, block)
		{
			myLogger = new Logger("TargetingBase", block);
		}

		private bool PhysicalProblem(Vector3D targetPos, IMyEntity target)
		{
			return !CanRotateTo(targetPos) || Obstructed(targetPos, target);
		}

		/// <summary>
		/// Used to apply restrictions on rotation, such as min/max elevation/azimuth.
		/// </summary>
		/// <param name="targetPoint">The point of the target.</param>
		/// <returns>true if the rotation is allowed</returns>
		/// <remarks>Invoked on targeting thread.</remarks>
		protected abstract bool CanRotateTo(Vector3D targetPos);

		protected abstract bool Obstructed(Vector3D targetPos, IMyEntity target);

		/// <summary>
		/// Determines the speed of the projectile.
		/// </summary>
		protected abstract float ProjectileSpeed(ref Vector3D targetPos);

		/// <summary>
		/// If the projectile has not been fired, aproximately where it will be created.
		/// Otherwise, the location of the projectile.
		/// </summary>
		/// <remarks>
		/// Returns MyEntity.GetPosition(), turrets may benefit from using barrel position.
		/// </remarks>
		protected virtual Vector3D ProjectilePosition()
		{
			return MyEntity.GetPosition();
		}

		/// <summary>
		/// Clears the set of entities that cannot be targeted.
		/// </summary>
		protected void ClearBlacklist()
		{
			Blacklist.Clear();
		}

		protected void BlacklistTarget()
		{
			//myLogger.debugLog("Blacklisting " + myTarget.Entity, "BlacklistTarget()");
			Blacklist.Add(myTarget.Entity);
			myTarget = NoTarget.Instance;
			CurrentTarget = myTarget;
		}

		#region Targeting

		/// <summary>
		/// Finds a target
		/// </summary>
		protected void UpdateTarget()
		{
			if (Options.TargetingRange < 1f)
			{
				myLogger.debugLog("Not targeting, zero range");
				return;
			}

			if ((myTarget.TType & TargetType.Projectile) != 0 && !myTarget.Entity.Closed)
				if (TryHard || ProjectileIsThreat(myTarget.Entity, myTarget.TType))
				{
					//myLogger.debugLog("Keeping Target = " + myTarget.Entity.getBestName(), "UpdateTarget()");
					return;
				}

			myTarget = NoTarget.Instance;

			CollectTargets();
			PickATarget();
			//if (myTarget.Entity != null && CurrentTarget != myTarget)
			//	myLogger.debugLog("New target: " + myTarget.Entity.getBestName());
			//else if (CurrentTarget != null && CurrentTarget.Entity != null)
			//	myLogger.debugLog("Lost target: " + CurrentTarget.Entity.getBestName());
			CurrentTarget = myTarget;
		}

		/// <summary>
		/// Targets a LastSeen chosen from the given storage, will overrride current target.
		/// </summary>
		/// <param name="storage">NetworkStorage to get LastSeen from.</param>
		protected void GetLastSeenTarget(NetworkStorage storage, double range)
		{
			if (Globals.UpdateCount < m_nextLastSeenSearch)
				return;
			m_nextLastSeenSearch = Globals.UpdateCount + 100ul;

			if (storage == null)
			{
				//myLogger.debugLog("no storage", "GetLastSeenTarget()", Logger.severity.INFO);
				return;
			}

			if (storage.LastSeenCount == 0)
			{
				//myLogger.debugLog("no last seen in storage", "GetLastSeenTarget()", Logger.severity.DEBUG);
				return;
			}

			LastSeen processing;
			IMyCubeBlock targetBlock;

			if (CurrentTarget.Entity != null && storage.TryGetLastSeen(CurrentTarget.Entity.EntityId, out processing) && processing.isRecent())
			{
				LastSeenTarget lst = myTarget as LastSeenTarget;
				if (lst != null && lst.Block != null && !lst.Block.Closed)
				{
					lst.Update(processing);
					CurrentTarget = myTarget;
					return;
				}

				if (ChooseBlock(processing, out targetBlock))
				{
					myTarget = new LastSeenTarget(processing, targetBlock);
					CurrentTarget = myTarget;
					return;
				}
			}

			if (Options.TargetEntityId.HasValue)
			{
				if (storage.TryGetLastSeen(Options.TargetEntityId.Value, out processing))
				{
					ChooseBlock(processing, out targetBlock);
					myTarget = new LastSeenTarget(processing, targetBlock);
					CurrentTarget = myTarget;
				}
				//else
				//	myLogger.debugLog("failed to get last seen from entity id", "GetLastSeenTarget()");
				return;
			}

			processing = null;
			targetBlock = null;

			if (SEAD)
			{
				float highestPowerLevel = 0f;

				storage.ForEachLastSeen((LastSeen seen) => {
					if (seen.isRecent() && CubeBlock.canConsiderHostile(seen.Entity) && Options.CanTargetType(seen.Entity))
					{
						IMyCubeBlock block;
						float powerLevel;
						if (RadarEquipment.GetRadarEquipment(seen, out block, out powerLevel) && powerLevel > highestPowerLevel)
						{
							highestPowerLevel = powerLevel;
							processing = seen;
							targetBlock = block;
						}
					}
				});
			}
			else
			{
				// choose closest grid
				Vector3D myPos = ProjectilePosition();
				double closestDist = range * range;

				storage.ForEachLastSeen(seen => {
					if (seen.isRecent() && CubeBlock.canConsiderHostile(seen.Entity) && Options.CanTargetType(seen.Entity))
					{
						IMyCubeBlock block;
						if (!ChooseBlock(seen, out block))
							return;

						// always prefer a grid with a block
						if (targetBlock != null && block == null)
							return;

						double dist = Vector3D.DistanceSquared(myPos, seen.LastKnownPosition);
						if (dist < closestDist)
						{
							closestDist = dist;
							processing = seen;
							targetBlock = block;
						}
					}
				});
			}

			if (processing == null)
			{
				//myLogger.debugLog("failed to get a target from last seen", "GetLastSeenTarget()");
				myTarget = NoTarget.Instance;
				CurrentTarget = myTarget;
			}
			else
			{
				myTarget = new LastSeenTarget(processing, targetBlock);
				CurrentTarget = myTarget;
			}
		}

		/// <summary>
		/// Attempts to choose a targetable block from a LastSeen.
		/// </summary>
		/// <returns>True iff the LastSeen can be targeted, either because it has no blocks or because a block was found.</returns>
		private bool ChooseBlock(LastSeen seen, out IMyCubeBlock block)
		{
			block = null;

			if (SEAD)
			{
				float powerLevel;
				return RadarEquipment.GetRadarEquipment(seen, out block, out powerLevel);
			}

			if (seen.Info == null || !seen.Info.IsRecent())
				return true;

			IMyCubeGrid grid = seen.Entity as IMyCubeGrid;
			if (grid == null)
				return true;

			if (Options.blocksToTarget.IsNullOrEmpty() && (Options.CanTarget & TargetType.Destroy) == 0)
				return true;

			double distValue;
			if (!GetTargetBlock(grid, Options.CanTarget, out block, out distValue, false))
				return false;
			return true;
		}

		/// <summary>
		/// Fills Available_Targets and PotentialObstruction
		/// </summary>
		private void CollectTargets()
		{
			myLogger.debugLog("entered");
			Available_Targets.Clear();
			PotentialObstruction.Clear();
			nearbyEntities.Clear();

			BoundingSphereD nearbySphere = new BoundingSphereD(ProjectilePosition(), Options.TargetingRange);
			MyGamePruningStructure.GetAllTopMostEntitiesInSphere(ref nearbySphere, nearbyEntities);

			foreach (IMyEntity entity in nearbyEntities)
			{
				myLogger.debugLog("entity: " + entity.getBestName());

				if (Options.TargetEntityId.HasValue && entity.EntityId != Options.TargetEntityId.Value)
					continue;

				if (Blacklist.Contains(entity))
					continue;

				if (entity is IMyFloatingObject)
				{
					if (entity.Physics == null || entity.Physics.Mass > 100)
					{
						AddTarget(TargetType.Moving, entity);
						continue;
					}
				}

				if (entity is IMyMeteor)
				{
					AddTarget(TargetType.Meteor, entity);
					continue;
				}

				IMyCharacter asChar = entity as IMyCharacter;
				if (asChar != null)
				{
					if (string.IsNullOrEmpty(entity.DisplayName))
					{
						myLogger.debugLog("Cannot target creatures, cannot get identity");
						continue;
					}

					IMyIdentity asIdentity = asChar.GetIdentity_Safe();
					if (asIdentity != null)
					{
						if (asIdentity.IsDead)
						{
							myLogger.debugLog("(s)he's dead, jim: " + entity.getBestName());
							continue;
						}
					}
					//else
					//	myLogger.debugLog("Found a robot! : " + asChar + " . " + entity.getBestName(), "CollectTargets()");

					if (asIdentity == null || CubeBlock.canConsiderHostile(asIdentity.PlayerId))
						AddTarget(TargetType.Character, entity);
					else
						PotentialObstruction.Add(entity);
					continue;
				}

				IMyCubeGrid asGrid = entity as IMyCubeGrid;
				if (asGrid != null)
				{
					if (!asGrid.Save)
						continue;

					if (CubeBlock.canConsiderHostile(asGrid))
					{
						AddTarget(TargetType.Moving, entity);
						AddTarget(TargetType.Destroy, entity);
						if (asGrid.IsStatic)
							AddTarget(TargetType.Station, entity);
						else if (asGrid.GridSizeEnum == MyCubeSize.Large)
							AddTarget(TargetType.LargeGrid, entity);
						else
							AddTarget(TargetType.SmallGrid, entity);

						if (Options.FlagSet(TargetingFlags.Preserve))
							PotentialObstruction.Add(entity);
					}
					else
						PotentialObstruction.Add(entity);
					continue;
				}

				if (entity.ToString().StartsWith("MyMissile"))
				{
					AddTarget(TargetType.Missile, entity);
					continue;
				}
			}
		}

		/// <summary>
		/// Adds a target to Available_Targets
		/// </summary>
		private void AddTarget(TargetType tType, IMyEntity target)
		{
			if (!Options.CanTargetType(tType))
				return;

			//myLogger.debugLog(target.ToString().StartsWith("MyMissile"), "missile: " + target.getBestName() + ", type = " + tType + ", allowed targets = " + Options.CanTarget, "AddTarget()");

			List<IMyEntity> list;
			if (!Available_Targets.TryGetValue(tType, out list))
			{
				list = new List<IMyEntity>();
				Available_Targets.Add(tType, list);
			}
			list.Add(target);
		}

		/// <summary>
		/// <para>Choose a target from Available_Targets.</para>
		/// </summary>
		private void PickATarget()
		{
			if (PickAProjectile(TargetType.Missile) || PickAProjectile(TargetType.Meteor) || PickAProjectile(TargetType.Moving))
				return;

			double closerThan = double.MaxValue;
			if (SetClosest(TargetType.Character, ref closerThan))
				return;

			// do not short for grid test
			SetClosest(TargetType.LargeGrid, ref closerThan);
			SetClosest(TargetType.SmallGrid, ref closerThan);
			SetClosest(TargetType.Station, ref closerThan);

			// if weapon does not have a target yet, check for destroy
			if (myTarget.TType == TargetType.None)
				SetClosest(TargetType.Destroy, ref closerThan);
		}

		/// <summary>
		/// Get the closest target of the specified type from Available_Targets[tType].
		/// </summary>
		private bool SetClosest(TargetType tType, ref double closerThan)
		{
			List<IMyEntity> targetsOfType;
			if (!Available_Targets.TryGetValue(tType, out targetsOfType))
				return false;

			myLogger.debugLog("getting closest " + tType + ", from list of " + targetsOfType.Count);

			IMyEntity closest = null;

			Vector3D weaponPosition = ProjectilePosition();

			foreach (IMyEntity entity in targetsOfType)
			{
				if (entity.Closed)
					continue;

				IMyEntity target;
				Vector3D targetPosition;
				double distanceValue;

				// get block from grid before obstruction test
				IMyCubeGrid asGrid = entity as IMyCubeGrid;
				if (asGrid != null)
				{
					IMyCubeBlock targetBlock;
					if (GetTargetBlock(asGrid, tType, out targetBlock, out distanceValue))
						target = targetBlock;
					else
						continue;
					targetPosition = target.GetPosition();
				}
				else
				{
					target = entity;
					targetPosition = target.GetPosition();

					distanceValue = Vector3D.DistanceSquared(targetPosition, weaponPosition);
					if (distanceValue > Options.TargetingRangeSquared)
					{
						//myLogger.debugLog("for type: " + tType + ", too far to target: " + target.getBestName(), "SetClosest()");
						continue;
					}

					if (PhysicalProblem(targetPosition, target))
					{
						//myLogger.debugLog("can't target: " + target.getBestName(), "SetClosest()");
						Blacklist.Add(target);
						continue;
					}
				}

				if (distanceValue < closerThan)
				{
					closest = target;
					closerThan = distanceValue;
				}
			}

			if (closest != null)
			{
				myTarget = new TurretTarget(closest, tType);
				return true;
			}
			return false;
		}

		/// <remarks>
		/// <para>Targeting non-terminal blocks would cause confusion.</para>
		/// <para>Tiny blocks, such as sensors, shall be skipped.</para>
		/// <para>Open doors shall not be targeted.</para>
		/// </remarks>
		private bool TargetableBlock(IMyCubeBlock block, bool Disable)
		{
			if (!(block is IMyTerminalBlock))
				return false;

			if (block.Mass < 100)
				return false;

			IMyDoor asDoor = block as IMyDoor;
			if (asDoor != null && asDoor.OpenRatio > 0.01)
				return false;

			if (Disable && !block.IsWorking)
				if (!block.IsFunctional || !Options.FlagSet(TargetingFlags.Functional))
					return false;

			if (Blacklist.Contains(block))
				return false;

			if (!CanRotateTo(block.GetPosition()))
				return false;

			return true;
		}

		/// <summary>
		/// Gets the best block to target from a grid.
		/// </summary>
		/// <param name="grid">The grid to search</param>
		/// <param name="tType">Checked for destroy</param>
		/// <param name="target">The best block fromt the grid</param>
		/// <param name="distanceValue">The value assigned based on distance and position in blocksToTarget.</param>
		/// <remarks>
		/// <para>Decoy blocks will be given a distanceValue of the distance squared to weapon.</para>
		/// <para>Blocks from blocksToTarget will be given a distanceValue of the distance squared * (index + 1)^3.</para>
		/// <para>Other blocks will be given a distanceValue of the distance squared * (1e12).</para>
		/// </remarks>
		public bool GetTargetBlock(IMyCubeGrid grid, TargetType tType, out IMyCubeBlock target, out double distanceValue, bool doRangeTest = true)
		{
			//myLogger.debugLog("getting block from " + grid.DisplayName + ", target type = " + tType, "GetTargetBlock()");

			Vector3D myPosition = ProjectilePosition();
			CubeGridCache cache = CubeGridCache.GetFor(grid);

			target = null;
			distanceValue = double.MaxValue;

			if (cache.TerminalBlocks == 0)
			{
				//myLogger.debugLog("no terminal blocks on grid: " + grid.DisplayName, "GetTargetBlock()");
				return false;
			}

			// get decoy block
			{
				var decoyBlockList = cache.GetBlocksOfType(typeof(MyObjectBuilder_Decoy));
				if (decoyBlockList != null)
					foreach (IMyCubeBlock block in decoyBlockList)
					{
						if (!TargetableBlock(block, true))
							continue;

						double distanceSq = Vector3D.DistanceSquared(myPosition, block.GetPosition());
						if (doRangeTest && distanceSq > Options.TargetingRangeSquared)
							continue;

						if (distanceSq < distanceValue && CubeBlock.canConsiderHostile(block))
						{
							target = block;
							distanceValue = distanceSq;
						}
					}
				if (target != null)
				{
					//myLogger.debugLog("for type = " + tType + " and grid = " + grid.DisplayName + ", found a decoy block: " + target.DisplayNameText + ", distanceValue: " + distanceValue, "GetTargetBlock()");
					return true;
				}
			}

			// get block from blocksToTarget
			if (!Options.blocksToTarget.IsNullOrEmpty())
			{
				int index = 0;
				IMyCubeBlock in_target = target;
				double in_distValue = distanceValue;

				Options.listOfBlocks.ForEach(cache, ref index, block => {
					if (!TargetableBlock(block, true))
						return;

					double distSq = Vector3D.DistanceSquared(myPosition, block.GetPosition());
					if (doRangeTest && distSq > Options.TargetingRangeSquared)
						return;

					int multiplier = index + 1;
					distSq *= multiplier * multiplier * multiplier;

					if (distSq < in_distValue && CubeBlock.canConsiderHostile(block))
					{
						in_target = block;
						in_distValue = distSq;
					}
				});

				target = in_target;
				distanceValue = in_distValue;

				if (target != null) // found a block from blocksToTarget
				{
					//myLogger.debugLog("for type = " + tType + " and grid = " + grid.DisplayName + ", target = " + target.DisplayNameText +
					//	", distance = " + Vector3D.Distance(myPosition, target.GetPosition()) + ", distanceValue = " + distanceValue);
					return true;
				}
			}

			// get any IMyTerminalBlock
			bool destroy = (tType & TargetType.Moving) != 0 || (tType & TargetType.Destroy) != 0;
			if (destroy || Options.blocksToTarget.IsNullOrEmpty())
			{
				List<IMySlimBlock> allSlims = new List<IMySlimBlock>();
				grid.GetBlocks_Safe(allSlims, (slim) => slim.FatBlock is IMyTerminalBlock);

				double closest = double.MaxValue;

				foreach (IMySlimBlock slim in allSlims)
					if (TargetableBlock(slim.FatBlock, !destroy))
					{
						double distanceSq = Vector3D.DistanceSquared(myPosition, slim.FatBlock.GetPosition());
						if (doRangeTest && distanceSq > Options.TargetingRangeSquared)
							continue;
						distanceSq *= 1e12;

						if (distanceSq < closest && CubeBlock.canConsiderHostile(slim.FatBlock))
						{
							target = slim.FatBlock;
							distanceValue = distanceSq;
						}
					}

				if (target != null)
				{
					//myLogger.debugLog("for type = " + tType + " and grid = " + grid.DisplayName + ", found a block: " + target.DisplayNameText + ", distanceValue = " + distanceValue);
					return true;
				}
			}

			return false;
		}

		/// <summary>
		/// Get any projectile which is a threat from Available_Targets[tType].
		/// </summary>
		private bool PickAProjectile(TargetType tType)
		{
			List<IMyEntity> targetsOfType;
			if (Available_Targets.TryGetValue(tType, out targetsOfType))
			{
				var sorted = targetsOfType.OrderBy(entity => GetWeaponsTargetingProjectile(entity));

				foreach (IMyEntity entity in sorted)
				{
					if (entity.Closed)
						continue;

					if (tType == TargetType.Missile)
					{
						long owner;
						if (Guided.GuidedMissile.TryGetOwnerId(entity.EntityId, out owner) && !CubeBlock.canConsiderHostile(owner))
						{
							//myLogger.debugLog("owner is not hostile", "PickAProjectile()");
							continue;
						}
					}

					// meteors and missiles are dangerous even if they are slow
					if (!(entity is IMyMeteor || entity.GetLinearVelocity().LengthSquared() > 100 || entity.ToString().StartsWith("MyMissile")))
						continue;

					IMyEntity projectile = entity;

					IMyCubeGrid asGrid = projectile as IMyCubeGrid;
					if (asGrid != null)
					{
						IMyCubeBlock targetBlock;
						double distanceValue;
						if (GetTargetBlock(asGrid, tType, out targetBlock, out distanceValue))
							projectile = targetBlock;
						else
						{
							//myLogger.debugLog("failed to get a block from: " + asGrid.DisplayName, "PickAProjectile()");
							continue;
						}
					}

					if (ProjectileIsThreat(projectile, tType))
					{
						if (!PhysicalProblem(projectile.GetPosition(), projectile))
						{
							//myLogger.debugLog("Is a threat: " + projectile.getBestName() + ", weapons targeting: " + GetWeaponsTargetingProjectile(projectile), "PickAProjectile()");
							myTarget = new TurretTarget(projectile, tType);
							return true;
						}
						//else
						//	myLogger.debugLog("Physical problem: " + projectile.getBestName(), "PickAProjectile()");
					}
					//else
					//	myLogger.debugLog("Not a threat: " + projectile.getBestName(), "PickAProjectile()");
				}
			}

			return false;
		}

		private bool ProjectileIsThreat(IMyEntity projectile, TargetType tType)
		{
			if (projectile.Closed)
				return false;

			Vector3D projectilePosition = projectile.GetPosition();
			BoundingSphereD ignoreArea = new BoundingSphereD(ProjectilePosition(), Options.TargetingRange / 10f);
			if (ignoreArea.Contains(projectilePosition) == ContainmentType.Contains)
				return false;

			Vector3D weaponPosition = ProjectilePosition();
			Vector3D nextPosition = projectilePosition + projectile.GetLinearVelocity() / 60f;
			if (Vector3D.DistanceSquared(weaponPosition, nextPosition) < Vector3D.DistanceSquared(weaponPosition, projectilePosition))
			{
				//myLogger.debugLog("projectile: " + projectile.getBestName() + ", is moving towards weapon. D0 = " + Vector3D.DistanceSquared(weaponPosition, nextPosition) + ", D1 = " + Vector3D.DistanceSquared(weaponPosition, projectilePosition), "ProjectileIsThreat()");
				return true;
			}
			else
			{
				//myLogger.debugLog("projectile: " + projectile.getBestName() + ", is moving away from weapon. D0 = " + Vector3D.DistanceSquared(weaponPosition, nextPosition) + ", D1 = " + Vector3D.DistanceSquared(weaponPosition, projectilePosition), "ProjectileIsThreat()");
				return false;
			}
		}

		#endregion

		#region Target Interception

		/// <summary>
		/// Calculates FiringDirection and InterceptionPoint
		/// </summary>
		protected void SetFiringDirection()
		{
			if (myTarget.Entity == null || myTarget.Entity.MarkedForClose || MyEntity.MarkedForClose)
				return;

			FindInterceptVector();
			if (myTarget.Entity != null)
			{
				if (PhysicalProblem(myTarget.ContactPoint.Value, myTarget.Entity))
				{
					//myLogger.debugLog("Shot path is obstructed, blacklisting " + myTarget.Entity.getBestName(), "SetFiringDirection()");
					BlacklistTarget();
					return;
				}
				//myLogger.debugLog("got an intercept vector: " + myTarget.FiringDirection + ", ContactPoint: " + myTarget.ContactPoint + ", target position: " + TargetPosition + ", by entity: " + myTarget.Entity.GetCentre(), "SetFiringDirection()");
			}
			CurrentTarget = myTarget;
		}

		/// <remarks>From http://danikgames.com/blog/moving-target-intercept-in-3d/</remarks>
		private void FindInterceptVector()
		{
			Vector3D shotOrigin = ProjectilePosition();
			Vector3 shooterVel = MyEntity.GetLinearVelocity();
			Vector3D targetOrigin = myTarget.GetPosition();

			Vector3 targetVel = myTarget.GetLinearVelocity();
			Vector3 relativeVel = (targetVel - shooterVel);
			targetOrigin += relativeVel * Globals.UpdateDuration;
			float shotSpeed = ProjectileSpeed(ref targetOrigin);

			Vector3 displacementToTarget = targetOrigin - shotOrigin;
			float distanceToTarget = displacementToTarget.Length();
			Vector3 directionToTarget = displacementToTarget / distanceToTarget;

			// Decompose the target's velocity into the part parallel to the
			// direction to the cannon and the part tangential to it.
			// The part towards the cannon is found by projecting the target's
			// velocity on directionToTarget using a dot product.
			float targetSpeedOrth = Vector3.Dot(relativeVel, directionToTarget);
			Vector3 relativeVelOrth = targetSpeedOrth * directionToTarget;

			// The tangential part is then found by subtracting the
			// result from the target velocity.
			Vector3 relativeVelTang = relativeVel - relativeVelOrth;

			// The tangential component of the velocities should be the same
			// (or there is no chance to hit)
			// THIS IS THE MAIN INSIGHT!
			Vector3 shotVelTang = relativeVelTang;

			if (TryHard)
				shotVelTang *= 3f;

			// Now all we have to find is the orthogonal velocity of the shot

			float shotVelSpeedSquared = shotVelTang.LengthSquared();
			if (shotVelSpeedSquared > shotSpeed * shotSpeed)
			{
				// Shot is too slow to intercept target.
				if (TryHard)
				{
					//myLogger.debugLog("shot too slow, trying anyway", "FindInterceptVector()");
					// direction is a trade-off between facing the target and fighting tangential velocity
					Vector3 direction = directionToTarget + displacementToTarget * 0.01f + shotVelTang;
					direction.Normalize();
					myTarget.FiringDirection = direction;
					myTarget.ContactPoint = shotOrigin + direction * distanceToTarget;
					return;
				}
				//myLogger.debugLog("shot too slow, blacklisting", "FindInterceptVector()");
				BlacklistTarget();
				return;
			}
			else
			{
				// We know the shot speed, and the tangential velocity.
				// Using pythagoras we can find the orthogonal velocity.
				float shotSpeedOrth = (float)Math.Sqrt(shotSpeed * shotSpeed - shotVelSpeedSquared);
				Vector3 shotVelOrth = directionToTarget * shotSpeedOrth;

				// Finally, add the tangential and orthogonal velocities.
				Vector3 firingDirection = Vector3.Normalize(shotVelOrth + shotVelTang);

				// Find the time of collision (distance / relative velocity)
				float timeToCollision = distanceToTarget / (shotSpeedOrth - targetSpeedOrth);

				// Calculate where the shot will be at the time of collision
				Vector3 shotVel = shotVelOrth + shotVelTang;
				Vector3 contactPoint = shotOrigin + (shotVel + shooterVel) * timeToCollision;

				myTarget.FiringDirection = firingDirection;
				myTarget.ContactPoint = contactPoint;
			}
		}

		/// <remarks>From http://danikgames.com/blog/moving-target-intercept-in-3d/</remarks>
		public static bool FindInterceptVector(Vector3D shotOrigin, Vector3 shooterVel, Vector3D targetOrigin, Vector3 targetVel, float shotSpeed, bool tryHard, out Vector3 firingDirection, out Vector3D contactPoint)
		{
			Vector3 relativeVel = (targetVel - shooterVel);
			targetOrigin += relativeVel * Globals.UpdateDuration;

			Vector3 displacementToTarget = targetOrigin - shotOrigin;
			float distanceToTarget = displacementToTarget.Length();
			Vector3 directionToTarget = displacementToTarget / distanceToTarget;

			// Decompose the target's velocity into the part parallel to the
			// direction to the cannon and the part tangential to it.
			// The part towards the cannon is found by projecting the target's
			// velocity on directionToTarget using a dot product.
			float targetSpeedOrth = Vector3.Dot(relativeVel, directionToTarget);
			Vector3 relativeVelOrth = targetSpeedOrth * directionToTarget;

			// The tangential part is then found by subtracting the
			// result from the target velocity.
			Vector3 relativeVelTang = relativeVel - relativeVelOrth;

			// The tangential component of the velocities should be the same
			// (or there is no chance to hit)
			// THIS IS THE MAIN INSIGHT!
			Vector3 shotVelTang = relativeVelTang;

			if (tryHard)
				shotVelTang *= 3f;

			// Now all we have to find is the orthogonal velocity of the shot

			float shotVelSpeedSquared = shotVelTang.LengthSquared();
			if (shotVelSpeedSquared > shotSpeed * shotSpeed)
			{
				// Shot is too slow to intercept target.
				if (tryHard)
				{
					// direction is a trade-off between facing the target and fighting tangential velocity
					Vector3 direction = directionToTarget + displacementToTarget * 0.01f + shotVelTang;
					direction.Normalize();
					firingDirection = direction;
					contactPoint = shotOrigin + direction * distanceToTarget;
					return true;
				}
				firingDirection = Vector3.Zero;
				contactPoint = Vector3D.Zero;
				return false;
			}
			else
			{
				// We know the shot speed, and the tangential velocity.
				// Using pythagoras we can find the orthogonal velocity.
				float shotSpeedOrth = (float)Math.Sqrt(shotSpeed * shotSpeed - shotVelSpeedSquared);
				Vector3 shotVelOrth = directionToTarget * shotSpeedOrth;

				// Finally, add the tangential and orthogonal velocities.
				firingDirection = Vector3.Normalize(shotVelOrth + shotVelTang);

				// Find the time of collision (distance / relative velocity)
				float timeToCollision = distanceToTarget / (shotSpeedOrth - targetSpeedOrth);

				// Calculate where the shot will be at the time of collision
				Vector3 shotVel = shotVelOrth + shotVelTang;
				contactPoint = shotOrigin + (shotVel + shooterVel) * timeToCollision;
				return true;
			}
		}

		#endregion

	}
}
