
using VRageMath;

namespace Rynchodon
{
	public static class ArrayExtensions
	{

		public static bool IsNullOrEmpty<T>(this T[] array)
		{
			return array == null || array.Length == 0;
		}

		public static void SetAll<T>(this T[] array, T value = default(T))
		{
			int length = array.Length;
			for (int i = 0; i < length; i++)
				array[i] = value;
		}

		public static void SetAll<T>(this T[,] array, T value = default(T))
		{
			Vector2I length, index;
			length.X = array.GetLength(0);
			length.Y = array.GetLength(1);
			for (index.X = 0; index.X < length.X; index.X++)
				for (index.Y = 0; index.Y < length.Y; index.Y++)
					array[index.X, index.Y] = value;
		}

		public static void SetAll<T>(this T[,,] array, T value = default(T))
		{
			Vector3I length, index;
			length.X = array.GetLength(0);
			length.Y = array.GetLength(1);
			length.Z = array.GetLength(2);
			for (index.X = 0; index.X < length.X; index.X++)
				for (index.Y = 0; index.Y < length.Y; index.Y++)
					for (index.Z = 0; index.Z < length.Z; index.Z++)
						array[index.X, index.Y, index.Z] = value;
		}

	}
}
