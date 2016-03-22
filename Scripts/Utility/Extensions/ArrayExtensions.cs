
namespace Rynchodon
{
	public static class ArrayExtensions
	{

		public static bool IsNullOrEmpty<T>(this T[] array)
		{
			return array == null || array.Length == 0;
		}

	}
}
