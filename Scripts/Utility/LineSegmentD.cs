using System;
using VRageMath;

namespace Rynchodon
{
	public class LineSegmentD
	{

		public static double GetShortestDistanceSquared(LineSegmentD line1, LineSegmentD line2)
		{
			return LineD.GetShortestDistanceSquared(line1.m_line, line2.m_line);
		}

		public static Vector3D GetShortestVector(LineSegmentD line1, LineSegmentD line2, out Vector3D res1, out Vector3D res2)
		{
			return LineD.GetShortestVector(ref line1.m_line, ref line2.m_line, out res1, out res2);
		}

		public static explicit operator LineSegment(LineSegmentD line)
		{
			return new LineSegment(line.From, line.To);
		}

		public static explicit operator LineSegmentD(LineSegment line)
		{
			return new LineSegmentD(line.From, line.To);
		}

		private LineD m_line;
		private BoundingBoxD m_boundingBox;
		private bool m_calculatedLength, m_calculatedBox;

		public LineSegmentD() { }

		public LineSegmentD(Vector3D from, Vector3D to)
		{
			m_line = new LineD() { From = from, To = to };
		}

		public LineSegmentD(ref Vector3D from, ref Vector3D to)
		{
			m_line = new LineD() { From = from, To = to };
		}

		public LineSegmentD Clone()
		{
			return new LineSegmentD()
			{
				m_line = this.m_line,
				m_boundingBox = this.m_boundingBox,
				m_calculatedLength = this.m_calculatedLength,
				m_calculatedBox = this.m_calculatedBox
			};
		}

		public Vector3D From
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

		public Vector3D To
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

		public Vector3D Direction
		{
			get
			{
				if (!m_calculatedLength)
					CalcLength();
				return m_line.Direction;
			}
		}

		public double Length
		{
			get
			{
				if (!m_calculatedLength)
					CalcLength();
				return m_line.Length;
			}
		}

		public BoundingBoxD BoundingBox
		{
			get
			{
				if (!m_calculatedBox)
					CalcBox();
				return m_boundingBox;
			}
		}

		public double LengthSquared() { return Vector3D.DistanceSquared(From, To); }

		private void CalcLength()
		{
			m_line.Length = (double)Math.Sqrt(LengthSquared());
			m_line.Direction = m_line.To - m_line.From;
			m_line.Direction /= m_line.Length;
			m_calculatedLength = true;
		}

		private void CalcBox()
		{
			m_boundingBox = new BoundingBoxD();
			m_boundingBox.Include(m_line.From);
			m_boundingBox.Include(m_line.To);
			m_calculatedBox = true;
		}

		public void Move(Vector3D shift)
		{
			Move(ref shift);
		}

		public void Move(ref Vector3D shift)
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
		public double DistanceSquared(Vector3D point)
		{
			return DistanceSquared(ref point);
		}

		/// <summary>
		/// Minimum distance squared between a line and a point.
		/// </summary>
		/// <remarks>
		/// based on http://stackoverflow.com/a/1501725
		/// </remarks>
		/// <returns>The minimum distance squared between the line and the point</returns>
		public double DistanceSquared(ref Vector3D point)
		{
			if (From == To)
				return Vector3D.DistanceSquared(From, point);

			Vector3D line_disp = To - From;

			double fraction = Vector3D.Dot(point - From, line_disp) / LengthSquared(); // projection as a fraction of line_disp

			if (fraction < 0) // extends past From
				return Vector3D.DistanceSquared(point, From);
			else if (fraction > 1) // extends past To
				return Vector3D.DistanceSquared(point, To);

			Vector3D closestPoint = From + fraction * line_disp; // closest point on the line
			return Vector3D.DistanceSquared(point, closestPoint);
		}

		/// <summary>
		/// Determines whether a point lies within a cylinder described by this line and a radius.
		/// </summary>
		/// <param name="radius">The radius of the cylinder.</param>
		/// <param name="point">The point to test.</param>
		/// <returns>True iff the point lies within the cylinder</returns>
		public bool PointInCylinder(double radius, Vector3D point)
		{
			return PointInCylinder(radius, ref point);
		}

		/// <summary>
		/// Determines whether a point lies within a cylinder described by this line and a radius.
		/// </summary>
		/// <param name="radius">The radius of the cylinder.</param>
		/// <param name="point">The point to test.</param>
		/// <returns>True iff the point lies within the cylinder</returns>
		public bool PointInCylinder(double radius, ref Vector3D point)
		{
			radius *= radius;

			if (From == To)
				return Vector3D.DistanceSquared(From, point) < radius;

			Vector3D line_disp = To - From;

			double fraction = Vector3D.Dot(point - From, line_disp) / LengthSquared(); // projection as a fraction of line_disp

			if (fraction < 0) // extends past From
				return false;
			else if (fraction > 1) // extends past To
				return false;

			Vector3D closestPoint = From + fraction * line_disp; // closest point on the line
			return Vector3D.DistanceSquared(point, closestPoint) < radius;
		}

		/// <summary>
		/// Closest point on a line to specified coordinates
		/// </summary>
		/// <remarks>
		/// based on http://stackoverflow.com/a/1501725
		/// </remarks>
		public Vector3D ClosestPoint(Vector3D coordinates)
		{
			Vector3D point;
			ClosestPoint(ref coordinates, out point);
			return point;
		}

		/// <summary>
		/// Closest point on a line to specified coordinates
		/// </summary>
		/// <remarks>
		/// based on http://stackoverflow.com/a/1501725
		/// </remarks>
		public void ClosestPoint(ref Vector3D coordinates, out Vector3D point)
		{
			if (From == To)
			{
				point = From;
				return;
			}

			Vector3D line_disp = To - From;

			double fraction = Vector3D.Dot(coordinates - From, line_disp) / LengthSquared(); // projection as a fraction of line_disp

			//(new Logger("LineSegmentD")).debugLog("From: " + From + ", To: " + To + ", coords: " + coordinates + ", disp: " + line_disp
			//+ ", LengthSquared: " + LengthSquared() + ", fraction: " + fraction, "ClosestPoint()");

			if (fraction < 0) // extends past From
			{
				point = From;
				return;
			}
			else if (fraction > 1) // extends past To
			{
				point = To;
				return;
			}

			point = From + fraction * line_disp; // closest point on the line
		}

		/// <summary>
		/// Minimum distance between a line and a point.
		/// </summary>
		/// <returns>Minimum distance between a line and a point.</returns>
		public double Distance(Vector3D point)
		{
			return Distance(ref point);
		}

		/// <summary>
		/// Minimum distance between a line and a point.
		/// </summary>
		/// <returns>Minimum distance between a line and a point.</returns>
		public double Distance(ref Vector3D point)
		{
			return (double)Math.Sqrt(DistanceSquared(point));
		}

		/// <summary>
		/// Tests that the distance between line and point is less than or equal to supplied distance
		/// </summary>
		public bool DistanceLessEqual(Vector3D point, double distance)
		{
			return DistanceLessEqual(ref point, distance);
		}

		/// <summary>
		/// Tests that the distance between line and point is less than or equal to supplied distance
		/// </summary>
		public bool DistanceLessEqual(ref Vector3D point, double distance)
		{
			distance *= distance;
			return DistanceSquared(point) <= distance;
		}

	}
}
