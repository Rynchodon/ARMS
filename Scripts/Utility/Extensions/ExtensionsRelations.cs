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
		/// <summary>
		/// <para>For a grid, we normally consider the worst relationship.</para>
		/// <para>Eg. A grid that contains any Neutral blocks and no Enemy blocks shall be considered Neutral.</para>
		/// <para>A grid that is completely unowned shall be considered Enemy.</para>
		/// </summary>
		[Flags]
		public enum Relations : byte
		{
			/// <summary>
			/// Owner/Player is "nobody"
			/// </summary>
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
		public static bool HasFlagFast(this Relations rel, Relations flag)
		{ return (rel & flag) != 0; }

		public static bool toIsFriendly(Relations rel)
		{
			if (rel.HasFlagFast(Relations.Enemy))
				return false;

			return rel.HasFlagFast(Relations.Owner) || rel.HasFlagFast(Relations.Faction);
		}

		public static bool toIsHostile(Relations rel)
		{
			if (rel.HasFlagFast(Relations.Enemy))
				return true;

			if (rel == Relations.None)
				return true;

			return false;
		}

		public static Relations highestPriority(this Relations rel)
		{
			foreach (Relations flag in relationsPriority)
				if (rel.HasFlagFast(flag))
					return flag;
			return Relations.None;
		}

		public static byte PriorityOrder(this Relations rel)
		{
			for (byte i = 0; i < relationsPriority.Length; i++)
				if (rel.HasFlagFast(relationsPriority[i]))
					return i;
			return (byte)relationsPriority.Length;
		}

		private static Relations GetRelations(MyRelationsBetweenPlayerAndBlock relations)
		{
			switch (relations)
			{
				case MyRelationsBetweenPlayerAndBlock.Enemies:
					return Relations.Enemy;
				case MyRelationsBetweenPlayerAndBlock.FactionShare:
					return Relations.Faction;
				case MyRelationsBetweenPlayerAndBlock.Neutral:
					return Relations.Neutral;
				case MyRelationsBetweenPlayerAndBlock.Owner:
					return Relations.Owner;
				case MyRelationsBetweenPlayerAndBlock.NoOwnership:
				default:
					return Relations.None;
			}
		}

		public static Relations getRelationsTo(this IMyPlayer player, long playerID)
		{
			return GetRelations(player.GetRelationTo(playerID));
		}

		public static Relations getRelationsTo(this IMyPlayer player, IMyCubeGrid target, Relations breakOn = Relations.None)
		{
			if (target.BigOwners.Count == 0 && target.SmallOwners.Count == 0) // grid has no owner
				return Relations.Enemy;

			Relations relationsToGrid = Relations.None;
			foreach (long gridOwner in target.BigOwners)
			{
				relationsToGrid |= player.getRelationsTo(gridOwner);
				if (breakOn != Relations.None && relationsToGrid.HasFlagFast(breakOn))
					return relationsToGrid;
			}

			foreach (long gridOwner in target.SmallOwners)
			{
				relationsToGrid |= player.getRelationsTo(gridOwner);
				if (breakOn != Relations.None && relationsToGrid.HasFlagFast(breakOn))
					return relationsToGrid;
			}

			return relationsToGrid;
		}

		private static Relations getRelationsTo(this IMyCubeBlock block, long playerID)
		{
			if (block.OwnerId == playerID)
				return Relations.Owner;

			IMyFaction fact1 = MyAPIGateway.Session.Factions.TryGetPlayerFaction(block.OwnerId);
			if (fact1 == null)
				return Relations.Enemy;
			IMyFaction fact2 = MyAPIGateway.Session.Factions.TryGetPlayerFaction(playerID);
			if (fact2 == null)
				return Relations.Enemy;

			if (fact1  == fact2)
				return Relations.Faction;

			if (MyAPIGateway.Session.Factions.GetRelationBetweenFactions(fact1.FactionId, fact2.FactionId) == MyRelationsBetweenFactions.Neutral)
				return Relations.Neutral;

			return Relations.Enemy;
		}

		public static Relations getRelationsTo(this IMyCubeBlock block, IMyCubeBlock target)
		{
			if (block.OwnerId == 0 || target.OwnerId == 0)
				return Relations.None;
			return block.getRelationsTo(target.OwnerId);
		}

		public static Relations getRelationsTo(this IMyCubeBlock block, IMyCubeGrid target, Relations breakOn = Relations.None)
		{
			if (target.BigOwners.Count == 0 && target.SmallOwners.Count == 0) // grid has no owner
				return Relations.Enemy;

			Relations relationsToGrid = Relations.None;
			foreach (long gridOwner in target.BigOwners)
			{
				relationsToGrid |= block.getRelationsTo(gridOwner);
				if (breakOn != Relations.None && relationsToGrid.HasFlagFast(breakOn))
					return relationsToGrid;
			}

			foreach (long gridOwner in target.SmallOwners)
			{
				relationsToGrid |= block.getRelationsTo(gridOwner);
				if (breakOn != Relations.None && relationsToGrid.HasFlagFast(breakOn))
					return relationsToGrid;
			}

			return relationsToGrid;
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


		public static bool canConsiderFriendly(this IMyPlayer player, long playerID)
		{ return toIsFriendly(player.getRelationsTo(playerID)); }
		
		public static bool canConsiderFriendly(this IMyCubeBlock block, long playerID)
		{ return toIsFriendly(block.getRelationsTo(playerID)); }

		public static bool canConsiderFriendly(this IMyCubeBlock block, IMyCubeBlock target)
		{ return toIsFriendly(block.getRelationsTo(target)); }

		public static bool canConsiderFriendly(this IMyCubeBlock block, IMyCubeGrid target)
		{ return toIsFriendly(block.getRelationsTo(target, Relations.Enemy | Relations.Neutral)); }

		public static bool canConsiderFriendly(this IMyCubeBlock block, IMyEntity target)
		{ return toIsFriendly(block.getRelationsTo(target, Relations.Enemy | Relations.Neutral)); }


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
