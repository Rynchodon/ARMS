using System;
using Rynchodon.Weapons.Guided;
using Sandbox.Common;
using Sandbox.ModAPI;
using VRage.Game;
using VRage.ModAPI;

namespace Rynchodon
{
	public static class ExtensionsRelations
	{
		[Flags]
		public enum Relations : byte
		{
			None = 0,
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
			Owner = 8
		}

		private static readonly Relations[] relationsPriority = new Relations[] { Relations.Enemy, Relations.Owner, Relations.Faction, Relations.Neutral };

		/// <summary>Checks if the Relations has a flag set or any of a group of flags.</summary>
		public static bool HasAnyFlag(this Relations rel, Relations flag)
		{ return (rel & flag) != 0; }

		public static bool toIsFriendly(Relations rel)
		{
			if (rel.HasAnyFlag(Relations.Enemy))
				return false;

			return rel.HasAnyFlag(Relations.Owner) || rel.HasAnyFlag(Relations.Faction);
		}

		public static bool toIsHostile(Relations rel)
		{
			if (rel.HasAnyFlag(Relations.Enemy))
				return true;

			if (rel == Relations.None)
				return true;

			return false;
		}

		public static Relations highestPriority(this Relations rel)
		{
			foreach (Relations flag in relationsPriority)
				if (rel.HasAnyFlag(flag))
					return flag;
			return Relations.None;
		}

		public static byte PriorityOrder(this Relations rel)
		{
			for (byte i = 0; i < relationsPriority.Length; i++)
				if (rel.HasAnyFlag(relationsPriority[i]))
					return i;
			return (byte)relationsPriority.Length;
		}

		public static Relations getRelationsTo(this long playerId1, long playerId2)
		{
			if (playerId1 == playerId2)
				return Relations.Owner;

			IMyFaction fact1 = MyAPIGateway.Session.Factions.TryGetPlayerFaction(playerId1);
			if (fact1 == null)
				return Relations.Enemy;
			IMyFaction fact2 = MyAPIGateway.Session.Factions.TryGetPlayerFaction(playerId2);
			if (fact2 == null)
				return Relations.Enemy;

			if (fact1  == fact2)
				return Relations.Faction;

			if (MyAPIGateway.Session.Factions.GetRelationBetweenFactions(fact1.FactionId, fact2.FactionId) == MyRelationsBetweenFactions.Neutral)
				return Relations.Neutral;

			return Relations.Enemy;
		}

		public static Relations getRelationsTo(this long playerId, IMyCubeGrid target, Relations breakOn = Relations.None)
		{
			if (target.BigOwners.Count == 0 && target.SmallOwners.Count == 0) // grid has no owner
				return Relations.Enemy;

			Relations relationsToGrid = Relations.None;
			foreach (long gridOwner in target.BigOwners)
			{
				relationsToGrid |= getRelationsTo(playerId, gridOwner);
				if (breakOn != Relations.None && relationsToGrid.HasAnyFlag(breakOn))
					return relationsToGrid;
			}

			foreach (long gridOwner in target.SmallOwners)
			{
				relationsToGrid |= getRelationsTo(playerId, gridOwner);
				if (breakOn != Relations.None && relationsToGrid.HasAnyFlag(breakOn))
					return relationsToGrid;
			}

			return relationsToGrid;
		}

		public static Relations getRelationsTo(this IMyPlayer player, long playerID)
		{
			return getRelationsTo(player.PlayerID, playerID);
		}

		public static Relations getRelationsTo(this IMyPlayer player, IMyCubeBlock block)
		{
			return getRelationsTo(player.PlayerID, block.OwnerId);
		}

		public static Relations getRelationsTo(this IMyPlayer player, IMyCubeGrid target, Relations breakOn = Relations.None)
		{
			return getRelationsTo(player.PlayerID, target, breakOn);
		}

		public static Relations getRelationsTo(this IMyCubeBlock block, long playerId)
		{
			return getRelationsTo(block.OwnerId, playerId);
		}

		public static Relations getRelationsTo(this IMyCubeBlock block1, IMyCubeBlock block2)
		{
			return getRelationsTo(block1.OwnerId, block2.OwnerId);
		}

		public static Relations getRelationsTo(this IMyCubeBlock block, IMyCubeGrid target, Relations breakOn = Relations.None)
		{
			return getRelationsTo(block.OwnerId, target, breakOn);
		}

		public static Relations getRelationsTo(this IMyCubeBlock block, object target, Relations breakOn = Relations.None)
		{
			IMyEntity entity = target as IMyEntity;
			if (entity != null)
			{
				IMyCubeBlock asBlock = target as IMyCubeBlock;
				if (asBlock != null)
					return block.getRelationsTo(asBlock);

				IMyCubeGrid asGrid = target as IMyCubeGrid;
				if (asGrid != null)
					return block.getRelationsTo(asGrid, breakOn);

				IMyCharacter asChar = target as IMyCharacter;
				if (asChar != null)
				{
					IMyPlayer player = asChar.GetPlayer_Safe();
					if (player != null)
						return block.getRelationsTo(player.IdentityId);
				}

				long missileOwner = GuidedMissile.GetOwnerId(entity.EntityId);
				return block.getRelationsTo(missileOwner);
			}

			IMyPlayer asPlayer = target as IMyPlayer;
			if (asPlayer != null)
				return block.getRelationsTo(asPlayer.IdentityId);

			throw new InvalidOperationException("Cannot handle: " + target);
		}


		public static bool canConsiderFriendly(this long playerId, long target)
		{
			return toIsFriendly(getRelationsTo(playerId, target));
		}

		public static bool canConsiderHostile(this long playerId, long target)
		{
			return toIsHostile(getRelationsTo(playerId, target));
		}

		public static bool canConsiderFriendly(this IMyPlayer player, long playerID)
		{ return toIsFriendly(player.getRelationsTo(playerID)); }
		
		public static bool canConsiderFriendly(this IMyCubeBlock block, long playerID)
		{ return toIsFriendly(block.getRelationsTo(playerID)); }

		public static bool canConsiderFriendly(this IMyCubeBlock block, IMyCubeBlock target)
		{ return toIsFriendly(block.getRelationsTo(target)); }

		public static bool canConsiderFriendly(this IMyCubeBlock block, IMyCubeGrid target)
		{ return toIsFriendly(block.getRelationsTo(target, Relations.Enemy)); }

		public static bool canConsiderFriendly(this IMyCubeBlock block, IMyEntity target)
		{ return toIsFriendly(block.getRelationsTo(target, Relations.Enemy)); }


		public static bool canConsiderHostile(this IMyCubeBlock block, long playerID)
		{ return toIsHostile(block.getRelationsTo(playerID)); }

		public static bool canConsiderHostile(this IMyCubeBlock block, IMyCubeBlock target)
		{ return toIsHostile(block.getRelationsTo(target)); }

		public static bool canConsiderHostile(this IMyCubeBlock block, IMyCubeGrid target)
		{ return toIsHostile(block.getRelationsTo(target, Relations.Enemy)); }

		public static bool canConsiderHostile(this IMyCubeBlock block, IMyEntity target)
		{ return toIsHostile(block.getRelationsTo(target, Relations.Enemy)); }


		/// <summary>
		/// Different from the others because share mode matters.
		/// </summary>
		public static bool canControlBlock(this IMyCubeBlock block, IMyCubeBlock target)
		{
			switch (target.GetUserRelationToOwner(block.OwnerId))
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

	}
}
