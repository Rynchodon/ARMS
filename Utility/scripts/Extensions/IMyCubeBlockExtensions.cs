#define LOG_ENABLED //remove on build

using System;
using System.Collections.Generic;
using Sandbox.Common;
using Sandbox.Common.ObjectBuilders;
using Sandbox.ModAPI;
using VRage.ModAPI;
using VRageMath;
using Ingame = Sandbox.ModAPI.Ingame;

namespace Rynchodon
{
	public static class IMyCubeBlockExtensions
	{
		#region Relations

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
			VRage.Exceptions.ThrowIf<ArgumentNullException>(block == null, "block");

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
			VRage.Exceptions.ThrowIf<ArgumentNullException>(block == null, "block");
			VRage.Exceptions.ThrowIf<ArgumentNullException>(target == null, "target");

			if (block.OwnerId == 0 || target.OwnerId == 0)
				return Relations.None;
			return block.getRelationsTo(target.OwnerId) ;
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

		#endregion

		/// <summary>
		/// Tests for sendFrom is working and grid attached or in range. Use without range to skip range test. If sendTo is not a block or a grid, will skip grid test. If sendTo is a block, it must be working.
		/// </summary>
		/// <param name="sendFrom"></param>
		/// <param name="sendTo"></param>
		/// <param name="range"></param>
		/// <returns></returns>
		public static bool canSendTo(this IMyCubeBlock sendFrom, IMyEntity sendTo, bool friendsOnly, float range = 0, bool rangeIsSquared = false)
		{
			sendFrom.throwIfNull_argument("sendFrom");
			sendTo.throwIfNull_argument("sendTo");

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

				// may or may not be grid
				if (range > 0)
				{
					double distance = sendTo.WorldAABB.Distance(sendFrom.GetPosition());
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
		{ return block.DisplayNameText.getInstructions(); }

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

		public static Vector3 LocalPosition(this IMyCubeBlock block)
		{ return block.Position * block.CubeGrid.GridSize; }

		/// <summary>
		/// Determines if a block is owned by "hostile NPC"
		/// </summary>
		public static bool OwnedNPC(this IMyCubeBlock block)
		{
			long Owner = block.OwnerId;
			if (Owner == 0)
				return false;
			List<IMyPlayer> match = MyAPIGateway.Players.GetPlayers_Safe((player) => { return player.PlayerID == Owner; });
			return match.Count == 0;
		}

		/// <summary>
		/// Get all the face directions for a block.
		/// </summary>
		/// <remarks>
		/// <para>Ship Controllers: forward</para>
		/// <para>Weapons: forward</para>
		/// <para>Connector: forward</para>
		/// <para>Solar Panel: forward, backward</para>
		/// <para>Solar Farm: forward, backward</para>
		/// <para>Directional Antenna: upward, forward, rightward, backward, leftward</para>
		/// <para>Landing Gear: downward</para>
		/// <para>Merge Block: rightward</para>
		/// </remarks>
		public static List<Base6Directions.Direction> GetFaceDirection(this IMyCubeBlock block)
		{
			List<Base6Directions.Direction> result = new List<Base6Directions.Direction>();
			if (block is Ingame.IMySolarPanel || block is Ingame.IMyOxygenFarm)
			{
				result.Add(Base6Directions.Direction.Forward);
				result.Add(Base6Directions.Direction.Backward);
			}
			else if (block is Ingame.IMyLaserAntenna)
			{
				result.Add(Base6Directions.Direction.Up);
				result.Add(Base6Directions.Direction.Forward);
				result.Add(Base6Directions.Direction.Right);
				result.Add(Base6Directions.Direction.Backward);
				result.Add(Base6Directions.Direction.Left);
			}
			else if (block is Ingame.IMyLandingGear)
				result.Add(Base6Directions.Direction.Down);
			else if (block is Ingame.IMyShipMergeBlock)
				result.Add(Base6Directions.Direction.Right);
			else
				result.Add(Base6Directions.Direction.Forward);

			return result;
		}

		/// <summary>
		/// Gets the closest face direction to worldDirection.
		/// </summary>
		public static Base6Directions.Direction GetFaceDirection(this IMyCubeBlock block, Vector3 worldDirection)
		{
			List<Base6Directions.Direction> faceDirections = GetFaceDirection(block);
			if (faceDirections.Count == 1)
				return faceDirections[0];

			//worldDirection = Vector3.Normalize(worldDirection); // maybe?
			Base6Directions.Direction? bestDirection = null;
			float bestDirectionCloseness = float.MaxValue;

			foreach (Base6Directions.Direction direction in faceDirections)
			{
				Vector3 directionVector = block.WorldMatrix.GetDirectionVector(direction);
				float closeness = directionVector.Dot(worldDirection);

				if (closeness < bestDirectionCloseness)
				{
					bestDirection = direction;
					bestDirectionCloseness = closeness;
				}
			}

			if (bestDirection == null)
				throw new NullReferenceException("bestDirection");

			return bestDirection.Value;
		}
	}
}
