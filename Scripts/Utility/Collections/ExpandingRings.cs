using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using VRageMath;

namespace Rynchodon.Utility.Collections
{
	/// <summary>
	/// For 2D int coordinates, yields neighbours in expanding rings. Each ring contains the all the closest elements to 0,0 that have not yet been yielded.
	/// </summary>
	public class ExpandingRings
	{

		public struct Ring
		{
			public int DistanceSquared;
			public Vector2I[] Squares;

			public Ring(int distSq, Vector2I[] squares)
			{
				this.DistanceSquared = distSq;
				this.Squares = squares;
			}
		}

		private struct Square : IEnumerable<Vector2I>
		{

			public int MinDistance, Step;

			public Square(int minDist)
			{
				this.MinDistance = minDist;
				this.Step = 0;
			}

			public int DistSquared()
			{
				return MinDistance * MinDistance + Step * Step;
			}

			public IEnumerator<Vector2I> GetEnumerator()
			{
				if (Step < 0 || Step > MinDistance)
					throw new Exception("Step is invalid. Step: " + Step + ", MinDistance: " + MinDistance);
				else if (Step == 0)
				{
					yield return new Vector2I(0, MinDistance);
					yield return new Vector2I(MinDistance, 0);
					yield return new Vector2I(0, -MinDistance);
					yield return new Vector2I(-MinDistance, 0);
				}
				else if (Step == MinDistance)
				{
					yield return new Vector2I(MinDistance, MinDistance);
					yield return new Vector2I(MinDistance, -MinDistance);
					yield return new Vector2I(-MinDistance, -MinDistance);
					yield return new Vector2I(-MinDistance, MinDistance);
				}
				else
				{
					yield return new Vector2I(Step, MinDistance);
					yield return new Vector2I(MinDistance, Step);
					yield return new Vector2I(MinDistance, -Step);
					yield return new Vector2I(Step, -MinDistance);
					yield return new Vector2I(-Step, -MinDistance);
					yield return new Vector2I(-MinDistance, -Step);
					yield return new Vector2I(-MinDistance, Step);
					yield return new Vector2I(-Step, MinDistance);
				}
			}

			IEnumerator IEnumerable.GetEnumerator()
			{
				return GetEnumerator();
			}

		}

		private static Ring[] m_rings = new Ring[0];
		private static FastResourceLock m_lock = new FastResourceLock();

		public static Ring GetRing(int index)
		{
			if (m_rings.Length <= index)
				using (m_lock.AcquireExclusiveUsing())
					if (m_rings.Length <= index)
					{
						int length = Math.Max(1024, Math.Max(index + 1, m_rings.Length * 2));
						Logger.DebugLog("Rebuilding to " + length, Logger.severity.DEBUG);
						m_rings = new Ring[length];
						ExpandingRings exRings = new ExpandingRings();
						for (int i = 0; i < length; i++)
							m_rings[i] = new Ring(exRings.m_bestDistSquared, exRings.EnumerateRing().ToArray());
					}
			using (m_lock.AcquireSharedUsing())
				return m_rings[index];
		}

		private Deque<Square> m_activeSquares = new Deque<Square>();
		private List<int> m_bestSquareIndex = new List<int>();
		private int m_nextSquare = 1;
		private int m_bestDistSquared;

		private ExpandingRings() { }

		/// <summary>
		/// Restarts at first ring.
		/// </summary>
		public void Reset()
		{
			m_activeSquares.Clear();
			m_nextSquare = 1;
			m_activeSquares.AddTail(new Square(m_nextSquare++));
		}

		/// <summary>
		/// Yields all the rings that are closest to 0,0 that have not yet been yielded.
		/// </summary>
		public IEnumerable<Vector2I> EnumerateRing()
		{
#if PROFILE
			Profiler.StartProfileBlock();
			try {
#endif
			m_bestDistSquared = int.MaxValue;
			m_bestSquareIndex.Clear();

			for (int i = m_activeSquares.Count - 1; i >= 0; i--)
			{
				int distSq = m_activeSquares[i].DistSquared();
				if (distSq <= m_bestDistSquared)
				{
					if (distSq < m_bestDistSquared)
					{
						m_bestSquareIndex.Clear();
						m_bestDistSquared = distSq;
					}
					m_bestSquareIndex.Add(i);
				}
			}

			int nextDistSq = m_nextSquare * m_nextSquare;
			if (nextDistSq <= m_bestDistSquared)
			{
				if (nextDistSq < m_bestDistSquared)
				{
					m_bestSquareIndex.Clear();
					m_bestDistSquared = nextDistSq;
				}
				m_bestSquareIndex.Add(m_activeSquares.Count);
				m_activeSquares.AddTail(new Square(m_nextSquare++));
			}

			foreach (int index in m_bestSquareIndex)
			{
				Square sq = m_activeSquares[index];
				foreach (Vector2I offset in sq)
					yield return offset;
				sq.Step++;
				if (sq.MinDistance < sq.Step)
				{
					Logger.DebugLog("Current square has reached its maximum step, but it is not head", Logger.severity.FATAL, condition: m_activeSquares[0].MinDistance != sq.MinDistance);
					m_activeSquares.RemoveHead();
				}
				else
					m_activeSquares[index] = sq;
			}
#if PROFILE
			}	finally { Profiler.EndProfileBlock(); }
#endif
		}

	}
}
