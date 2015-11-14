using System;
using System.Linq;
using System.Collections.Generic;

using Sandbox.Common.ObjectBuilders;
using Sandbox.Game.Entities;
using Sandbox.ModAPI;
using VRage.Collections;
using VRage.ModAPI;
using VRageMath;

namespace Rynchodon.Weapons
{
	public abstract class TargetingBase
	{

		private static Dictionary<long, short> WeaponsTargetingProjectile = new Dictionary<long, short>();

		static TargetingBase()
		{
			MyAPIGateway.Entities.OnCloseAll += Entities_OnCloseAll;
		}

		private static void Entities_OnCloseAll()
		{
			MyAPIGateway.Entities.OnCloseAll -= Entities_OnCloseAll;
			WeaponsTargetingProjectile = null;
		}

		private static short GetWeaponsTargetingProjectile(IMyEntity entity)
		{
			short result;
			if (!WeaponsTargetingProjectile.TryGetValue(entity.EntityId, out result))
				result = 0;
			return result;
		}

		public readonly IMyEntity MyEntity;
		/// <summary>Either the weapon block that is targeting or the block that created the weapon.</summary>
		public readonly IMyCubeBlock CubeBlock;
		public readonly IMyFunctionalBlock FuncBlock;

		/// <summary>Iff true, the projectile will attempt to chase-down a target.</summary>
		protected bool TryHard = false;
		protected List<IMyEntity> PotentialObstruction = new List<IMyEntity>();

		/// <summary>Targets that cannot be hit.</summary>
		private readonly MyUniqueList<IMyEntity> Blacklist = new MyUniqueList<IMyEntity>();
		private readonly Dictionary<TargetType, List<IMyEntity>> Available_Targets = new Dictionary<TargetType, List<IMyEntity>>();
		private List<MyEntity> nearbyEntities = new List<MyEntity>();

	
		private readonly Logger myLogger;

		private Target value_CurrentTarget;
		/// <summary>The target that is being processed.</summary>
		protected Target myTarget;
		public TargetingOptions Options { get; protected set; }
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
				{
					short count = WeaponsTargetingProjectile[value_CurrentTarget.Entity.EntityId];
					if (count == 1)
						WeaponsTargetingProjectile.Remove(value_CurrentTarget.Entity.EntityId);
					else
						WeaponsTargetingProjectile[value_CurrentTarget.Entity.EntityId] = --count;
				}
				if (value != null && (value.TType & TargetType.Projectile) != 0)
				{
					short count;
					if (!WeaponsTargetingProjectile.TryGetValue(value.Entity.EntityId, out count))
						count = 0;
					WeaponsTargetingProjectile[value.Entity.EntityId] = ++count;
				}

