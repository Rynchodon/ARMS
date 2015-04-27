using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using VRageMath;

namespace Rynchodon
{
	public static class VectorExtensions
	{
		///// <summary>
		///// Calcluate the vector rejection of A from B.
		///// </summary>
		///// <remarks>
		///// It is not useful to normalize B first. About 10% slower than keen's version but slightly more accurate (by about 3E-7 m).
		///// </remarks>
		///// <param name="vectorB_part">used to reduce the number of operations for multiple calls with the same B, should initially be null</param>
		///// <returns>The vector rejection of A from B.</returns>
		//public static Vector3 Rejection(this Vector3 vectorA, Vector3 vectorB, ref Vector3? vectorB_part)
		//{
		//	if (vectorB_part == null)
		//		vectorB_part = vectorB / vectorB.LengthSquared();
		//	return vectorA - vectorA.Dot(vectorB) * (Vector3)vectorB_part;
		//}

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
		public static void ApplyOperation(this Vector3D vector, Func<double, double> operation, out Vector3I result)
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
		public static void ApplyOperation(this Vector3 vector, Func<double, double> operation, out Vector3I result)
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
			for (int X = min.X; X <= max.X; X++)
				for (int Y = min.Y; Y <= max.Y; Y++)
					for (int Z = min.Z; Z <= max.Z; Z++)
						if (invokeOnEach.Invoke(new Vector3I(X, Y, Z)))
							return;
		}

		///// <summary>
		///// <para>invokes a function on each vector between min and max (inclusive)</para>
		///// <para>breaks when toInvoke return true</para>
		///// </summary>
		//public static void ForEach(this Vector3I min, Vector3I max, int step, Func<Vector3I, bool> toInvoke)
		//{
		//	for (int x = min.X; x <= max.X; x += step)
		//		for (int y = min.Y; y <= max.Y; y += step)
		//			for (int z = min.Z; z <= max.Z; z += step)
		//				if (toInvoke(new Vector3I(x, y, z)))
		//					return;
		//}

		///// <summary>
		///// <para>invokes a function on each vector between min and max (inclusive)</para>
		///// <para>breaks when toInvoke return true</para>
		///// </summary>
		//public static void ForEach(this Vector3I min, Vector3I max, Func<Vector3I, bool> toInvoke)
		//{ ForEach(min, max, 1, toInvoke); }

		///// <summary>
		///// <para>invokes a function on each vector between min and max (inclusive)</para>
		///// <para>breaks when toInvoke return true</para>
		///// </summary>
		//public static void ForEach(this Vector3 min, Vector3 max, float step, Func<Vector3, bool> toInvoke)
		//{
		//	for (float x = min.X; x <= max.X; x += step)
		//		for (float y = min.Y; y <= max.Y; y += step)
		//			for (float z = min.Z; z <= max.Z; z += step)
		//				if (toInvoke(new Vector3(x, y, z)))
		//					return;
		//}

		///// <summary>
		///// <para>invokes a function on each vector between min and max (inclusive)</para>
		///// <para>breaks when toInvoke return true</para>
		///// </summary>
		//public static void ForEach(this Vector3 min, Vector3 max, Func<Vector3, bool> toInvoke)
		//{ ForEach(min, max, 1, toInvoke); }
	}
}
