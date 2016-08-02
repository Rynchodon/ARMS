using System;
using System.Collections.Generic;
using VRage;

namespace Rynchodon
{
	/// <summary>
	/// <para>A replacement for System.Collections.ObjectModel.ReadOnlyCollection&lt;T&gt;, which is blacklisted.</para>
	/// <para>Not synchronized, generic interfaces only.</para>
	/// </summary>
	/// <remarks>
	/// A ReadOnlyList may be writable, until it is made read-only.
	/// </remarks>
	public class ReadOnlyList<T> : IList<T>, ICollection<T>, IEnumerable<T>
	{
		private bool value_IsReadOnly;
		private readonly List<T> myList;

		public int Count { get { return myList.Count; } }

		public T this[int index]
		{
			get { return myList[index]; }
			set
			{
				Exceptions.ThrowIf<NotSupportedException>(IsReadOnly, "object is readonly");
				myList[index] = value;
			}
		}

		public bool IsReadOnly
		{
			get { return value_IsReadOnly; }
			set
			{
				Exceptions.ThrowIf<NotSupportedException>(!value, "cannot make object writable");
				value_IsReadOnly = true;
			}
		}

		public ReadOnlyList(bool readOnly = true)
		{
			myList = new List<T>();
			value_IsReadOnly = readOnly;
		}

		public ReadOnlyList(int initialCapacity, bool readOnly = true)
		{
			myList = new List<T>(initialCapacity);
			value_IsReadOnly = readOnly;
		}

		public ReadOnlyList(IEnumerable<T> copyFrom, bool readOnly = true)
		{
			myList = new List<T>(copyFrom);
			value_IsReadOnly = readOnly;
		}

		public ReadOnlyList<T> Mutable()
		{
			if (value_IsReadOnly)
				return new ReadOnlyList<T>(this, false);
			return this;
		}

		public ReadOnlyList<T> Immutable()
		{
			value_IsReadOnly = true;
			return this;
		}

		public bool Contains(T value)
		{ return myList.Contains(value); }

		public void CopyTo(T[] array, int index)
		{ myList.CopyTo(array); }

		public IEnumerator<T> GetEnumerator()
		{ return myList.GetEnumerator(); }

		public int IndexOf(T value)
		{ return myList.IndexOf(value); }

		System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator()
		{ return myList.GetEnumerator(); }

		#region Write Operations

		public void Insert(int index, T item)
		{
			Exceptions.ThrowIf<NotSupportedException>(IsReadOnly, "object is readonly");
			myList.Insert(index, item);
		}

		public void RemoveAt(int index)
		{
			Exceptions.ThrowIf<NotSupportedException>(IsReadOnly, "object is readonly");
			myList.RemoveAt(index);
		}

		public void Add(T item)
		{
			Exceptions.ThrowIf<NotSupportedException>(IsReadOnly, "object is readonly");
			myList.Add(item);
		}

		public void Clear()
		{
			Exceptions.ThrowIf<NotSupportedException>(IsReadOnly, "object is readonly");
			myList.Clear();
		}

		public bool Remove(T item)
		{
			Exceptions.ThrowIf<NotSupportedException>(IsReadOnly, "object is readonly");
			return (myList.Remove(item));
		}

		#region Sort
		public void Sort()
		{
			Exceptions.ThrowIf<NotSupportedException>(IsReadOnly, "object is readonly");
			myList.Sort();
		}

		public void Sort(Comparison<T> comparison)
		{
			Exceptions.ThrowIf<NotSupportedException>(IsReadOnly, "object is readonly");
			myList.Sort(comparison);
		}

		public void Sort(IComparer<T> comparer)
		{
			Exceptions.ThrowIf<NotSupportedException>(IsReadOnly, "object is readonly");
			myList.Sort(comparer);
		}

		public void Sort(int index, int count, IComparer<T> comparer)
		{
			Exceptions.ThrowIf<NotSupportedException>(IsReadOnly, "object is readonly");
			myList.Sort(index, count, comparer);
		}
		#endregion

		#endregion
	}
}
