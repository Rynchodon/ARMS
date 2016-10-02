using System;
using System.Collections.Generic;
using VRage.Game;
using VRage.Game.ObjectBuilders.Definitions;
using VRageMath;

namespace Rynchodon
{
	public static class Globals
	{

		private class StaticVariables
		{
			public readonly Vector3I[] NeighboursOne = new Vector3I[]
			{
				new Vector3I(0, 0, -1),
				new Vector3I(0, 0, 1),
				new Vector3I(0, -1, 0),
				new Vector3I(0, 1, 0),
				new Vector3I(-1, 0, 0),
				new Vector3I(1, 0, 0)
			};
			public readonly Vector3I[] NeighboursTwo = new Vector3I[]
			{
				new Vector3I(0, 1, -1),
				new Vector3I(1, 0, -1),
				new Vector3I(0, -1, -1),
				new Vector3I(-1, 0, -1),
				new Vector3I(1, 1, 0),
				new Vector3I(1, -1, 0),
				new Vector3I(-1, -1, 0),
				new Vector3I(-1, 1, 0),
				new Vector3I(0, 1, 1),
				new Vector3I(1, 0, 1),
				new Vector3I(0, -1, 1),
				new Vector3I(-1, 0, 1),
			};
			public readonly Vector3I[] NeighboursThree = new Vector3I[]
			{
				new Vector3I(1, 1, -1),
				new Vector3I(1, -1, -1),
				new Vector3I(-1, -1, -1),
				new Vector3I(-1, 1, -1),
				new Vector3I(1, 1, 1),
				new Vector3I(1, -1, 1),
				new Vector3I(-1, -1, 1),
				new Vector3I(-1, 1, 1),
			};
		}

		#region SE Constants

		public const int UpdatesPerSecond = 60;
		public const float PlayerBroadcastRadius = 200f;

		#endregion SE Constants

		/// <summary>Duration of one update in seconds.</summary>
		public const float UpdateDuration = 1f / (float)UpdatesPerSecond;

		public const double UpdatesToTicks = (double)TimeSpan.TicksPerSecond / (double)UpdatesPerSecond;

		public static readonly Random Random = new Random();

		/// <summary>The number of updates since mod started.</summary>
		public static ulong UpdateCount = 0;

		/// <summary>Simulation speed of game based on time between updates.</summary>
		public static float SimSpeed = 1f;

		/// <summary>Elapsed time based on number of updates i.e. not incremented while paused.</summary>
		public static TimeSpan ElapsedTime
		{
			get { return new TimeSpan((long)(UpdateCount * UpdatesToTicks)); }
		}

		public static long ElapsedTimeTicks
		{
			get { return (long)(UpdateCount * UpdatesToTicks); }
		}

		public static readonly MyDefinitionId Electricity = new MyDefinitionId(typeof(MyObjectBuilder_GasProperties), "Electricity");

		private static bool m_worldClosed;

		private static StaticVariables Static = new StaticVariables();

		public static bool WorldClosed
		{
			get { return m_worldClosed; }
			set
			{
				m_worldClosed = true;
				Static = null;
			}
		}

		public static IEnumerable<Vector3I> NeighboursOne { get { return Static.NeighboursOne; } }

		public static IEnumerable<Vector3I> NeighboursTwo { get { return Static.NeighboursTwo; } }

		public static IEnumerable<Vector3I> NeighboursThree { get { return Static.NeighboursThree; } }

		public static IEnumerable<Vector3I> Neighbours
		{
			get
			{
				foreach (Vector3I vector in Static.NeighboursOne)
					yield return vector;
				foreach (Vector3I vector in Static.NeighboursTwo)
					yield return vector;
				foreach (Vector3I vector in Static.NeighboursThree)
					yield return vector;
			}
		}

	}
}
