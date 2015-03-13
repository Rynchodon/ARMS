using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

using Sandbox.Common;
using Sandbox.ModAPI;

namespace Rynchodon
{
	/// <summary>
	/// Target can be considered friendly when Relations = Owner or FactionShare (includes unowned blocks).
	/// Target can be considered hostile when Relations = Enemy or when target is an unowned grid.
	/// </summary>
	public static class IMyCubeBlockExtensions
	{
		/// <summary>
		/// True if FactionShare or Owner
		/// </summary>
		/// <param name="block"></param>
		/// <param name="playerID"></param>
		/// <returns></returns>
		public static bool canConsiderFriendly(this IMyCubeBlock block, long playerID)
		{
			switch (block.GetUserRelationToOwner(playerID))
			{
				case MyRelationsBetweenPlayerAndBlock.FactionShare:
				case MyRelationsBetweenPlayerAndBlock.Owner:
					return true;
				default:
					return false;
			}
		}

		/// <summary>
		/// If owned, canConsiderFriendly(this IMyCubeBlock block, long playerID). If unowned, canConsiderFriendly(this IMyCubeBlock block, IMyCubeGrid target)
		/// </summary>
		/// <param name="block"></param>
		/// <param name="target"></param>
		/// <returns></returns>
		public static bool canConsiderFriendly(this IMyCubeBlock block, IMyCubeBlock target)
		{
			if (target.OwnerId == 0) // every ship will have neutral blocks, treat them as belonging to grid's owner
				return block.canConsiderFriendly(target.CubeGrid);
			return block.canConsiderFriendly(target.OwnerId);
		}

		/// <summary>
		/// If owned, true if any big or small owner is FactionShare or Owner. If unowned, false.
		/// </summary>
		/// <param name="block"></param>
		/// <param name="target"></param>
		/// <returns></returns>
		public static bool canConsiderFriendly(this IMyCubeBlock block, IMyCubeGrid target)
		{
			if (target.BigOwners.Count == 0)
				return false;
			foreach (long bigOwner in target.BigOwners)
				if (block.canConsiderFriendly(bigOwner))
					return true;
			foreach (long smallOwner in target.SmallOwners)
				if (block.canConsiderFriendly(smallOwner))
					return true;
			return false;
		}

		/// <summary>
		/// True if Enemies
		/// </summary>
		/// <param name="block"></param>
		/// <param name="playerID"></param>
		/// <returns></returns>
		public static bool canConsiderHostile(this IMyCubeBlock block,long playerID)
		{
			return (block.GetUserRelationToOwner(playerID) == MyRelationsBetweenPlayerAndBlock.Enemies);
		}

		/// <summary>
		/// If owned, canConsiderHostile(this IMyCubeBlock block,long playerID). If unowned, canConsiderHostile(this IMyCubeBlock block, IMyCubeGrid target).
		/// </summary>
		/// <param name="target"></param>
		/// <returns></returns>
		public static bool canConsiderHostile(this IMyCubeBlock block, IMyCubeBlock target)
		{
			if (target.OwnerId == 0) // every ship will have neutral blocks, treat them as belonging to grid's owner
				return block.canConsiderHostile(target.CubeGrid);
			return block.canConsiderHostile(target.OwnerId);
		}

		/// <summary>
		/// If owned, true if any big or small owner is an Enemy. If unowned, true.
		/// </summary>
		/// <param name="block"></param>
		/// <param name="target"></param>
		/// <returns></returns>
		public static bool canConsiderHostile(this IMyCubeBlock block, IMyCubeGrid target)
		{
			if (target.BigOwners.Count == 0)
				return true;
			foreach (long bigOwner in target.BigOwners)
				if (block.canConsiderHostile(bigOwner))
					return true;
			foreach (long smallOwner in target.SmallOwners)
				if (block.canConsiderHostile(smallOwner))
					return true;
			return false;
		}

		/// <summary>
		/// tests for sendFrom and sendTo are working and same grid or in radius
		/// </summary>
		/// <param name="sendTo"></param>
		/// <param name="rangeSquared"></param>
		/// <returns></returns>
		public static bool canSendTo(this IMyCubeBlock sendFrom, IMyCubeBlock sendTo, float rangeSquared)
		{
			if (!sendFrom.IsWorking || !sendTo.IsWorking)
				return false;

			if (sendFrom.CubeGrid == sendTo.CubeGrid)
				return true;

			if (rangeSquared > 0)
			{
				double distanceSquared = (sendFrom.GetPosition() - sendTo.GetPosition()).LengthSquared();
				if (distanceSquared < rangeSquared)
					return true;
			}

			return Rynchodon.AttachedGrids.isGridAttached(sendFrom.CubeGrid, sendTo.CubeGrid);
		}
	}
}
