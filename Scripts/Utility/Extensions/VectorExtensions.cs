using System;
using VRageMath;

namespace Rynchodon
{
	public static class VectorExtensions
	{
		#region ApplyOperation

		/// <summary>
		/// aply an operation to each of x, y, z
		/// </summary>
		public static void ApplyOperation(this Vector3D vector, Func<double, double> operation, out Vector3D result)
		{
			double x = operation(vector.X);
			double y = operation(vector.Y);
			double z = operation(vector.Z);
			result = new Vector3D(x, y, z);
		}

		/// <summary>
		/// aply an operation to each of x, y, z
		/// </summary>
		public static void ApplyOperation(this Vector3D vector, Func<double, int> operation, out Vector3I result)
		{
			int x = (int)operation(vector.X);
			int y = (int)operation(vector.Y);
			int z = (int)operation(vector.Z);
			result = new Vector3I(x, y, z);
		}

		/// <summary>
		/// aply an operation to each of x, y, z
		/// </summary>
		public static void ApplyOperation(this Vector3 vector, Func<double, double> operation, out Vector3 result)
		{
			double x = operation(vector.X);
			double y = operation(vector.Y);
			double z = operation(vector.Z);
			result = new Vector3(x, y, z);
		}

		/// <summary>
		/// aply an operation to each of x, y, z
		/// </summary>
		public static void ApplyOperation(this Vector3 vector, Func<double, int> operation, out Vector3I result)
		{
			int x = (int)operation(vector.X);
			int y = (int)operation(vector.Y);
			int z = (int)operation(vector.Z);
			result = new Vector3I(x, y, z);
		}

		/// <summary>
		/// aply an operation to each of x, y, z
		/// </summary>
		public static void ApplyOperation(this Vector3I vector, Func<int, int> operation, out Vector3I result)
		{
			int x = operation(vector.X);
			int y = operation(vector.Y);
			int z = operation(vector.Z);
			result = new Vector3I(x, y, z);
		}

		#endregion

		/// <summary>
		/// invoke a function on each vector from min to max
		/// </summary>
		/// <param name="min">the first vector</param>
		/// <param name="max">the last vector</param>
		/// <param name="invokeOnEach">function to call for each vector, if it returns true short-curcuit</param>
		public static void ForEachVector(this Vector3I min, Vector3I max, Func<Vector3I, bool> invokeOnEach)
		{
			Vector3I vector = Vector3I.Zero;
			for (vector.X = min.X; vector.X <= max.X; vector.X++)
				for (vector.Y = min.Y; vector.Y <= max.Y; vector.Y++)
					for (vector.Z = min.Z; vector.Z <= max.Z; vector.Z++)
						if (invokeOnEach.Invoke(vector))
							return;
		}

		/// <summary>
		/// Returns the acute angle between two vectors.
		/// </summary>
		public static float AngleBetween(this Vector3 first, Vector3 second)
		{
			first.Normalize();
			second.Normalize();
			return (float)Math.Acos(first.Dot(second));
		}

		/// <summary>
		/// Returns the acute angle between two vectors.
		/// </summary>
		public static double AngleBetween(this Vector3D first, Vector3D second)
		{
			return Math.Acos(first.Dot(second) / (first.Length() * second.Length()));
		}

		public static string ToGpsTag(this Vector3 vec, string name)
		{
			return "GPS:" + name + ':' + vec.X + ':' + vec.Y + ':' + vec.Z + ':';
		}

		public static string ToGpsTag(this Vector3D vec, string name)
		{
			return "GPS:" + name + ':' + vec.X + ':' + vec.Y + ':' + vec.Z + ':';
		}

		public static string ToPretty(this Vector3 vec)
		{
			return '{' + PrettySI.makePretty(vec.X, 3, false) + ", " + PrettySI.makePretty(vec.Y, 3, false) + ", " + PrettySI.makePretty(vec.Z, 3, false) + '}';
		}

		public static string ToPretty(this Vector3D vec)
		{
			return '{' + PrettySI.makePretty(vec.X, 3, false) + ", " + PrettySI.makePretty(vec.Y, 3, false) + ", " + PrettySI.makePretty(vec.Z, 3, false) + '}';
		}

		public static int DistanceSquared(this Vector3I vec, Vector3I sec)
		{
			int X = vec.X - sec.X, Y = vec.Y - sec.Y, Z = vec.Z - sec.Z;
			return X * X + Y * Y + Z * Z;
		}

	}
}
