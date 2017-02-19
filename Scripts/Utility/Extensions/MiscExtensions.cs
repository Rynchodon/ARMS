using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using Sandbox.Game.Entities;
using VRage.Game.Components;
using VRage.Game.ModAPI;
using VRage.ModAPI;
using VRageMath;

namespace Rynchodon
{
	public static class MiscExtensions
	{
		public static string getBestName(this IMyEntity entity)
		{
			IMyCubeBlock asBlock = entity as IMyCubeBlock;
			string name;
			if (asBlock != null)
			{
				name = asBlock.DisplayNameText;
				if (string.IsNullOrEmpty(name))
					name = asBlock.DefinitionDisplayNameText;
				return name;
			}

			if (entity == null)
				return "N/A";
			name = entity.DisplayName;
			if (string.IsNullOrEmpty(name))
			{
				name = entity.Name;
				if (string.IsNullOrEmpty(name))
				{
					name = entity.GetFriendlyName();
					if (string.IsNullOrEmpty(name))
					{
						name = entity.ToString();
					}
				}
			}
			return name;
		}

		public static string nameWithId(this IMyEntity entity)
		{
			return entity == null ? "N/A" : getBestName(entity) + '(' + entity.EntityId + ')';
		}

		public static Vector3 GetLinearAcceleration(this MyPhysicsComponentBase Physics)
		{
			if (Physics.CanUpdateAccelerations && Physics.LinearAcceleration == Vector3.Zero)
				Physics.UpdateAccelerations();
			return Physics.LinearAcceleration;
		}

		public static Vector3 GetLinearVelocity(this IMyEntity entity)
		{
			entity = entity.GetTopMostParent();
			if (entity.Physics == null)
				return Vector3.Zero;
			return entity.Physics.LinearVelocity;
		}

		public static Vector3D GetCentre(this IMyCubeBlock block)
		{
			return block.GetPosition();
		}

		public static Vector3D GetCentre(this IMyCubeGrid grid)
		{
			return Vector3D.Transform(grid.LocalAABB.Center, grid.WorldMatrix);
		}

		public static Vector3D GetCentre(this IMyEntity entity)
		{
			IMyCubeBlock block = entity as IMyCubeBlock;
			if (block != null)
				return block.GetPosition();
			MyPlanet planet = entity as MyPlanet;
			if (planet != null)
				return planet.WorldMatrix.Translation;
			IMyVoxelBase asteroid = entity as IMyVoxelBase;
			if (asteroid != null)
				return asteroid.WorldAABB.Center;

			return Vector3D.Transform(entity.LocalAABB.Center, entity.WorldMatrix);
		}

		public static void throwIfNull_argument(this object argument, string name)
		{
			if (argument == null)
				throw new ArgumentNullException(name);
		}

		public static void throwIfNull_variable(this object variable, string name)
		{
			if (variable == null)
				throw new NullReferenceException(name);
		}

		public static string ToPrettySeconds(this VRage.Library.Utils.MyTimeSpan timeSpan)
		{ return PrettySI.makePretty(timeSpan.Seconds) + 's'; }

		#region Distance

		/// <summary>
		/// Minimum distance squared between a line and a point.
		/// </summary>
		/// <remarks>
		/// based on http://stackoverflow.com/a/1501725
		/// </remarks>
		/// <returns>The minimum distance squared between the line and the point</returns>
		public static float DistanceSquared(this Line line, Vector3 point)
		{
			if (line.From == line.To)
				return Vector3.DistanceSquared(line.From, point);

			Vector3 line_disp = line.To - line.From;
			float line_distSq = line_disp.LengthSquared();

			float fraction = Vector3.Dot(point - line.From, line_disp) / line_distSq; // projection as a fraction of line_disp

			if (fraction < 0) // extends past From
				return Vector3.DistanceSquared(point, line.From);
			else if (fraction > 1) // extends past To
				return Vector3.DistanceSquared(point, line.To);

			Vector3 closestPoint = line.From + fraction * line_disp; // closest point on the line
			return Vector3.DistanceSquared(point, closestPoint);
		}

