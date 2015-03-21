using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

using Sandbox.Common;
using Sandbox.Common.ObjectBuilders;
using Sandbox.ModAPI;

namespace Rynchodon
{
	public static class IMyCubeBlockExtensions
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

		public static bool HasFlagFast(this Relations rel, Relations flag)
		{ return (rel & flag) > 0; }

		private static bool toIsFriendly(Relations rel)
		{
			if (rel.HasFlagFast(Relations.Enemy))
				return false;
			if (rel.HasFlagFast(Relations.Neutral))
				return false;

			if (rel == Relations.None)
				return false;

			return true;
		}

		private static bool toIsHostile(Relations rel)
		{
			if (rel.HasFlagFast(Relations.Enemy))
				return true;

			if (rel == Relations.None)
				return true;

			return false;
		}

		public static Relations mostHostile(this Relations rel)
		{
			foreach (Relations flag in new Relations[] { Relations.Enemy, Relations.Neutral, Relations.Faction, Relations.Owner })
				if (rel.HasFlagFast(flag))
					return flag;
			return Relations.None;
		}

		private static Relations getRelationsTo(this IMyCubeBlock block, long playerID)
		{
			switch (block.GetUserRelationToOwner(playerID))
			{
				case MyRelationsBetweenPlayerAndBlock.Enemies:
					return Relations.Enemy;
				case MyRelationsBetweenPlayerAndBlock.Neutral:
					return Relations.Neutral;
				case MyRelationsBetweenPlayerAndBlock.FactionShare:
					return Relations.Faction;
				case MyRelationsBetweenPlayerAndBlock.Owner:
					return Relations.Owner;
				default:
					return Relations.None;
			}
		}

		public static Relations getRelationsTo(this IMyCubeBlock block, IMyCubeBlock target)
		{
			if (block.OwnerId == 0 || target.OwnerId == 0)
				return Relations.None;
			return block.getRelationsTo(target.OwnerId) ;
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

		public static bool canConsiderFriendly(this IMyCubeBlock block, long playerID)
		{ return toIsFriendly(block.getRelationsTo(playerID));}

		public static bool canConsiderFriendly(this IMyCubeBlock block, IMyCubeBlock target)
		{ return toIsFriendly(block.getRelationsTo(target)); }

		public static bool canConsiderFriendly(this IMyCubeBlock block, IMyCubeGrid target)
		{ return toIsFriendly(block.getRelationsTo(target, Relations.Enemy | Relations.Neutral)); }


		public static bool canConsiderHostile(this IMyCubeBlock block, long playerID)
		{ return toIsHostile(block.getRelationsTo(playerID)); }

		public static bool canConsiderHostile(this IMyCubeBlock block, IMyCubeBlock target)
		{ return toIsHostile(block.getRelationsTo(target)); }

		public static bool canConsiderHostile(this IMyCubeBlock block, IMyCubeGrid target)
		{ return toIsHostile(block.getRelationsTo(target, Relations.Enemy)); }


		public static bool canControlBlock(this IMyCubeBlock block, IMyCubeBlock target)
		{
			switch (block.getRelationsTo(target))
			{
				case Relations.Faction:
				case Relations.Owner:
				case Relations.None:
					return true;
				default:
					return false;
			}
		}


		/// <summary>
		/// Tests for sendFrom is working and grid attached or in range. Use without range to skip range test. If sendTo is not a block or a grid, will skip grid test. If sendTo is a block, it must be working.
		/// </summary>
		/// <param name="sendFrom"></param>
		/// <param name="sendTo"></param>
		/// <param name="range"></param>
		/// <returns></returns>
		public static bool canSendTo(this IMyCubeBlock sendFrom, IMyEntity sendTo, bool friendsOnly, float range = 0, bool rangeIsSquared = false)
		{
			if (sendFrom.Closed || !sendFrom.IsWorking)
				return false;

			IMyCubeBlock sendToAsBlock = sendTo as IMyCubeBlock;
			if (sendToAsBlock != null)
			{
				if (sendToAsBlock.Closed || !sendToAsBlock.IsWorking)
					return false;

				if (friendsOnly && !sendFrom.canConsiderFriendly(sendToAsBlock))
					return false;

				if (AttachedGrids.isGridAttached(sendFrom.CubeGrid, sendToAsBlock.CubeGrid))
					return true;

				if (range > 0)
				{
					double distanceSquared = (sendFrom.GetPosition() - sendTo.GetPosition()).LengthSquared();
					if (rangeIsSquared)
						return distanceSquared < range;
					else
						return distanceSquared < range * range;
				}
			}
			else
			{
				IMyCubeGrid sendToAsGrid = sendTo as IMyCubeGrid;
				if (sendToAsGrid != null)
				{
					if (friendsOnly && !sendFrom.canConsiderFriendly(sendToAsGrid))
						return false;

					if (Rynchodon.AttachedGrids.isGridAttached(sendFrom.CubeGrid, sendToAsGrid))
						return true;
				}

				if (range > 0)
				{
					double distance = sendToAsGrid.WorldAABB.Distance(sendFrom.GetPosition());
					if (rangeIsSquared)
						return distance * distance < range;
					else
						return distance < range;
				}
			}

			return false;
		}


		public static string gridBlockName(this IMyCubeBlock block)
		{ return block.CubeGrid.DisplayName + "." + block.DisplayNameText; }

		public static IMySlimBlock getSlim(this IMyCubeBlock block)
		{ return block.CubeGrid.GetCubeBlock(block.Position); }

		public static MyObjectBuilder_CubeBlock getSlimObjectBuilder(this IMyCubeBlock block)
		{ return block.getSlim().GetObjectBuilder(); }

		public static string getInstructions(this IMyCubeBlock block)
		{
			string displayName = block.DisplayNameText;
			int start = displayName.IndexOf('[') + 1;
			int end = displayName.IndexOf(']');
			if (start > 0 && end > start) // has appropriate brackets
			{
				int length = end - start;
				return displayName.Substring(start, length);
			}
			return null;
		}

		/// <summary>
		/// Extracts the identifier portion of a blocks name.
		/// </summary>
		/// <param name="rc"></param>
		/// <returns>null iff name could not be extracted</returns>
		public static string getNameOnly(this IMyCubeBlock rc)
		{
			string displayName = rc.DisplayNameText;
			int start = displayName.IndexOf('>') + 1;
			int end = displayName.IndexOf('[');
			if (start > 0 && end > start)
			{
				int length = end - start;
				return displayName.Substring(start, length);
			}
			if (start > 0)
			{
				return displayName.Substring(start);
			}
			if (end > 0)
			{
				return displayName.Substring(0, end);
			}
			return null;
		}
	}
}
