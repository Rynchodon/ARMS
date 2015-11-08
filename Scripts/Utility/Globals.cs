
namespace Rynchodon
{
	public static class Globals
	{

		#region SE Constants

		public const int UpdatesPerSecond = 60;

		#endregion SE Constants

		/// <summary>Duration of one update in seconds.</summary>
		public const float UpdateDuration = 1f / (float)UpdatesPerSecond;

		/// <summary>The number of updates since mod started.</summary>
		public static ulong UpdateCount = 0;

	}
}
