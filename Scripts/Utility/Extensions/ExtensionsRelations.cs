using System;
using Rynchodon.Weapons.Guided;
using Sandbox.ModAPI;
using VRage.Game;
using VRage.Game.ModAPI;
using VRage.ModAPI;

namespace Rynchodon
{
	public static class ExtensionsRelations
	{
		// Projector is "extending" this enum
		[Flags]
		public enum Relations : byte
		{
			NoOwner = 0,
			/// <summary>
			/// Owner/Player is a member of an at war faction
			/// </summary>
			Enemy = 1,
			/// <summary>
			/// Owner/Player is a member of an at peace faction
			/// </summary>
			Neutral = 2,
			/// <summary>
			/// Owner/Player is a member of the same faction
			/// </summary>
			Faction = 4,
			/// <summary>
			/// Owner/Player is the same player
			/// </summary>
			Owner = 8,

			Friendly = Faction | Owner
		}

		private static readonly Relations[] relationsPriority = new Relations[] { Relations.Enemy, Relations.Owner, Relations.Faction, Relations.Neutral };

		/// <summary>Checks if the Relations has a flag set or any of a group of flags.</summary>
		public static bool HasAnyFlag(this Relations rel, Relations flag)
		{ return (rel & flag) != 0; }

		public static bool toIsFriendly(Relations rel)
		{
			if (rel.HasAnyFlag(Relations.Enemy))
				return false;

			return rel.HasAnyFlag(Relations.Friendly);
		}

		public static bool toIsHostile(Relations rel, bool ownerlessHostile = true)
		{
			if (rel.HasAnyFlag(Relations.Enemy))
				return true;

			if (rel == Relations.NoOwner)
				return ownerlessHostile;

			return false;
		}

		public static Relations highestPriority(this Relations rel)
		{
			foreach (Relations flag in relationsPriority)
				if (rel.HasAnyFlag(flag))
					return flag;
			return Relations.NoOwner;
		}

		public static byte PriorityOrder(this Relations rel)
		{
			for (byte i = 0; i < relationsPriority.Length; i++)
				if (rel.HasAnyFlag(relationsPriority[i]))
					return i;
			return (byte)relationsPriority.Length;
		}

		public static Relations getRelationsTo(this long identityId1, long identityId2)
		{
			if (identityId1 == identityId2)
				return Relations.Owner;

			if (identityId1 == 0L || identityId2 == 0L)
				return Relations.NoOwner;

			IMyFaction fact1 = MyAPIGateway.Session.Factions.TryGetPlayerFaction(identityId1);
			if (fact1 == null)
				return Relations.Enemy;
			IMyFaction fact2 = MyAPIGateway.Session.Factions.TryGetPlayerFaction(identityId2);
			if (fact2 == null)
				return Relations.Enemy;

			if (fact1  == fact2)
				return Relations.Faction;

			if (MyAPIGateway.Session.Factions.GetRelationBetweenFactions(fact1.FactionId, fact2.FactionId) == MyRelationsBetweenFactions.Neutral)
				return Relations.Neutral;

			return Relations.Enemy;
		}

		public static Relations getRelationsTo(this long identityId, IMyCubeGrid target, Relations breakOn = Relations.NoOwner)
		{
			if (identityId == 0L)
				return Relations.NoOwner;

			IMyPlayer controlling = MyAPIGateway.Players.GetPlayerControllingEntity(target);
			if (controlling != null)
				return getRelationsTo(identityId, controlling.IdentityId); 

			if (target.BigOwners.Count == 0 && target.SmallOwners.Count == 0) // grid has no owner
				return Relations.NoOwner;

			Relations relationsToGrid = Relations.NoOwner;
			foreach (long gridOwner in target.BigOwners)
			{
				relationsToGrid |= getRelationsTo(identityId, gridOwner);
				if (breakOn != Relations.NoOwner && relationsToGrid.HasAnyFlag(breakOn))
					return relationsToGrid;
			}

			foreach (long gridOwner in target.SmallOwners)
			{
				relationsToGrid |= getRelationsTo(identityId, gridOwner);
				if (breakOn != Relations.NoOwner && relationsToGrid.HasAnyFlag(breakOn))
					return relationsToGrid;
			}

			return relationsToGrid;
		}

		public static Relations getRelationsTo(this long identityId, IMyEntity target, Relations breakOn = Relations.NoOwner)
		{
			if (identityId == 0L)
				return Relations.NoOwner;

			IMyCubeBlock asBlock = target as IMyCubeBlock;
			if (asBlock != null)
				return getRelationsTo(identityId, asBlock.OwnerId);

			IMyCubeGrid asGrid = target as IMyCubeGrid;
			if (asGrid != null)
				return getRelationsTo(identityId, asGrid, breakOn);

			IMyCharacter asChar = target as IMyCharacter;
			if (asChar != null)
			{
				if (asChar.IsBot)
					return Relations.Enemy;
				IMyPlayer player = asChar.GetPlayer_Safe();
				if (player != null)
					return getRelationsTo(identityId, player.IdentityId);

				Logger.DebugLog("character not found, treating as enemy: " + target.getBestName(), Logger.severity.WARNING);
				return Relations.Enemy;
			}

			if (target is IMyMeteor)
				return Relations.Enemy;

			if (target.IsMissile())
			{
				long missileOwner;
				if (GuidedMissile.TryGetOwnerId(target.EntityId, out missileOwner))
					return getRelationsTo(identityId, missileOwner);
				return Relations.Enemy;
			}

			Logger.DebugLog("unknown entity, treating as enemy: " + target.getBestName(), Logger.severity.WARNING);
			return Relations.Enemy;
		}

