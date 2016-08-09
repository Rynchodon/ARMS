using System;
using System.Collections.Generic;

namespace Rynchodon.Utility.Collections
{
	/// <summary>
	/// An array list with an offset to its indexing. 
	/// User need only supply the indecies, the internal array is adjusted automatically to include all indecies with a size no greater than twice the range of supplied indecies.
	/// </summary>
	/// <typeparam name="T">The type of element in the list.</typeparam>
	public class OffsetList<T> : IEnumerable<KeyValuePair<int, T>>
	{
		// Variable names are prefixed with i when refering to an index of the internal array.

		private const int DefaultCapacity = 4;

		static OffsetList()
		{
			Logger.SetFileName("OffsetArray<T>");
		}

		private static bool EqualsDefault(T value)
		{
			return EqualityComparer<T>.Default.Equals(value, default(T));
		}

		private int m_offset;
		private T[] m_array;

		public OffsetList() { }

		public OffsetList(int includeIndex)
		{
			InitializeArray(includeIndex);
		}

		/// <summary>
		/// Gets or sets an element in the list.
		/// When getting an element that has not been set, default(T) is returned.
		/// When setting an element the list capacity may be increased.
		/// </summary>
		/// <param name="index">The position in the list.</param>
		/// <returns>The element at the specified position.</returns>
		public T this[int index]
		{
			get
			{
				if (m_array == null)
					return default(T);
				int iIndex = index - m_offset;
				if (iIndex >= 0 && iIndex < m_array.Length)
					return m_array[iIndex];
				return default(T);
			}
			set
			{
				if (m_array == null)
					InitializeArray(index);
				int iIndex = index - m_offset;
				if (iIndex < 0 || iIndex >= m_array.Length)
				{
					Include(index);
					iIndex = index - m_offset;
				}
				m_array[iIndex] = value;
			}
		}

		/// <summary>
		/// Sets every element in the array to default(T).
		/// </summary>
		public void Clear()
		{
			if (m_array != null)
				for (int ii = 0; ii < m_array.Length; ii++)
					m_array[ii] = default(T);
		}

		/// <summary>
		/// Returns an enumerator that iterates through the OffsetList.
		/// </summary>
		/// <returns>An enumerator for the OffsetList.</returns>
		public IEnumerator<KeyValuePair<int, T>> GetEnumerator()
		{
			if (m_array == null)
				yield break;

			for (int ii = 0; ii < m_array.Length; ii++)
				yield return new KeyValuePair<int, T>(ii + m_offset, m_array[ii]);
		}

		System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator()
		{
			return GetEnumerator();
		}

		/// <summary>
		/// Set m_offset to offset and create a new array of DefaultCapacity.
		/// </summary>
		/// <param name="offset">Index that triggered the new array allocation.</param>
		private void InitializeArray(int offset)
		{
			const int padFront = (DefaultCapacity - 1) >> 1;
			Logger.DebugLog("Initializing array");
			m_offset = offset - padFront;
			m_array = new T[DefaultCapacity];
		}

		/// <summary>
		/// Expand array to include the specified index.
		/// </summary>
		/// <param name="index">The position in the list.</param>
		private void Include(int index)
		{
			int iIndex = index - m_offset;

			if (iIndex >= 0 && iIndex < m_array.Length)
			{
				Logger.DebugLog("Index is in range");
				return;
			}

			int iFirst, iLast;
			GetFirstAndLast(out iFirst, out iLast);

			if (iFirst < 0)
			{
				Logger.DebugLog("Array is empty, creating one with default capacity");
				InitializeArray(index);
				return;
			}

			int first = iFirst + m_offset, last = iLast + m_offset;

			if (index < first)
				first = index;
			else if (index > last)
				last = index;
			else
				throw new InvalidOperationException("The array was modified");

			int doubleSize = m_array.Length << 1;
			int newSize = last - first + 1;
			if (newSize < doubleSize)
			{
				first -= doubleSize - newSize >> 1;
				newSize = doubleSize;
			}
			int shift = m_offset - first;

			Logger.DebugLog("Copying to a new array. Offset: " + m_offset + ", iFirst: " + iFirst + ", iLast: " + iLast + ", index: " + index + ", new offset: " + first + ", new size: " + newSize + ", shift: " + shift);

			T[] newArray = new T[newSize];

			for (int ii = iFirst; ii <= iLast; ii++)
				newArray[ii + shift] = m_array[ii];

			m_offset = first;
			m_array = newArray;
		}

		/// <summary>
		/// Get the internal index of the first and last non-default elements in the array.
		/// </summary>
		/// <param name="comparer">Comparer to use for equality testing.</param>
		/// <param name="first">Internal index of the first non-default element in the array.</param>
		/// <param name="last">Internal index of the last non-default element in the array.</param>
		/// <returns>True if the array contains at least on non-default element. False, otherwise.</returns>
		private void GetFirstAndLast(out int first, out int last)
		{
			for (first = 0; first < m_array.Length; first++)
			{
				T element = m_array[first];
				if (!EqualsDefault(element))
					goto FindLast;
			}

			first = last = -1;
			return;

		FindLast:

			for (last = m_array.Length - 1; last >= 0; last--)
			{
				T element = m_array[last];
				if (!EqualsDefault(element))
					goto AddOffset;
			}

			throw new InvalidOperationException("The array was modified");

		AddOffset: 
			return;
		}

	}
}
