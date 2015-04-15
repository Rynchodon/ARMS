using System;
//using System.Collections.Generic;
//using System.Linq;
//using System.Text;

using VRageMath;

namespace Rynchodon.Autopilot.Pathfinder
{
	public class Path
	{
		public readonly Vector3 P0;
		public readonly Vector3 P1;
		public readonly float BufferedRadiusSquared;
		public float BufferedRadius { get { return lazy_BufferedRadius.Value; } }
		private readonly Lazy<float> lazy_BufferedRadius;
		public readonly Line line;

		public Path(Vector3 P0, Vector3 P1, float BufferedRadiusSquared)
		{
			this.P0 = P0;
			this.P1 = P1;
			this.BufferedRadiusSquared = BufferedRadiusSquared;
			this.lazy_BufferedRadius = new Lazy<float>(() => (float)Math.Pow(this.BufferedRadiusSquared, 0.5));
			this.line = new Line(P0, P1, false);
		}
	}
}
