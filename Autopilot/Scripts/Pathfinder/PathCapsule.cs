// skip file on build

#define LOG_ENABLED // remove on build

using Sandbox.ModAPI;
using System;
using VRageMath;

namespace Rynchodon.Autopilot.Pathfinder
{
	public class PathCapsule
	{
		//public readonly float BufferSize;
		//public readonly float BufferSquared;
		///// <summary>
		///// Radius + BufferSize
		///// </summary>
		//private readonly float BufferedRadius;
		///// <summary>
		///// BufferedRadius * BufferedRadius
		///// </summary>
		//private readonly float BufferedRadiusSquared;

		//private readonly RelativeVector3F P0;
		//private readonly RelativeVector3F P1;

		private readonly float gridSize;

		private readonly float Radius;
		private readonly float RadiusSquared;

		//private readonly Line line_local;
		private readonly Line line_world;

		private static Logger myLogger = new Logger(null, "Path");

		public PathCapsule(RelativeVector3F P0, RelativeVector3F P1, float RadiusSquared, float gridSize)
		{
			//BufferSize = 5;
			//BufferSquared = BufferSize * BufferSize;
			//float Radius = (float)(Math.Pow(RadiusSquared, 0.5));
			//BufferedRadius = Radius + BufferSize;
			//BufferedRadiusSquared = BufferedRadius * BufferedRadius;

			this.gridSize = gridSize;

			this.RadiusSquared = RadiusSquared;
			this.Radius = (float)(Math.Pow(RadiusSquared, 0.5));

			//this.P0 = P0;
			//this.P1 = P1;
			//this.line_local = new Line(P0.getLocal(), P1.getLocal(), false);
			this.line_world = new Line(P0.getWorldAbsolute(), P1.getWorldAbsolute());

			myLogger.debugLog("new path(world abs) from " + P0.getWorldAbsolute() + " to " + P1.getWorldAbsolute(), "Path()");
			myLogger.debugLog("new path(local) from " + P0.getLocal() + " to " + P1.getLocal(), "Path()");
		}

		public bool IntersectsAABB(IMyEntity entity)
		{
			BoundingBox AABB = (BoundingBox)entity.WorldAABB;
			AABB.Inflate(Radius);
			float distance;
			return (AABB.Intersects(line_world, out distance));
		}

		//public bool Intersects(IMySlimBlock block)
		//{
		//	float buffer = block.CubeGrid.GridSize;
		//	IMyCubeBlock FatBlock = block.FatBlock;
		//	if (FatBlock == null)
		//		return Intersects(block.CubeGrid.GridIntegerToWorld(block.Position), buffer);
		//	else
		//	{
		//		//bool cellIntersects = false;
		//		//FatBlock.LocalAABB.Min.ForEach(FatBlock.LocalAABB.Max, vector =>
		//		//{
		//		//	if (Intersects(vector))
		//		//	{
		//		//		cellIntersects = true;
		//		//		return true;
		//		//	}
		//		//	return false;
		//		//});
		//		//return cellIntersects;
		//	}
		//}

		/// <param name="buffer">the grid size of the grid to test, added to the size of the grid that owns the path</param>
		public bool Intersects(Vector3 worldPosition, float buffer)
		{
			buffer += gridSize;
			float bufferedRadius = Radius + buffer;
			float bufferedRadiusSquared = bufferedRadius * bufferedRadius;
			return line_world.DistanceSquared(worldPosition) <= bufferedRadiusSquared;
		}
	}
}
