using System;
using VRageMath;

namespace Rynchodon
{
	public class LineSegment
	{

		public static float GetShortestDistanceSquared(LineSegment line1, LineSegment line2)
		{
			return Line.GetShortestDistanceSquared(line1.m_line, line2.m_line);
		}

		public static Vector3 GetShortestVector(LineSegment line1, LineSegment line2, out Vector3 res1, out Vector3 res2)
		{
			return Line.GetShortestVector(ref line1.m_line, ref line2.m_line, out res1, out res2);
		}

		private Line m_line;
		private bool m_calculatedLength;
		private bool m_calculatedBox;

		public LineSegment() { }

		public LineSegment(Vector3 from, Vector3 to)
		{
			m_line = new Line() { From = from, To = to };
		}

		public LineSegment Clone()
		{
			return new LineSegment()
			{
				m_line = this.m_line,
				m_calculatedLength = this.m_calculatedLength,
				m_calculatedBox = this.m_calculatedBox
			};
		}

		public Vector3 From
		{
			get { return m_line.From; }
			set
			{
				if (value == m_line.From)
					return;
				m_line.From = value;
				m_calculatedLength = false;
				m_calculatedBox = false;
			}
		}

		public Vector3 To
		{
			get { return m_line.To; }
			set
			{
				if (value == m_line.To)
					return;
				m_line.To = value;
				m_calculatedLength = false;
				m_calculatedBox = false;
			}
		}

		public Vector3 Direction
		{
			get
			{
				if (!m_calculatedLength)
					CalcLength();
				return m_line.Direction;
			}
		}

		public float Length
		{
			get
			{
				if (!m_calculatedLength)
					CalcLength();
				return m_line.Length;
			}
		}

		public BoundingBox BoundingBox
		{
			get
			{
				if (!m_calculatedBox)
					CalcBox();
				return m_line.BoundingBox;
			}
		}

		public float LengthSquared() { return Vector3.DistanceSquared(From, To); }

		private void CalcLength()
		{
			m_line.Length = (float)Math.Sqrt(LengthSquared());
			m_line.Direction = m_line.To - m_line.From;
			m_line.Direction /= m_line.Length;
			m_calculatedLength = true;
		}

		private void CalcBox()
		{
			m_line.BoundingBox = new BoundingBox();
			m_line.BoundingBox.Include(m_line.From);
			m_line.BoundingBox.Include(m_line.To);
			m_calculatedBox = true;
		}

		public void Move(Vector3 shift)
		{
			m_line.From += shift;
			m_line.To += shift;
			m_calculatedBox = false;
		}

		/// <summary>
		/// Minimum distance squared between a line and a point.
		/// </summary>
		/// <remarks>
		/// based on http://stackoverflow.com/a/1501725
		/// </remarks>
		/// <returns>The minimum distance squared between the line and the point</returns>
		public float DistanceSquared(Vector3 point)
		{
			if (From == To)
				return Vector3.DistanceSquared(From, point);

			Vector3 line_disp = To - From;

			float fraction = Vector3.Dot(point - From, line_disp) / LengthSquared(); // projection as a fraction of line_disp

			if (fraction < 0) // extends past From
				return Vector3.DistanceSquared(point, From);
			else if (fraction > 1) // extends past To
				return Vector3.DistanceSquared(point, To);

			Vector3 closestPoint = From + fraction * line_disp; // closest point on the line
			return Vector3.DistanceSquared(point, closestPoint);
		}

		/// <summary>
		/// Determines whether a point lies within a cylinder described by this line and a radius.
		/// </summary>
		/// <param name="radius">The radius of the cylinder.</param>
		/// <param name="point">The point to test.</param>
		/// <returns>True iff the point lies within the cylinder</returns>
		public bool PointInCylinder(float radius, Vector3 point)
		{
			radius *= radius;

			if (From == To)
				return Vector3.DistanceSquared(From, point) < radius;

			Vector3 line_disp = To - From;

			float fraction = Vector3.Dot(point - From, line_disp) / LengthSquared(); // projection as a fraction of line_disp

			if (fraction < 0) // extends past From
				return false;
			else if (fraction > 1) // extends past To
				return false;

			Vector3 closestPoint = From + fraction * line_disp; // closest point on the line
			return Vector3.DistanceSquared(point, closestPoint) < radius;
		}

		/// <summary>
		/// Closest point on a line to specified coordinates
		/// </summary>
		/// <remarks>
		/// based on http://stackoverflow.com/a/1501725
		/// </remarks>
		public Vector3 ClosestPoint(Vector3 coordinates)
		{
			if (From == To)
				return From;

			Vector3 line_disp = To - From;

			float fraction = Vector3.Dot(coordinates - From, line_disp) / LengthSquared(); // projection as a fraction of line_disp

			if (fraction < 0) // extends past From
				return From;
			else if (fraction > 1) // extends past To
				return To;

			return From + fraction * line_disp; // closest point on the line
		}

		/// <summary>
		/// Minimum distance between a line and a point.
		/// </summary>
		/// <returns>Minimum distance between a line and a point.</returns>
		public float Distance(Vector3 point)
		{ return (float)Math.Sqrt(DistanceSquared(point)); }

		/// <summary>
		/// Tests that the distance between line and point is less than or equal to supplied distance
		/// </summary>
		public bool DistanceLessEqual(Vector3 point, float distance)
		{
			distance *= distance;
			return DistanceSquared(point) <= distance;
		}

	}
}
