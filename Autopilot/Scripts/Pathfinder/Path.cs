using System;
//using System.Collections.Generic;
//using System.Linq;
//using System.Text;

using VRageMath;

namespace Rynchodon.Autopilot.Pathfinder
{
	public class Path
	{
		public readonly RelativeVector3F P0;
		public readonly RelativeVector3F P1;
		public readonly float BufferedRadiusSquared;
		public float BufferedRadius;// { get { return lazy_BufferedRadius.Value; } }
		//private readonly Lazy<float> lazy_BufferedRadius;
		public readonly Line line_local;
		public readonly LineD line_world;

		public Path(RelativeVector3F P0, RelativeVector3F P1, float BufferedRadiusSquared)
		{
			this.P0 = P0;
			this.P1 = P1;
			this.BufferedRadiusSquared = BufferedRadiusSquared;
			//this.lazy_BufferedRadius = new Lazy<float>(() => (float)Math.Pow(this.BufferedRadiusSquared, 0.5));
			BufferedRadius = (float)Math.Pow(this.BufferedRadiusSquared, 0.5);
			this.line_local = new Line(P0.getGrid(), P1.getGrid(), false);
			this.line_world = new LineD(P0.getWorldAbsolute(), P1.getWorldAbsolute());
		}
	}
}
