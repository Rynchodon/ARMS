using System;
using System.Collections.Generic;
using VRageMath;

namespace Rynchodon
{
	public static class VectorExtensions
	{
		#region ApplyOperation

		/// <summary>
		/// apply an operation to each of x, y, z
		/// </summary>
		public static void ApplyOperation(this Vector3D vector, Func<double, double> operation, out Vector3D result)
		{
			result = new Vector3D(operation(vector.X), operation(vector.Y), operation(vector.Z));
		}

		/// <summary>
		/// apply an operation to each of x, y, z
		/// </summary>
		public static void ApplyOperation(this Vector3D vector, Func<double, float> operation, out Vector3 result)
		{
			result = new Vector3D(operation(vector.X), operation(vector.Y), operation(vector.Z));
		}

		/// <summary>
		/// apply an operation to each of x, y, z
		/// </summary>
		public static void ApplyOperation(this Vector3D vector, Func<double, double> operation, out Vector3 result)
		{
			result = new Vector3D(operation(vector.X), operation(vector.Y), operation(vector.Z));
		}

		/// <summary>
		/// apply an operation to each of x, y, z
		/// </summary>
		public static void ApplyOperation(this Vector3D vector, Func<double, int> operation, out Vector3I result)
		{
			result = new Vector3I(operation(vector.X), operation(vector.Y), operation(vector.Z));
		}

		/// <summary>
		/// apply an operation to each of x, y, z
		/// </summary>
		public static void ApplyOperation(this Vector3 vector, Func<float, float> operation, out Vector3 result)
		{
			result = new Vector3(operation(vector.X), operation(vector.Y), operation(vector.Z));
		}

		/// <summary>
		/// apply an operation to each of x, y, z
		/// </summary>
		public static void ApplyOperation(this Vector3 vector, Func<float, double> operation, out Vector3 result)
		{
			result = new Vector3(operation(vector.X), operation(vector.Y), operation(vector.Z));
		}

		/// <summary>
		/// apply an operation to each of x, y, z
		/// </summary>
		public static void ApplyOperation(this Vector3 vector, Func<float, int> operation, out Vector3I result)
		{
			result = new Vector3I(operation(vector.X), operation(vector.Y), operation(vector.Z));
		}

		/// <summary>
		/// apply an operation to each of x, y, z
		/// </summary>
		public static void ApplyOperation(this Vector3I vector, Func<int, int> operation, out Vector3I result)
		{
			result = new Vector3I(operation(vector.X), operation(vector.Y), operation(vector.Z));
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

		public static IEnumerator<Vector3I> ForEachVector(this Vector3I min, Vector3I max)
		{
			Vector3I vector ;
			for (vector.X = min.X; vector.X <= max.X; vector.X++)
				for (vector.Y = min.Y; vector.Y <= max.Y; vector.Y++)
					for (vector.Z = min.Z; vector.Z <= max.Z; vector.Z++)
						yield return vector;
		}

		/// <summary>
		/// Returns the acute angle between two vectors.
		/// </summary>
		public static float AngleBetween(this Vector3 first, Vector3 second)
		{
			return (float)Math.Acos(first.Dot(second) / (first.Length() * second.Length()));
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

		public static Matrix OuterProduct(this Vector3 first, ref Vector3 second, out Matrix result)
		{
			result = new Matrix()
			{
				M11 = first.X * second.X,
				M12 = first.X * second.Y,
				M13 = first.X * second.Z,
				M22 = first.Y * second.Y,
				M23 = first.Y * second.Z,
				M33 = first.Z * second.Z
			};
			result.M21 = result.M12;
			result.M31 = result.M13;
			result.M32 = result.M23;
			return result;
		}

	}
}
