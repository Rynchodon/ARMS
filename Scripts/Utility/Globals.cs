using System;
using VRageMath;

namespace Rynchodon
{
	public static class Globals
	{

		#region SE Constants

		public const int UpdatesPerSecond = 60;
		public const float PlayerBroadcastRadius = 200f;

		#endregion SE Constants

		/// <summary>Duration of one update in seconds.</summary>
		public const float UpdateDuration = 1f / (float)UpdatesPerSecond;

		public static readonly Random Random = new Random();

		/// <summary>The number of updates since mod started.</summary>
		public static ulong UpdateCount = 0;

		public static readonly Vector3I[] CellNeighbours = 
		{
			new	Vector3I(-1, -1, -1),
			new Vector3I(-1, -1, 0),
			new Vector3I(-1, -1, 1),
			new Vector3I(-1, 0, -1),
			new Vector3I(-1, 0, 0),
			new Vector3I(-1, 0, 1),
			new Vector3I(-1, 1, -1),
			new Vector3I(-1, 1, 0),
			new Vector3I(-1, 1, 1),

			new	Vector3I(0, -1, -1),
			new Vector3I(0, -1, 0),
			new Vector3I(0, -1, 1),
			new Vector3I(0, 0, -1),
			// new Vector3I(0, 0, 0), // not a neighbour
			new Vector3I(0, 0, 1),
			new Vector3I(0, 1, -1),
			new Vector3I(0, 1, 0),
			new Vector3I(0, 1, 1),
			
			new	Vector3I(1, -1, -1),
			new Vector3I(1, -1, 0),
			new Vector3I(1, -1, 1),
			new Vector3I(1, 0, -1),
			new Vector3I(1, 0, 0),
			new Vector3I(1, 0, 1),
			new Vector3I(1, 1, -1),
			new Vector3I(1, 1, 0),
			new Vector3I(1, 1, 1),
		};

	}
}