		public static bool PointInCylinder(this Line line, float radius, Vector3 point)
		{
			radius *= radius;

			if (line.From == line.To)
				return Vector3.DistanceSquared(line.From, point) < radius;

			Vector3 line_disp = line.To - line.From;
			float line_distSq = line_disp.LengthSquared();

			float fraction = Vector3.Dot(point - line.From, line_disp) / line_distSq; // projection as a fraction of line_disp

			if (fraction < 0) // extends past From
				return false;
			else if (fraction > 1) // extends past To
				return false;

			Vector3 closestPoint = line.From + fraction * line_disp; // closest point on the line
			return Vector3.DistanceSquared(point, closestPoint) < radius;
		}

		/// <summary>
		/// Closest point on a line to specified coordinates
		/// </summary>
		/// <remarks>
		/// based on http://stackoverflow.com/a/1501725
		/// </remarks>
		public static Vector3 ClosestPoint(this Line line, Vector3 coordinates)
		{
			if (line.From == line.To)
				return line.From;

			Vector3 line_disp = line.To - line.From;
			float line_distSq = line_disp.LengthSquared();

			float fraction = Vector3.Dot(coordinates - line.From, line_disp) / line_distSq; // projection as a fraction of line_disp

			if (fraction < 0) // extends past From
				return line.From;
			else if (fraction > 1) // extends past To
				return line.To;

			return line.From + fraction * line_disp; // closest point on the line
		}

		/// <summary>
		/// Minimum distance between a line and a point.
		/// </summary>
		/// <returns>Minimum distance between a line and a point.</returns>
		public static float Distance(this Line line, Vector3 point)
		{ return (float)Math.Sqrt(line.DistanceSquared(point)); }

		/// <summary>
		/// Tests that the distance between line and point is less than or equal to supplied distance
		/// </summary>
		public static bool DistanceLessEqual(this Line line, Vector3 point, float distance)
		{
			distance *= distance;
			return line.DistanceSquared(point) <= distance;
		}

		public static double Distance(this BoundingSphereD first, BoundingSphereD second)
		{
			double distance_centre = Vector3D.Distance(first.Center, second.Center);
			return distance_centre - first.Radius - second.Radius;
		}

		public static double DistanceSquared(this BoundingBoxD first, BoundingBoxD second)
		{
			double distanceSquared = 0;

			for (int i = 0; i < 3; i++)
			{
				double
					firstMin = first.Min.GetDim(i),
					firstMax = first.Max.GetDim(i),
					secondMin = second.Min.GetDim(i),
					secondMax = second.Max.GetDim(i);

				if (firstMin > secondMax)
				{
					double delta = secondMax - firstMin;
					distanceSquared += delta * delta;
				}
				else if (secondMin > firstMax)
				{
					double delta = firstMax - secondMin;
					distanceSquared += delta * delta;
				}
			}

			return distanceSquared;
		}

		public static double Distance(this BoundingBoxD first, BoundingBoxD second)
		{ return Math.Sqrt(first.DistanceSquared(second)); }

		public static double DistanceSquared(this BoundingBoxD box, Vector3D point)
		{
			Vector3D closestPoint = Vector3D.Clamp(point, box.Min, box.Max);
			double result;
			Vector3D.DistanceSquared(ref point, ref closestPoint, out result);
			return result;
		}

		///// <summary>
		///// Finds the shorter of distance between AABB and distance between Volume
		///// </summary>
		//public static double Distance_ShorterBounds(this IMyEntity first, IMyEntity second)
		//{
		//	double distanceAABB = Distance(first.WorldAABB, second.WorldAABB);
		//	double distanceVolume = Distance(first.WorldVolume, second.WorldVolume);
		//	return Math.Min(distanceAABB, distanceVolume);
		//}

		#endregion
		#region Float Significand++/--
		// based on http://realtimemadness.blogspot.ca/2012/06/nextafter-in-c-without-allocations-of.html

		[StructLayout(LayoutKind.Explicit)]
		private struct FloatIntUnion
		{
			[FieldOffset(0)]
			public int i;
			[FieldOffset(0)]
			public float f;
		}

		public static float IncrementSignificand(this float number)
		{
			FloatIntUnion union; union.i = 0; union.f = number;
			union.i++;
			return union.f;
		}

