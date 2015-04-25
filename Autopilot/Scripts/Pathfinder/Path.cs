using System;
using VRageMath;

namespace Rynchodon.Autopilot.Pathfinder
{
	// TODO: separate radius for testing large and small grids.
	public class Path
	{
		public readonly float BufferSize;
		public readonly float BufferSquared;
		/// <summary>
		/// Radius + BufferSize
		/// </summary>
		public readonly float BufferedRadius;
		/// <summary>
		/// BufferedRadius * BufferedRadius
		/// </summary>
		public readonly float BufferedRadiusSquared;

		public readonly RelativeVector3F P0;
		public readonly RelativeVector3F P1;
		public readonly Line line_local;
		public readonly LineD line_world;

		public Path(RelativeVector3F P0, RelativeVector3F P1, float RadiusSquared, float gridSize)
		{
			BufferSize = 5;
			BufferSquared = BufferSize * BufferSize;
			float Radius = (float)(Math.Pow(RadiusSquared, 0.5));
			BufferedRadius = Radius + BufferSize;
			BufferedRadiusSquared = BufferedRadius * BufferedRadius;

			this.P0 = P0;
			this.P1 = P1;
			this.line_local = new Line(P0.getLocal(), P1.getLocal(), false);
			this.line_world = new LineD(P0.getWorldAbsolute(), P1.getWorldAbsolute());
		}
	}
}
