using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

using Sandbox.Common;
using Sandbox.ModAPI;

namespace Rynchodon
{
	/// <summary>
	/// Hostile:
	/// The goal is to be consistent with what a turret will shoot at, the exception is that unowned blocks will result in testing grid's ownership.
	/// i.e. A turret will shoot at an enemy block and will shoot at a grid if it contains any enemy blocks.
	/// 
	/// Friendly:
	/// Essentially, any block or grid which is neither enemies nor neutral.
	/// </summary>
	public static class IMyCubeBlockExtensions
	{
		public static MyRelationsBetweenPlayerAndBlock getRelationsTo(this IMyCubeBlock block, IMyCubeBlock target)
		{
			if (target.OwnerId == 0)
				return block.getRelationsTo(target.CubeGrid);
			return block.GetUserRelationToOwner(target.OwnerId);
		}

		public static MyRelationsBetweenPlayerAndBlock getRelationsTo(this IMyCubeBlock block, IMyCubeGrid target)
		{
			if (target.BigOwners.Count == 0 && target.SmallOwners.Count == 0)
				return MyRelationsBetweenPlayerAndBlock.Enemies;

			MyRelationsBetweenPlayerAndBlock worstRelations = MyRelationsBetweenPlayerAndBlock.Owner;
			foreach (long gridOwner in target.BigOwners)
			{
				MyRelationsBetweenPlayerAndBlock relations = block.GetUserRelationToOwner(gridOwner);
				if (relations == MyRelationsBetweenPlayerAndBlock.Enemies)
					return MyRelationsBetweenPlayerAndBlock.Enemies;
				setIfWorse(relations, ref worstRelations);
			}

			foreach (long gridOwner in target.SmallOwners)
			{
				MyRelationsBetweenPlayerAndBlock relations = block.GetUserRelationToOwner(gridOwner);
				if (relations == MyRelationsBetweenPlayerAndBlock.Enemies)
					return MyRelationsBetweenPlayerAndBlock.Enemies;
				setIfWorse(relations, ref worstRelations);
			}

			return worstRelations;
 		}


		public static bool canConsiderFriendly(this IMyCubeBlock block, long playerID)
		{ return toFriendly(block.GetUserRelationToOwner(playerID)); }

		public static bool canConsiderFriendly(this IMyCubeBlock block, IMyCubeBlock target)
		{ return toFriendly(block.getRelationsTo(target)); }

		public static bool canConsiderFriendly(this IMyCubeBlock block, IMyCubeGrid target)
		{ return toFriendly(block.getRelationsTo(target)); }


		public static bool canConsiderHostile(this IMyCubeBlock block, long playerID)
		{ return toHostile(block.GetUserRelationToOwner(playerID)); }

		public static bool canConsiderHostile(this IMyCubeBlock block, IMyCubeBlock target)
		{ return toHostile(block.getRelationsTo(target)); }

		public static bool canConsiderHostile(this IMyCubeBlock block, IMyCubeGrid target)
		{ return toHostile(block.getRelationsTo(target)); }


		private static void setIfWorse(MyRelationsBetweenPlayerAndBlock value, ref MyRelationsBetweenPlayerAndBlock variable)
		{
			switch (value)
			{
				case MyRelationsBetweenPlayerAndBlock.Enemies:
					variable = MyRelationsBetweenPlayerAndBlock.Enemies;
					return;
				case MyRelationsBetweenPlayerAndBlock.Neutral:
					if (variable != MyRelationsBetweenPlayerAndBlock.Enemies)
						variable = MyRelationsBetweenPlayerAndBlock.Neutral;
					return;
				case MyRelationsBetweenPlayerAndBlock.FactionShare:
					if (variable == MyRelationsBetweenPlayerAndBlock.Owner)
						variable = MyRelationsBetweenPlayerAndBlock.FactionShare;
					return;

				// owner is never worse than anything
			}
		}

		private static bool toFriendly(MyRelationsBetweenPlayerAndBlock relations)
		{
			switch (relations)
			{
				case MyRelationsBetweenPlayerAndBlock.FactionShare:
				case MyRelationsBetweenPlayerAndBlock.Owner:
					return true;
				default:
					return false;
			}
		}

		private static bool toHostile(MyRelationsBetweenPlayerAndBlock relations)
		{ return (relations == MyRelationsBetweenPlayerAndBlock.Enemies); }

		///// <summary>
		///// True if FactionShare or Owner
		///// </summary>
		///// <param name="block"></param>
		///// <param name="playerID"></param>
		///// <returns></returns>
		//public static bool canConsiderFriendly(this IMyCubeBlock block, long playerID)
		//{
		//	switch (block.GetUserRelationToOwner(playerID))
		//	{
		//		case MyRelationsBetweenPlayerAndBlock.FactionShare:
		//		case MyRelationsBetweenPlayerAndBlock.Owner:
		//			return true;
		//		default:
		//			return false;
		//	}
		//}