		public static Relations getRelationsTo(this IMyPlayer player, long identityID)
		{
			return getRelationsTo(player.IdentityId, identityID);
		}

		public static Relations getRelationsTo(this IMyPlayer player, IMyCubeBlock block)
		{
			return getRelationsTo(player.IdentityId, block.OwnerId);
		}

		public static Relations getRelationsTo(this IMyPlayer player, IMyCubeGrid target, Relations breakOn = Relations.NoOwner)
		{
			return getRelationsTo(player.IdentityId, target, breakOn);
		}

		public static Relations getRelationsTo(this IMyCubeBlock block, long identityId)
		{
			return getRelationsTo(block.OwnerId, identityId);
		}

		public static Relations getRelationsTo(this IMyCubeBlock block1, IMyCubeBlock block2)
		{
			return getRelationsTo(block1.OwnerId, block2.OwnerId);
		}

		public static Relations getRelationsTo(this IMyCubeBlock block, IMyCubeGrid target, Relations breakOn = Relations.NoOwner)
		{
			return getRelationsTo(block.OwnerId, target, breakOn);
		}

		public static Relations getRelationsTo(this IMyCubeBlock block, IMyEntity target, Relations breakOn = Relations.NoOwner)
		{
			return getRelationsTo(block.OwnerId, target, breakOn);
		}


		public static bool canConsiderFriendly(this long identityId, long target)
		{
			return toIsFriendly(getRelationsTo(identityId, target));
		}

		public static bool canConsiderFriendly(this long identityId, IMyCubeGrid target)
		{
			return toIsFriendly(getRelationsTo(identityId, target));
		}

		public static bool canConsiderHostile(this long identityId, long target, bool ownerlessHostile = true)
		{
			return toIsHostile(getRelationsTo(identityId, target));
		}


		public static bool canConsiderFriendly(this IMyPlayer player, long identityId)
		{ return toIsFriendly(player.getRelationsTo(identityId)); }
		
		public static bool canConsiderFriendly(this IMyCubeBlock block, long identityId)
		{ return toIsFriendly(block.getRelationsTo(identityId)); }

		public static bool canConsiderFriendly(this IMyCubeBlock block, IMyCubeBlock target)
		{ return toIsFriendly(block.getRelationsTo(target)); }

		public static bool canConsiderFriendly(this IMyCubeBlock block, IMyCubeGrid target)
		{ return toIsFriendly(block.getRelationsTo(target, Relations.Enemy)); }

		public static bool canConsiderFriendly(this IMyCubeBlock block, IMyEntity target)
		{ return toIsFriendly(block.getRelationsTo(target, Relations.Enemy)); }


		public static bool canConsiderHostile(this IMyCubeBlock block, long identityId, bool ownerlessHostile = true)
		{ return toIsHostile(block.getRelationsTo(identityId), ownerlessHostile); }

		public static bool canConsiderHostile(this IMyCubeBlock block, IMyCubeBlock target, bool ownerlessHostile = true)
		{ return toIsHostile(block.getRelationsTo(target), ownerlessHostile); }

		public static bool canConsiderHostile(this IMyCubeBlock block, IMyCubeGrid target, bool ownerlessHostile = true)
		{ return toIsHostile(block.getRelationsTo(target, Relations.Enemy), ownerlessHostile); }

		public static bool canConsiderHostile(this IMyCubeBlock block, IMyEntity target, bool ownerlessHostile = true)
		{ return toIsHostile(block.getRelationsTo(target, Relations.Enemy), ownerlessHostile); }


		/// <remarks>
		/// Different from the others because share mode matters.
		/// </remarks>
		public static bool canControlBlock(this long ownerId, VRage.Game.ModAPI.Ingame.IMyCubeBlock target)
		{
			switch (target.GetUserRelationToOwner(ownerId))
			{
				case MyRelationsBetweenPlayerAndBlock.Enemies:
				case MyRelationsBetweenPlayerAndBlock.Neutral:
					return false;
				case MyRelationsBetweenPlayerAndBlock.FactionShare:
				case MyRelationsBetweenPlayerAndBlock.Owner:
					return true;
				case MyRelationsBetweenPlayerAndBlock.NoOwnership:
				default:
					return target.OwnerId == 0;
			}
		}

		/// <remarks>
		/// Different from the others because share mode matters.
		/// </remarks>
		public static bool canControlBlock(this VRage.Game.ModAPI.Ingame.IMyCubeBlock block, VRage.Game.ModAPI.Ingame.IMyCubeBlock target)
		{
			return canControlBlock(block.OwnerId, target);
		}

	}
}
