using System;
using Sandbox.Common;
using Sandbox.ModAPI;
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

		public static bool HasFlagFast(this Relations rel, Relations flag)
		{ return (rel & flag) == flag; }

		private static bool toIsFriendly(Relations rel)
		{
			if (rel.HasFlagFast(Relations.Enemy))
				return false;

			return rel.HasFlagFast(Relations.Owner) || rel.HasFlagFast(Relations.Faction);
		}

		private static bool toIsHostile(Relations rel)
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
			if (player == null)
				throw new ArgumentNullException("player");

			return GetRelations(player.GetRelationTo(playerID));
		}

		public static Relations getRelationsTo(this IMyPlayer player, IMyCubeGrid target, Relations breakOn = Relations.None)
		{
			if (player == null)
				throw new ArgumentNullException("player");
			if (target == null)
				throw new ArgumentNullException("grid");

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
			VRage.Exceptions.ThrowIf<ArgumentNullException>(block == null, "block");

			return GetRelations( block.GetUserRelationToOwner(playerID));
		}

		public static Relations getRelationsTo(this IMyCubeBlock block, IMyCubeBlock target)
		{
			VRage.Exceptions.ThrowIf<ArgumentNullException>(block == null, "block");
			VRage.Exceptions.ThrowIf<ArgumentNullException>(target == null, "target");

			if (block.OwnerId == 0 || target.OwnerId == 0)
				return Relations.None;
			return block.getRelationsTo(target.OwnerId);
		}

		public static Relations getRelationsTo(this IMyCubeBlock block, IMyCubeGrid target, Relations breakOn = Relations.None)
		{
			VRage.Exceptions.ThrowIf<ArgumentNullException>(block == null, "block");
			VRage.Exceptions.ThrowIf<ArgumentNullException>(target == null, "target");

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

			IMyPlayer asPlayer = target as IMyPlayer;
			if (asPlayer != null)
				return block.getRelationsTo(asPlayer.IdentityId);

			throw new InvalidOperationException("Cannot handle: " + target);
		}


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


		public static bool canControlBlock(this IMyCubeBlock block, IMyCubeBlock target)
		{
			switch (block.getRelationsTo(target))
			{
				case Relations.Faction:
				case Relations.Owner:
					return true;
				case Relations.None:
					return target.OwnerId == 0;
				default:
					return false;
			}
		}
	}
}
