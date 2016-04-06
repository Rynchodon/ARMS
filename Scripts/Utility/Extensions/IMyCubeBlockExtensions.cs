using System;
using System.Collections.Generic;
using Sandbox.Definitions;
using Sandbox.Game.Entities;
using Sandbox.ModAPI;
using VRage.Game;
using VRage.Game.ModAPI;
using VRageMath;
using Ingame = Sandbox.ModAPI.Ingame;
using SE_Ingame = SpaceEngineers.Game.ModAPI.Ingame;

namespace Rynchodon
{
	public static class IMyCubeBlockExtensions
	{
		private static Logger myLogger = new Logger("IMyCubeBlockExtensions");

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
		public static string getNameOnly(this IMyCubeBlock block)
		{
			string displayName = block.DisplayNameText;
			if (string.IsNullOrWhiteSpace(displayName))
				return string.Empty;

			int end = displayName.IndexOf('[');
			if (end > 0)
				return displayName.Substring(0, end);
			return displayName;
		}

		public static Vector3 LocalPosition(this IMyCubeBlock block)
		{
			return (block.Min + block.Max) * 0.5f * block.CubeGrid.GridSize;
		}

		/// <summary>
		/// Determines if a block is owned by "hostile NPC"
		/// </summary>
		public static bool OwnedNPC(this IMyCubeBlock block)
		{
			if (block.OwnerId == 0)
				return false;

			return MyAPIGateway.Players.GetFirstPlayer_Safe(player => player.PlayerID == block.OwnerId) == null;
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
			if (block is SE_Ingame.IMySolarPanel || block is SE_Ingame.IMyOxygenFarm)
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
			else if (block is SE_Ingame.IMyLandingGear)
				result.Add(Base6Directions.Direction.Down);
			else if (block is SE_Ingame.IMyShipMergeBlock)
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

				//myLogger.debugLog(cosAngle < -1 || cosAngle > 1, "cosAngle out of bounds: " + cosAngle, "GetFaceDirection()", Logger.severity.ERROR); // sometimes values are slightly out of range
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

		public static void ApplyAction(this VRage.Game.ModAPI.Ingame.IMyCubeBlock block, string actionName)
		{
			IMyTerminalBlock asTerm = block as IMyTerminalBlock;
			asTerm.GetActionWithName(actionName).Apply(asTerm);
		}

		public static MyCubeBlockDefinition GetCubeBlockDefinition(this VRage.Game.ModAPI.Ingame.IMyCubeBlock block)
		{
			MyCubeBlock cubeBlock = block as MyCubeBlock;
			return cubeBlock.BlockDefinition;
		}

		public static MyCubeBlockDefinition GetCubeBlockDefinition(this IMyCubeBlock block)
		{
			MyCubeBlock cubeBlock = block as MyCubeBlock;
			return cubeBlock.BlockDefinition;
		}

	}
}
