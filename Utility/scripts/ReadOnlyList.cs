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
	public class ReadOnlyList<T> : IList<T>,
	ICollection<T>, IEnumerable<T>
	{
		public int Count { get { return myList.Count; } }
		public T this[int index] { get { return myList[index]; } set { Exceptions.ThrowIf<NotSupportedException>(IsReadOnly, "object is readonly"); } }

		private List<T> myList;

		/// <summary>
		/// Creates an initially writable ReadOnlyList, use set_ReadOnly to make it read only.
		/// </summary>
		/// <returns>A writable ReadOnlyList!</returns>
		public static ReadOnlyList<T> create_Writable()
		{
			ReadOnlyList<T> newList = new ReadOnlyList<T>();
			newList.value_IsReadOnly = false;
			return newList;
		}

		/// <summary>
		/// Creates an initially writable ReadOnlyList, use set_ReadOnly to make it read only.
		/// </summary>
		/// <param name="initialCapacity">Initial capacity of the list.</param>
		/// <returns>A writable ReadOnlyList!</returns>
		public static ReadOnlyList<T> create_Writable(int initialCapacity)
		{
			ReadOnlyList<T> newList = new ReadOnlyList<T>(initialCapacity);
			newList.value_IsReadOnly = false;
			return newList;
		}

		/// <summary>
		/// Creates an initially writable ReadOnlyList, use set_ReadOnly to make it read only.
		/// </summary>
		/// <param name="copyFrom">Copy all the elements from</param>
		/// <returns>A writable ReadOnlyList!</returns>
		public static ReadOnlyList<T> create_Writable(IEnumerable<T> copyFrom)
		{
			ReadOnlyList<T> newList = new ReadOnlyList<T>(copyFrom);
			newList.value_IsReadOnly = false;
			return newList;
		}

		public ReadOnlyList() 
		{ myList = new List<T>(); }

		public ReadOnlyList(int initialCapacity) 
		{ myList = new List<T>(initialCapacity); }

		public ReadOnlyList(IEnumerable<T> copyFrom)
		{ myList = new List<T>(copyFrom); }

		//public ReadOnlyList(List<T> toWrap)
		//{ myList = toWrap; }

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

		private bool value_IsReadOnly = true;
		public bool IsReadOnly { get { return value_IsReadOnly; } }

		/// <summary>
		/// Irreversible. If this is writable, make it read-only.
		/// </summary>
		public void set_ReadOnly()
		{ value_IsReadOnly = true; }

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
			myList = new List<T>(myList.Count);
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
