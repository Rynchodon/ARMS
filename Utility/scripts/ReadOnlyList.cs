using System;
using System.Collections.Generic;
//using System.Linq;
//using System.Text;

using VRage;

namespace Rynchodon
{
	/// <summary>
	/// A replacement for System.Collections.ObjectModel.ReadOnlyCollection<T>, which is blacklisted. Thinnest possible wrapper, generic interfaces only.
	/// </summary>
	public class ReadOnlyList<T> : IList<T>,
	ICollection<T>, IEnumerable<T>
	{
		public int Count { get { return myList.Count; } }
		public T this[int index] { get { return myList[index]; } set { Exceptions.ThrowIf<NotSupportedException>(true); } }

		private List<T> myList;

		public ReadOnlyList(List<T> toWrap)
		{ myList = toWrap; }

		public bool Contains(T value)
		{ return myList.Contains(value); }

		public void CopyTo(T[] array, int index)
		{ myList.CopyTo(array); }

		public IEnumerator<T> GetEnumerator()
		{ return myList.GetEnumerator(); }

		public int IndexOf(T value)
		{ return myList.IndexOf(value); }

		// Implements

		System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator()
		{ return myList.GetEnumerator(); }

		public bool IsReadOnly
		{ get { return true; } }

		// Unsupported Operations

		public void Insert(int index, T item)
		{ Exceptions.ThrowIf<NotSupportedException>(true); }

		public void RemoveAt(int index)
		{ Exceptions.ThrowIf<NotSupportedException>(true); }

		public void Add(T item)
		{ Exceptions.ThrowIf<NotSupportedException>(true); }

		public void Clear()
		{ Exceptions.ThrowIf<NotSupportedException>(true); }

		public bool Remove(T item)
		{
			Exceptions.ThrowIf<NotSupportedException>(true);
			return false;
		}
	}
}