		///// <summary>
		///// If owned, canConsiderFriendly(this IMyCubeBlock block, long playerID). If unowned, canConsiderFriendly(this IMyCubeBlock block, IMyCubeGrid target)
		///// </summary>
		///// <param name="block"></param>
		///// <param name="target"></param>
		///// <returns></returns>
		//public static bool canConsiderFriendly(this IMyCubeBlock block, IMyCubeBlock target)
		//{
		//	if (target.OwnerId == 0) // every ship will have neutral blocks, treat them as belonging to grid's owner
		//		return block.canConsiderFriendly(target.CubeGrid);
		//	return block.canConsiderFriendly(target.OwnerId);
		//}

		//// TODO: make consistent with blurb
		///// <summary>
		///// If owned, true if any big or small owner is FactionShare or Owner. If unowned, false.
		///// </summary>
		///// <param name="block"></param>
		///// <param name="target"></param>
		///// <returns></returns>
		//public static bool canConsiderFriendly(this IMyCubeBlock block, IMyCubeGrid target)
		//{
		//	if (target.BigOwners.Count == 0)
		//		return false;
		//	foreach (long bigOwner in target.BigOwners)
		//		if (block.canConsiderHostile(bigOwner))
		//			return false;
		//	foreach (long smallOwner in target.SmallOwners)
		//		if (block.canConsiderHostile(smallOwner))
		//			return false;
		//	return false;
		//}

		///// <summary>
		///// True if Enemies
		///// </summary>
		///// <param name="block"></param>
		///// <param name="playerID"></param>
		///// <returns></returns>
		//public static bool canConsiderHostile(this IMyCubeBlock block, long playerID)
		//{ return (block.GetUserRelationToOwner(playerID) == MyRelationsBetweenPlayerAndBlock.Enemies); }

		///// <summary>
		///// If owned, canConsiderHostile(this IMyCubeBlock block,long playerID). If unowned, canConsiderHostile(this IMyCubeBlock block, IMyCubeGrid target).
		///// </summary>
		///// <param name="target"></param>
		///// <returns></returns>
		//public static bool canConsiderHostile(this IMyCubeBlock block, IMyCubeBlock target)
		//{
		//	if (target.OwnerId == 0) // every ship will have neutral blocks, treat them as belonging to grid's owner
		//		return block.canConsiderHostile(target.CubeGrid);
		//	return block.canConsiderHostile(target.OwnerId);
		//}

		///// <summary>
		///// If owned, true if any big or small owner is an Enemy. If unowned, true.
		///// </summary>
		///// <param name="block"></param>
		///// <param name="target"></param>
		///// <returns></returns>
		//public static bool canConsiderHostile(this IMyCubeBlock block, IMyCubeGrid target)
		//{
		//	if (target.BigOwners.Count == 0)
		//		return true;
		//	foreach (long bigOwner in target.BigOwners)
		//		if (block.canConsiderHostile(bigOwner))
		//			return true;
		//	foreach (long smallOwner in target.SmallOwners)
		//		if (block.canConsiderHostile(smallOwner))
		//			return true;
		//	return false;
		//}

		///// <summary>
		///// Tests for sendFrom and sendTo are working and same grid or in range. Use without range to skip range test.
		///// </summary>
		///// <param name="sendTo"></param>
		///// <param name="rangeSquared"></param>
		///// <returns></returns>
		//public static bool canSendTo(this IMyCubeBlock sendFrom, IMyCubeBlock sendTo, float range = 0)
		//{
		//	if (!sendFrom.IsWorking || !sendTo.IsWorking)
		//		return false;

		//	if (Rynchodon.AttachedGrids.isGridAttached(sendFrom.CubeGrid, sendTo.CubeGrid))
		//		return true;

		//	if (range > 0)
		//	{
		//		double distanceSquared = (sendFrom.GetPosition() - sendTo.GetPosition()).LengthSquared();
		//		return distanceSquared < range * range;
		//	}
		//	return false;
		//}

		/// <summary>
		/// Tests for sendFrom is working and same grid or in range. Use without range to skip range test. If sendTo is not a block or a grid, will skip grid test. If sendTo is a block, it must be working.
		/// </summary>
		/// <param name="sendFrom"></param>
		/// <param name="sendTo"></param>
		/// <param name="range"></param>
		/// <returns></returns>
		public static bool canSendTo(this IMyCubeBlock sendFrom, IMyEntity sendTo, float range = 0, bool rangeIsSquared = false)
		{
			if (!sendFrom.IsWorking)
				return false;

			IMyCubeBlock sendToAsBlock = sendTo as IMyCubeBlock;
			if (sendToAsBlock != null)
			{
				if (!sendToAsBlock.IsWorking)
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
					if (Rynchodon.AttachedGrids.isGridAttached(sendFrom.CubeGrid, sendToAsGrid))
						return true;

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
	}
}