		public static float DecrementSignificand(this float number)
		{
			FloatIntUnion union; union.i = 0; union.f = number;
			union.i--;
			return union.f;
		}

		#endregion

		/// <summary>
		/// Longest of width, length, height.
		/// </summary>
		public static float GetLongestDim(this BoundingBox box)
		{
			Vector3 Size = box.Size;
			return MathHelper.Max(Size.X, Size.Y, Size.Z);
		}

		/// <summary>
		/// Shortest of width, length, height.
		/// </summary>
		public static float GetShortestDim(this BoundingBox box)
		{
			Vector3 Size = box.Size;
			return MathHelper.Min(Size.X, Size.Y, Size.Z);
		}

		/// <summary>
		/// Longest of width, length, height.
		/// </summary>
		public static double GetLongestDim(this BoundingBoxD box)
		{
			Vector3 Size = box.Size;
			return MathHelper.Max(Size.X, Size.Y, Size.Z);
		}

		/// <summary>
		/// Shortest of width, length, height.
		/// </summary>
		public static double GetShortestDim(this BoundingBoxD box)
		{
			Vector3D Size = box.Size;
			return MathHelper.Min(Size.X, Size.Y, Size.Z);
		}

		/// <summary>
		/// Longest of width, length, height.
		/// </summary>
		public static float GetLongestDim(this IMyCubeGrid grid)
		{ return grid.LocalAABB.GetLongestDim(); }

		/// <summary>
		/// Shortest of width, length, height.
		/// </summary>
		public static float GetShortestDim(this IMyCubeGrid grid)
		{ return grid.LocalAABB.GetShortestDim(); }

		/// <summary>
		/// <para>Tests the AABB and the Volume of first against the AABB and Volume of the second.</para>
		/// </summary>
		/// <returns>true iff all intersect</returns>
		public static bool IntersectsAABBVolume(this IMyEntity first, IMyEntity second)
		{
			return first.WorldAABB.Intersects(second.WorldAABB)
				&& first.WorldAABB.Intersects(second.WorldVolume)
				&& first.WorldVolume.Intersects(second.WorldAABB)
				&& first.WorldVolume.Intersects(second.WorldVolume);
		}

		public static bool IsValid(this float number)
		{ return !float.IsNaN(number) && !float.IsInfinity(number); }

		public static bool IsValid(this double number)
		{ return !double.IsNaN(number) && !double.IsInfinity(number); }

		/// <summary>
		/// Check that a value is a real number and not zero.
		/// </summary>
		public static bool ValidNonZero(this float number)
		{
			return !float.IsInfinity(number) && (0f < number || number < 0f);
		}

		/// <summary>
		/// Check that a value is a real number and not zero.
		/// </summary>
		public static bool ValidNonZero(this double number)
		{
			return !double.IsInfinity(number) && (0d < number || number < 0d);
		}

		public static bool NullOrClosed(this IMyEntity entity)
		{ return entity == null || entity.MarkedForClose || entity.Closed; }

		/// <summary>
		/// Try to perform an Action on game thread, if it fails do not crash the game.
		/// </summary>
		public static void TryInvokeOnGameThread(this IMyUtilities util, Action invoke, [CallerFilePath] string callerFile = null, [CallerMemberName] string callerMember = null, [CallerLineNumber] int callerLineNumber = 0)
		{
			util.InvokeOnGameThread(() => {
				try { invoke.Invoke(); }
				catch (Exception ex)
				{
					Logger.AlwaysLog("Exception: " + ex, Logger.severity.ERROR, filePath: callerFile, member: callerMember, lineNumber: callerLineNumber);
				}
			});
		}

		public static bool EqualsIgnoreCapacity(this StringBuilder first, StringBuilder second)
		{
			if (ReferenceEquals(first, second))
				return true;

			if (first.Length != second.Length)
				return false;

			for (int i = 0; i < first.Length; i++)
				if (first[i] != second[i])
					return false;

			return true;
		}

		public static IEnumerable<T> ToEnumerable<T>(this T item)
		{
			yield return item;
		}

	}
}
