﻿#define LOG_ENABLED //remove on build

using System;
using System.Collections.Generic;
using Rynchodon.Attached;
using Sandbox.Common.ObjectBuilders;
using Sandbox.ModAPI;
using VRage.ModAPI;
using VRageMath;
using Ingame = Sandbox.ModAPI.Ingame;

namespace Rynchodon
{
	public static class IMyCubeBlockExtensions
	{
		private static Logger myLogger = new Logger("IMyCubeBlockExtensions");

		/// <summary>
		/// Tests for sendFrom is working and grid attached or in range. Use without range to skip range test. If sendTo is not a block or a grid, will skip grid test. If sendTo is a block, it must be working.
		/// </summary>
		/// <remarks>
		/// If sendFrom == sendTo, returns false
		/// </remarks>
		public static bool canSendTo(this IMyCubeBlock sendFrom, object sendTo, bool friendsOnly, float range = 0, bool rangeIsSquared = false)
		{
			sendFrom.throwIfNull_argument("sendFrom");
			sendTo.throwIfNull_argument("sendTo");

			if (sendFrom.Closed || !sendFrom.IsWorking)
				return false;

			IMyCubeBlock sendToAsBlock = sendTo as IMyCubeBlock;
			if (sendToAsBlock != null)
			{
				if (sendFrom == sendToAsBlock)
					return false;

				if (sendToAsBlock.Closed || !sendToAsBlock.IsWorking)
					return false;

				if (friendsOnly && !sendFrom.canConsiderFriendly(sendToAsBlock))
					return false;

				if (AttachedGrid.IsGridAttached(sendFrom.CubeGrid, sendToAsBlock.CubeGrid, AttachedGrid.AttachmentKind.Terminal))
					return true;

				if (range > 0)
				{
					double distanceSquared = (sendFrom.GetPosition() - sendToAsBlock.GetPosition()).LengthSquared();
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

					if (AttachedGrid.IsGridAttached(sendFrom.CubeGrid, sendToAsGrid, AttachedGrid.AttachmentKind.Terminal))
						return true;
				}
			}

			if (range > 0)
			{
				IMyEntity asEntity = sendTo as IMyEntity;
				if (asEntity != null)
				{
					double distance = asEntity.WorldAABB.Distance(sendFrom.GetPosition());

					if (rangeIsSquared)
						return distance * distance < range;
					else
						return distance < range;
				}
				else
				{
					IMyPlayer asPlayer = sendTo as IMyPlayer;
					if (asPlayer != null)
					{
						double distanceSq = Vector3D.DistanceSquared(sendFrom.GetPosition(), asPlayer.GetPosition());

						if (rangeIsSquared)
							return distanceSq < range;
						else
							return distanceSq < range * range;
					}
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

		[Obsolete("Use BlockInstruction")]
		public static string getInstructions(this IMyCubeBlock block)
		{ return block.DisplayNameText.getInstructions(); }

		/// <summary>
		/// Extracts the identifier portion of a blocks name.
		/// </summary>
		/// <returns>null iff name could not be extracted</returns>
		public static string getNameOnly(this IMyCubeBlock rc)
		{
			string displayName = rc.DisplayNameText;
			//int start = displayName.IndexOf('>') + 1;
			int end = displayName.IndexOf('[');
			//if (start > 0 && end > start)
			//{
			//	int length = end - start;
			//	return displayName.Substring(start, length);
			//}
			//if (start > 0)
			//{
			//	return displayName.Substring(start);
			//}
			if (end > 0)
			{
				return displayName.Substring(0, end);
			}
			return displayName;
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
				//result.Add(Base6Directions.Direction.Up); // up is really bad for laser antenna, it can't pick an azimuth and spins constantly
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
			if (faceDirections.Count == 0)
				throw new InvalidOperationException("faceDirections.Count == 0");

			worldDirection.Normalize();
			Base6Directions.Direction? bestDirection = null;
			double bestDirectionAngle = double.MinValue;

			foreach (Base6Directions.Direction direction in faceDirections)
			{
				Vector3 directionVector = block.WorldMatrix.GetDirectionVector(direction);
				double cosAngle = directionVector.Dot(worldDirection);

				myLogger.debugLog(cosAngle < -1 || cosAngle > 1, "cosAngle out of bounds", "GetFaceDirection()", Logger.severity.ERROR);
				myLogger.debugLog(double.IsNaN(cosAngle) || double.IsInfinity(cosAngle), "cosAngle invalid", "GetFaceDirection()", Logger.severity.ERROR);

				if (cosAngle > bestDirectionAngle)
				{
					//myLogger.debugLog("angle: " + angle + ", bestDirectionAngle: " + bestDirectionAngle + ", direction: " + direction, "GetFaceDirection()");
					bestDirection = direction;
					bestDirectionAngle = cosAngle;
				}
			}

			if (bestDirection == null)
				throw new NullReferenceException("bestDirection");

			return bestDirection.Value;
		}

		public static float GetLengthInDirection(this IMyCubeBlock block, Base6Directions.Direction direction)
		{
			switch (direction)
			{
				case Base6Directions.Direction.Right:
				case Base6Directions.Direction.Left:
					return block.LocalAABB.Size.X;
				case Base6Directions.Direction.Up:
				case Base6Directions.Direction.Down:
					return block.LocalAABB.Size.Y;
				case Base6Directions.Direction.Backward:
				case Base6Directions.Direction.Forward:
					return block.LocalAABB.Size.Z;
			}
			VRage.Exceptions.ThrowIf<NotImplementedException>(true, "direction not implemented: " + direction);
			throw new Exception();
		}

		public static void ApplyAction(this Ingame.IMyCubeBlock block, string actionName)
		{
			IMyTerminalBlock asTerm = block as IMyTerminalBlock;
			asTerm.GetActionWithName(actionName).Apply(asTerm);
		}

	}
}
