using System;
using System.Collections.Generic;
using Sandbox.Game.Entities;
using Sandbox.Game.Entities.Cube;
using VRage.Game.Components;
using VRage.Game.Entity;
using VRage.Game.Models;
using VRageMath;

namespace Rynchodon
{
	static class CubeLineIntersection
	{

		private const IntersectionFlags DefaultFlags = IntersectionFlags.ALL_TRIANGLES;

		/// <summary>
		/// Tests a line for intersection with models of blocks on the grid.
		/// </summary>
		public static bool Intersects(this MyCubeGrid grid, ref LineD localLine, List<Vector3I> rayCastPositions, out MyIntersectionResultLineTriangleEx? result, IntersectionFlags flags = DefaultFlags)
		{
			rayCastPositions.Clear();
			grid.RayCastCellsLocal(ref localLine.From, ref localLine.To, rayCastPositions);
			for (int i = 0; i < rayCastPositions.Count; i++)
			{
				MySlimBlock slim = grid.GetCubeBlock(rayCastPositions[i]);
				if (slim != null && Intersects(slim, ref localLine, out result, flags))
					return true;
			}

			result = null;
			return false;
		}

		/// <summary>
		/// Tests a line for intersection with models of blocks on the grid.
		/// </summary>
		public static bool Intersects(this MyCubeBlock block, ref LineD localLine, out MyIntersectionResultLineTriangleEx? result, IntersectionFlags flags = DefaultFlags)
		{
			Logger.TraceLog("Checking cube block: " + block.getBestName() + ", line from: " + localLine.From + ", to: " + localLine.To);

			MyCompoundCubeBlock compound = block as MyCompoundCubeBlock;
			if (compound != null)
			{
				foreach (MySlimBlock subBlock in compound.GetBlocks())
					if (Intersects(subBlock, ref localLine, out result))
						return true;
				result = null;
				return false;
			}

			return Intersects((MyEntity)block, ref localLine, out result, flags);
		}

		/// <summary>
		/// Tests a line for intersection with models of blocks on the grid.
		/// </summary>
		public static bool Intersects(this MySlimBlock slim, ref LineD localLine, out MyIntersectionResultLineTriangleEx? result, IntersectionFlags flags = DefaultFlags)
		{
			Logger.TraceLog("Checking slim block: " + slim.getBestName() + ", line from: " + localLine.From + ", to: " + localLine.To);

			if (slim.FatBlock != null)
				return Intersects(slim.FatBlock, ref localLine, out result);

			MyCube cube;
			if (!slim.CubeGrid.TryGetCube(slim.Position, out cube))
				throw new Exception("Failed to get MyCube for " + slim.nameWithId());

			foreach (MyCubePart part in cube.Parts)
			{
				Matrix localMatrix = part.InstanceData.LocalMatrix;
				Matrix invLocal; Matrix.Invert(ref localMatrix, out invLocal);
				MatrixD invLocalD = invLocal;

				Logger.TraceLog("Checking part: " + part.Model.AssetName + ", line from " + localLine.From + " to " + localLine.To + " becomes " +
					Vector3D.Transform(localLine.From, ref invLocalD) + " to " + Vector3D.Transform(localLine.To, ref invLocalD));

				result = part.Model.GetTrianglePruningStructure().GetIntersectionWithLine(slim.CubeGrid, ref localLine, ref invLocalD, flags);
				if (result.HasValue)
				{
					MyIntersectionResultLineTriangleEx value = result.Value;
					Vector3.Transform(ref value.IntersectionPointInObjectSpace, ref localMatrix, out value.IntersectionPointInObjectSpace);
					Matrix orientation = localMatrix.GetOrientation();
					Vector3.Transform(ref value.NormalInObjectSpace, ref orientation, out value.NormalInObjectSpace);

					result = value;
					return true;
				}
			}

			result = null;
			return false;
		}

		private static bool Intersects(MyEntity entity, ref LineD localLine, out MyIntersectionResultLineTriangleEx? result, IntersectionFlags flags = DefaultFlags)
		{
			Logger.TraceLog("Checking entity: " + entity.getBestName() + ", line from: " + localLine.From + ", to: " + localLine.To);

			Logger.DebugLog("entity is a MyCubeGrid, please call the appropriate method", Logger.severity.FATAL, condition: entity is MyCubeGrid);

			Matrix localMatrix = entity.PositionComp.LocalMatrix;
			Matrix invLocal; Matrix.Invert(ref localMatrix, out invLocal);
			MatrixD invLocalD = invLocal;

			result = entity.ModelCollision.GetTrianglePruningStructure().GetIntersectionWithLine(entity, ref localLine, ref invLocalD, flags);
			if (result.HasValue)
				return true;

			if (entity.Subparts != null)
				foreach (MyEntitySubpart part in entity.Subparts.Values)
					if (Intersects(part, ref localLine, out result, flags))
						return true;

			return false;
		}

	}
}
