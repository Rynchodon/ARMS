using System.Collections;
using System.Collections.Generic;
using VRageMath;

namespace Rynchodon.Utility.Collections
{

	public class Vector2IMatrix<T> : IEnumerable<KeyValuePair<Vector2I, T>>
	{

		public static IEqualityComparer<T> EqualityComparer { get { return OffsetList<T>.EqualityComparer; } set { OffsetList<T>.EqualityComparer = value; } }

		private static bool EqualsDefault(T value)
		{
			return EqualityComparer.Equals(value, default(T));
		}

		private OffsetList<OffsetList<T>> m_data;

		public int Count { get; private set; }

		/// <summary>
		/// Gets or sets the element at the specified index.
		/// </summary>
		/// <param name="index">The position in the matrix</param>
		/// <returns>The element at the specified index</returns>
		public T this[Vector2I index]
		{
			get
			{
				if (m_data == null)
					return default(T);
				OffsetList<T> xList = m_data[index.X];
				if (xList == null)
					return default(T);
				return xList[index.Y];
			}
			set
			{
				OffsetList<T> xList = ForceGetXList(index);
				T current = xList[index.Y];
				if (EqualsDefault(current))
				{
					if (!EqualsDefault(value))
						Count++;
				}
				else if (EqualsDefault(value))
					Count--;
				ForceGetXList(index)[index.Y] = value;
			}
		}

		/// <summary>
		/// Add the specified value at index.
		/// </summary>
		/// <param name="index">The position in the matrix.</param>
		/// <param name="value">The value to set the element to.</param>
		/// <returns>True if the element could be added. False if it was already set.</returns>
		public bool Add(Vector2I index, T value)
		{
			OffsetList<T> xList = ForceGetXList(index);
			if (!EqualsDefault(xList[index.Y]))
				return false;
			xList[index.Y] = value;
			Count++;
			return true;
		}

		/// <summary>
		/// Sets every element in the matrix to default(T).
		/// </summary>
		public void Clear()
		{
			if (m_data == null)
				return;

			foreach (KeyValuePair<int, OffsetList<T>> pair in m_data)
				if (pair.Value != null)
					pair.Value.Clear();

			Count = 0;
		}

		/// <summary>
		/// Checks whether the matrix contains a non-default value at the specified index.
		/// </summary>
		/// <param name="index">The position in the matrix</param>
		/// <returns>True if a non-default value exists at the specified index.</returns>
		public bool Contains(Vector2I index)
		{
			if (m_data == null)
				return false;
			OffsetList<T> xList = m_data[index.X];
			if (xList == null)
				return false;
			return !EqualsDefault(xList[index.Y]);
		}

		public IEnumerator<KeyValuePair<Vector2I, T>> GetEnumerator()
		{
			if (m_data == null)
				yield break;

			foreach (KeyValuePair<int, OffsetList<T>> xList in m_data)
				if (xList.Value != null)
					foreach (KeyValuePair<int, T> item in xList.Value)
						if (!EqualsDefault(item.Value))
							yield return new KeyValuePair<Vector2I, T>(new Vector2I(xList.Key, item.Key), item.Value);
		}

		IEnumerator IEnumerable.GetEnumerator()
		{
			return GetEnumerator();
		}

		public IEnumerable<KeyValuePair<Vector2I, T>> MiddleOut()
		{
			if (m_data == null)
				yield break;

			foreach (KeyValuePair<int, OffsetList<T>> xList in m_data.MiddleOut())
				foreach (KeyValuePair<int, T> item in xList.Value.MiddleOut())
					yield return new KeyValuePair<Vector2I, T>(new Vector2I(xList.Key, item.Key), item.Value);
		}

		/// <summary>
		/// Get the array in m_data at index.X with a minimum capacity of index.Y + 1, enlarging m_data or the array as needed.
		/// </summary>
		/// <param name="index">The position in the matrix</param>
		/// <returns>The array in m_data at index.X</returns>
		private OffsetList<T> ForceGetXList(Vector2I index)
		{
			if (m_data == null)
			{
				m_data = new OffsetList<OffsetList<T>>(index.X);
				//Logger.DebugLog("Created matrix");
			}

			OffsetList<T> xList = m_data[index.X];
			if (xList == null)
			{
				xList = new OffsetList<T>(index.Y);
				m_data[index.X] = xList;
				//Logger.DebugLog("Created x array at " + index.X);
			}

			return xList;
		}

	}
}
