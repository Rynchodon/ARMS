using System;
using System.Collections.Generic;
using System.Linq;
using Sandbox.Definitions;
using Sandbox.Game.Entities;
using Sandbox.Game.Entities.Cube;
using Sandbox.ModAPI;
using VRage.Game;
using VRage.Game.Entity;
using VRage.Game.ModAPI;
using VRageMath;
using Ingame = Sandbox.ModAPI.Ingame;
using SE_Ingame = SpaceEngineers.Game.ModAPI.Ingame;

namespace Rynchodon
{
	public static class IMyCubeBlockExtensions
	{

		public static string gridBlockName(this IMyCubeBlock block)
		{ return block.CubeGrid.DisplayName + "." + block.DisplayNameText; }

		public static IMySlimBlock getSlim(this IMyCubeBlock block)
		{ return block.CubeGrid.GetCubeBlock(block.Position); }

		public static MyObjectBuilder_CubeBlock getSlimObjectBuilder(this IMyCubeBlock block)
		{ return block.getSlim().GetObjectBuilder(); }

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
		/// Gets the closest face direction to worldDirection.
		/// </summary>
		public static Base6Directions.Direction ClosestFaceDirection(this IMyCubeBlock block, Vector3 worldDirection)
		{
			IEnumerable<Base6Directions.Direction> faceDirections = FaceDirections(block);

			worldDirection.Normalize();
			Base6Directions.Direction bestDirection = (Base6Directions.Direction)255;
			double bestDirectionAngle = double.MinValue;

			foreach (Base6Directions.Direction direction in faceDirections)
			{
				Vector3 directionVector = block.WorldMatrix.GetDirectionVector(direction);
				double cosAngle = directionVector.Dot(worldDirection);

				//Log.DebugLog(cosAngle < -1 || cosAngle > 1, "cosAngle out of bounds: " + cosAngle, "GetFaceDirection()", Logger.severity.ERROR); // sometimes values are slightly out of range
				Logger.DebugLog("cosAngle invalid", Logger.severity.ERROR, condition: double.IsNaN(cosAngle) || double.IsInfinity(cosAngle));

				if (cosAngle > bestDirectionAngle)
				{
					//Log.DebugLog("angle: " + angle + ", bestDirectionAngle: " + bestDirectionAngle + ", direction: " + direction, "GetFaceDirection()");
					bestDirection = direction;
					bestDirectionAngle = cosAngle;
				}
			}

			if (bestDirectionAngle == double.MinValue)
				throw new NullReferenceException("bestDirection");

			return bestDirection;
		}

		/// <summary>
		/// Enumerable for all the face directions for a block.
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
		public static IEnumerable<Base6Directions.Direction> FaceDirections(this IMyCubeBlock block)
		{
			if (block is SE_Ingame.IMySolarPanel || block is SE_Ingame.IMyOxygenFarm)
			{
				yield return Base6Directions.Direction.Forward;
				yield return Base6Directions.Direction.Backward;
			}
			else if (block is Ingame.IMyLaserAntenna)
			{
				// up is really bad for laser antenna, it can't pick an azimuth and spins constantly
				yield return Base6Directions.Direction.Forward;
				yield return Base6Directions.Direction.Right;
				yield return Base6Directions.Direction.Backward;
				yield return Base6Directions.Direction.Left;
			}
			else if (block is SE_Ingame.IMyLandingGear)
				yield return Base6Directions.Direction.Down;
			else if (block is SE_Ingame.IMyShipMergeBlock)
				yield return Base6Directions.Direction.Right;
			else
				yield return Base6Directions.Direction.Forward;
		}

		/// <summary>
		/// As FaceDirections(this IMyCubeBlock block) but starts with the closest direction to worldDirection.
		/// </summary>
		public static IEnumerable<Base6Directions.Direction> FaceDirections(this IMyCubeBlock block, Vector3 worldDirection)
		{
			Base6Directions.Direction closest = ClosestFaceDirection(block, worldDirection);
			yield return closest;
			foreach (Base6Directions.Direction direction in FaceDirections(block))
				if (direction != closest)
					yield return direction;
		}

		public static Base6Directions.Direction FirstFaceDirection(this IMyCubeBlock block)
		{
			return FaceDirections(block).First();
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
			return ((MyCubeBlock)block).BlockDefinition;
		}

		public static MyCubeBlockDefinition GetCubeBlockDefinition(this IMyCubeBlock block)
		{
			return ((MyCubeBlock)block).BlockDefinition;
		}

		public static bool IsOnSide(this IMyCubeBlock block, Vector3 side)
		{
			Vector3 blockPosition = block.LocalPosition();
			Vector3D centreOfMass = block.CubeGrid.Physics.CenterOfMassWorld;
			MatrixD invWorld = block.CubeGrid.PositionComp.WorldMatrixNormalizedInv;
			Vector3D.Transform(ref centreOfMass, ref invWorld, out centreOfMass);
			Vector3 localCentreOfMass = centreOfMass;

			float blockPosInDirect; Vector3.Dot(ref blockPosition, ref side, out blockPosInDirect);
			float centreInDirect; Vector3.Dot(ref localCentreOfMass, ref side, out centreInDirect);

			return blockPosInDirect > centreInDirect;
		}

		public static void EnableGameThread(this IMyCubeBlock block, bool enable)
		{
			MyAPIGateway.Utilities.InvokeOnGameThread(() => ((MyFunctionalBlock)block).Enabled = enable);
		}

		/// <summary>
		/// Creates an AABB that includes all subparts of the block.
		/// </summary>
		/// <param name="block">The block to create the AABB for.</param>
		/// <param name="gsAABB">An AABB that includes all subparts of the block, in grid space.</param>
		public static void CombinedAABB(this MyCubeBlock block, out BoundingBox gsAABB)
		{
			BoundingBox bsAABBSum = block.PositionComp.LocalAABB;

			Dictionary<string, MyEntitySubpart> subparts = block.Subparts;
			if (subparts != null && subparts.Count != 0)
				foreach (MyEntitySubpart part in subparts.Values)
					IncludeAABB(part, ref bsAABBSum);

			Matrix blockLocalMatrix = block.PositionComp.LocalMatrix;
			gsAABB = bsAABBSum.Transform(ref blockLocalMatrix);
		}

		/// <summary>
		/// Include AABB for entity and all subparts in psAABB.
		/// </summary>
		/// <param name="entity">Entity to include the AABB of.</param>
		/// <param name="psAABB">In entity's parent's space.</param>
		private static void IncludeAABB(MyEntity entity, ref BoundingBox psAABB)
		{
			BoundingBox esAABBSum = entity.PositionComp.LocalAABB;

			Dictionary<string, MyEntitySubpart> subparts = entity.Subparts;
			if (subparts != null && subparts.Count != 0)
				foreach (MyEntitySubpart part in subparts.Values)
					IncludeAABB(part, ref esAABBSum);

			Matrix entityLocalMatrix = entity.PositionComp.LocalMatrix;
			BoundingBox psEntityAABB = esAABBSum.Transform(ref entityLocalMatrix);
			psAABB.Include(ref psEntityAABB); // modifies psAABB and returns same
		}

	}
}