				value_CurrentTarget = value;
			}
		}

		public TargetingBase(IMyEntity entity, IMyCubeBlock controllingBlock)
		{
			if (entity == null)
				throw new ArgumentNullException("entity");
			if (controllingBlock == null)
				throw new ArgumentNullException("controllingBlock");

			myLogger = new Logger("TargetingBase", () => entity.getBestName());
			MyEntity = entity;
			CubeBlock = controllingBlock;
			FuncBlock = controllingBlock as IMyFunctionalBlock;

			myTarget = new NoTarget();
			CurrentTarget = myTarget;
			Options = new TargetingOptions();

			myLogger.debugLog("entity: " + MyEntity.getBestName() + ", block: " + CubeBlock.getBestName(), "TargetingBase()");
		}

		public TargetingBase(IMyCubeBlock block)
			: this(block, block)
		{
			myLogger = new Logger("TargetingBase", block);
		}

		/// <summary>
		/// Determines if there is a physical reason the target cannot be hit such as an obstruction or a rotation limit.
		/// </summary>
		protected abstract bool PhysicalProblem(Vector3D targetPos);

		/// <summary>
		/// Determines the speed of the projectile.
		/// </summary>
		protected abstract float ProjectileSpeed(Vector3D targetPos);

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
			myLogger.debugLog("Blacklisting " + myTarget.Entity, "BlacklistTarget()");
			Blacklist.Add(myTarget.Entity);
			myTarget = new NoTarget();
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
				myLogger.debugLog("Not targeting, zero range", "UpdateTarget()");
				return;
			}

			switch (myTarget.TType)
			{
				case TargetType.Missile:
				case TargetType.Meteor:
					if (ProjectileIsThreat(myTarget.Entity, myTarget.TType))
					{
						myLogger.debugLog("Keeping Target = " + myTarget.Entity.getBestName(), "UpdateTarget()");
						return;
					}
					goto case TargetType.None;
				case TargetType.None:
				default:
					myTarget = new NoTarget();
					break;
			}

			CollectTargets();
			PickATarget();
			if (myTarget.Entity != null)
				myLogger.debugLog("myTarget = " + myTarget.Entity.getBestName(), "UpdateTarget()");
			CurrentTarget = myTarget;
		}

		/// <summary>
		/// Fills Available_Targets and PotentialObstruction
		/// </summary>
		private void CollectTargets()
		{
			//myLogger.debugLog("entered", "CollectTargets()");
			Available_Targets.Clear();
			PotentialObstruction.Clear();
			nearbyEntities.Clear();

			BoundingSphereD nearbySphere = new BoundingSphereD(ProjectilePosition(), Options.TargetingRange);
			MyGamePruningStructure.GetAllTopMostEntitiesInSphere(ref nearbySphere, nearbyEntities);

			foreach (IMyEntity entity in nearbyEntities)
			{
				//myLogger.debugLog("entity: " + entity.getBestName(), "CollectTargets()");

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
					IMyIdentity asIdentity = asChar.GetIdentity_Safe();
					if (asIdentity != null)
					{
						if (asIdentity.IsDead)
						{
							myLogger.debugLog("(s)he's dead, jim: " + entity.getBestName(), "CollectTargets()");
							continue;
						}
					}
					else
						myLogger.debugLog("Found a robot! : " + asChar + " . " + entity.getBestName(), "CollectTargets()");

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

			if (target.ToString().StartsWith("MyMissile"))
			{
				myLogger.debugLog("missile: " + target.getBestName() + ", type = " + tType + ", allowed targets = " + Options.CanTarget, "AddTarget()");
			}

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

			myLogger.debugLog("getting closest " + tType + ", from list of " + targetsOfType.Count, "SetClosest()");

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
						myLogger.debugLog("for type: " + tType + ", too far to target: " + target.getBestName(), "SetClosest()");
						continue;
					}

					if (PhysicalProblem(targetPosition))
					{
						myLogger.debugLog("can't target: " + target.getBestName() + ", obstructed", "SetClosest()");
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
		/// <para>Open doors should not be targeted.</para>
		/// </remarks>
		private bool TargetableBlock(IMyCubeBlock block, bool Disable)
		{
			if (!(block is IMyTerminalBlock))
				return false;

			if (Disable && !block.IsWorking)
				return block.IsFunctional && Options.FlagSet(TargetingFlags.Functional);

			if (block.Mass < 100)
				return false;

			IMyDoor asDoor = block as IMyDoor;
			return asDoor == null || asDoor.OpenRatio < 0.01;
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
		/// <para>Blocks from blocksToTarget will be given a distanceValue of the distance squared * (2 + index)^2.</para>
		/// <para>Other blocks will be given a distanceValue of the distance squared * (1e12).</para>
		/// </remarks>
		private bool GetTargetBlock(IMyCubeGrid grid, TargetType tType, out IMyCubeBlock target, out double distanceValue)
		{
			myLogger.debugLog("getting block from " + grid.DisplayName + ", target type = " + tType, "GetTargetBlock()");

			Vector3D myPosition = ProjectilePosition();
			CubeGridCache cache = CubeGridCache.GetFor(grid);

			target = null;
			distanceValue = double.MaxValue;

			if (cache.TotalByDefinition() == 0)
			{
				myLogger.debugLog("no terminal blocks on grid: " + grid.DisplayName, "GetTargetBlock()");
				return false;
			}

			// get decoy block
			{
				var decoyBlockList = cache.GetBlocksOfType(typeof(MyObjectBuilder_Decoy));
				if (decoyBlockList != null)
					foreach (IMyTerminalBlock block in decoyBlockList)
					{
						if (!block.IsWorking)
							continue;

						if (Blacklist.Contains(block))
							continue;

						double distanceSq = Vector3D.DistanceSquared(myPosition, block.GetPosition());
						if (distanceSq > Options.TargetingRangeSquared)
							continue;

						if (distanceSq < distanceValue && CubeBlock.canConsiderHostile(block as IMyCubeBlock))
						{
							target = block as IMyCubeBlock;
							distanceValue = distanceSq;
						}
					}
				if (target != null)
				{
					myLogger.debugLog("for type = " + tType + " and grid = " + grid.DisplayName + ", found a decoy block: " + target.DisplayNameText + ", distanceValue: " + distanceValue, "GetTargetBlock()");
					return true;
				}
			}

			// get block from blocksToTarget
			int multiplier = 1;
			foreach (string blocksSearch in Options.blocksToTarget)
			{
				multiplier++;
				var master = cache.GetBlocksByDefLooseContains(blocksSearch);
				foreach (var blocksWithDef in master)
					foreach (IMyCubeBlock block in blocksWithDef)
					{
						if (!TargetableBlock(block, true))
						{
							myLogger.debugLog("not targetable: " + block.DisplayNameText, "GetTargetBlock()");
							continue;
						}

						if (Blacklist.Contains(block))
						{
							myLogger.debugLog("blacklisted: " + block.DisplayNameText, "GetTargetBlock()");
							continue;
						}

						double distanceSq = Vector3D.DistanceSquared(myPosition, block.GetPosition());
						if (distanceSq > Options.TargetingRangeSquared)
						{
							myLogger.debugLog("too far: " + block.DisplayNameText + ", distanceSq = " + distanceSq + ", TargetingRangeSquared = " + Options.TargetingRangeSquared, "GetTargetBlock()");
							continue;
						}
						distanceSq *= multiplier * multiplier * multiplier;

						myLogger.debugLog("blocksSearch = " + blocksSearch + ", block = " + block.DisplayNameText + ", distance value = " + distanceSq, "GetTargetBlock()");
						if (distanceSq < distanceValue && CubeBlock.canConsiderHostile(block))
						{
							target = block;
							distanceValue = distanceSq;
						}
						else
							myLogger.debugLog("have a closer block than " + block.DisplayNameText + ", close = " + target.getBestName() + ", distance value = " + distanceValue, "GetTargetBlock()");
					}
				if (target != null) // found a block from blocksToTarget
				{
					myLogger.debugLog("for type = " + tType + " and grid = " + grid.DisplayName + ", blocksSearch = " + blocksSearch + ", target = " + target.DisplayNameText + ", distanceValue = " + distanceValue, "GetTargetBlock()");
					return true;
				}
			}

			// get any terminal block
			if (tType == TargetType.Moving || tType == TargetType.Destroy)
			{
				List<IMySlimBlock> allSlims = new List<IMySlimBlock>();
				grid.GetBlocks_Safe(allSlims, (slim) => slim.FatBlock != null);

				foreach (IMySlimBlock slim in allSlims)
					if (TargetableBlock(slim.FatBlock, false))
					{
						if (Blacklist.Contains(slim.FatBlock))
							continue;

						double distanceSq = Vector3D.DistanceSquared(myPosition, slim.FatBlock.GetPosition());
						if (distanceSq > Options.TargetingRangeSquared)
							continue;
						distanceSq *= 1e12;

						if (CubeBlock.canConsiderHostile(slim.FatBlock))
						{
							target = slim.FatBlock;
							distanceValue = distanceSq;
							myLogger.debugLog("for type = " + tType + " and grid = " + grid.DisplayName + ", found a block: " + target.DisplayNameText + ", distanceValue = " + distanceValue, "GetTargetBlock()");
							return true;
						}
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

					//bool isMissile = entity.ToString().StartsWith("MyMissile");

					//if ((isMissile || entity is IMyMeteor) && Target.WeaponsTargeting(entity.EntityId) > 10)
					//{
					//	myLogger.debugLog("Not targeting " + entity.getBestName() + ", already targeted by " + Target.WeaponsTargeting(entity.EntityId) + " weapons", "PickAProjectile()");
					//	continue;
					//}

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
							myLogger.debugLog("failed to get a block from: " + asGrid.DisplayName, "PickAProjectile()");
							continue;
						}
					}

					if (ProjectileIsThreat(projectile, tType) && !PhysicalProblem(projectile.GetPosition()))
					{
						myLogger.debugLog("Is a threat: " + projectile.getBestName(), "PickAProjectile()");
						myTarget = new TurretTarget(projectile, tType);
						return true;
					}
					else
						myLogger.debugLog("Not a threat: " + projectile.getBestName(), "PickAProjectile()");
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
				myLogger.debugLog("projectile: " + projectile.getBestName() + ", is moving towards weapon. D0 = " + Vector3D.DistanceSquared(weaponPosition, nextPosition) + ", D1 = " + Vector3D.DistanceSquared(weaponPosition, projectilePosition), "ProjectileIsThreat()");
				return true;
			}
			else
			{
				myLogger.debugLog("projectile: " + projectile.getBestName() + ", is moving away from weapon. D0 = " + Vector3D.DistanceSquared(weaponPosition, nextPosition) + ", D1 = " + Vector3D.DistanceSquared(weaponPosition, projectilePosition), "ProjectileIsThreat()");
				return false;
			}
		}

		#endregion

		#region Target Interception

		/// <summary>
		/// Calculates FiringDirection and InterceptionPoint
		/// </summary>
		/// TODO: if target is accelerating, look ahead (missiles and such)
		protected void SetFiringDirection()
		{
			if (myTarget.Entity == null || myTarget.Entity.MarkedForClose)
				return;
			myLogger.debugLog(MyEntity.MarkedForClose, "MyEntity.MarkedForClose", "SetFiringDirection()", Logger.severity.FATAL);

			Vector3D TargetPosition = myTarget.GetPosition();

			FindInterceptVector(ProjectilePosition(), MyEntity.GetLinearVelocity(), ProjectileSpeed(TargetPosition), TargetPosition, myTarget.GetLinearVelocity());
			if (myTarget.Entity != null && PhysicalProblem(myTarget.InterceptionPoint.Value))
			{
				myLogger.debugLog("Shot path is obstructed, blacklisting " + myTarget.Entity.getBestName(), "SetFiringDirection()");
				BlacklistTarget();
				return;
			}
			myLogger.debugLog("got an intercept vector: " + myTarget.FiringDirection + ", target position: " + TargetPosition + ", by entity: " + myTarget.Entity.GetCentre(), "SetFiringDirection()");
			CurrentTarget = myTarget;
		}

		/// <remarks>From http://danikgames.com/blog/moving-target-intercept-in-3d/</remarks>
		private void FindInterceptVector(Vector3 shotOrigin, Vector3 shooterVel, float shotSpeed, Vector3 targetOrigin, Vector3 targetVel)
		{
			Vector3 relativeVel = targetVel - shooterVel;
			Vector3 displacementToTarget = targetOrigin - shotOrigin;
			float distanceToTarget = displacementToTarget.Length();
			Vector3 directionToTarget = displacementToTarget / distanceToTarget;

			// Decompose the target's velocity into the part parallel to the
			// direction to the cannon and the part tangential to it.
			// The part towards the cannon is found by projecting the target's
			// velocity on directionToTarget using a dot product.
			float targetSpeedOrth = Vector3.Dot(relativeVel, directionToTarget);
			Vector3 targetVelOrth = targetSpeedOrth * directionToTarget;

			// The tangential part is then found by subtracting the
			// result from the target velocity.
			Vector3 targetVelTang = relativeVel - targetVelOrth;

			// The tangential component of the velocities should be the same
			// (or there is no chance to hit)
			// THIS IS THE MAIN INSIGHT!
			Vector3 shotVelTang = targetVelTang;

			// Now all we have to find is the orthogonal velocity of the shot

			float shotVelSpeedSquared = shotVelTang.LengthSquared();
			if (shotVelSpeedSquared > shotSpeed * shotSpeed)
			{
				// Shot is too slow to intercept target.
				if (TryHard)
				{
					myLogger.debugLog("shot too slow, trying anyway", "FindInterceptVector()");
					// direction is a trade-off between facing the target and fighting tangential velocity
					Vector3 direction = directionToTarget + displacementToTarget * 0.01f + shotVelTang;
					direction.Normalize();
					myTarget.FiringDirection = direction;
					myTarget.InterceptionPoint = shotOrigin + direction * distanceToTarget;
					return;
				}
				myLogger.debugLog("shot too slow, blacklisting", "FindInterceptVector()");
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
				Vector3 interceptDirection = Vector3.Normalize(shotVelOrth + shotVelTang);

				// Find the time of collision (distance / relative velocity)
				float timeToCollision = distanceToTarget / (shotSpeedOrth - targetSpeedOrth);

				// Calculate where the shot will be at the time of collision
				Vector3 shotVel = shotVelOrth + shotVelTang;
				Vector3 interceptPoint = shotOrigin + (shotVel + shooterVel) * timeToCollision;

				myTarget.FiringDirection = interceptDirection;
				myTarget.InterceptionPoint = interceptPoint;
			}
		}

		#endregion

	}
}
