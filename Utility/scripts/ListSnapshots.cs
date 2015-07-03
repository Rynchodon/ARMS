using System;
using System.Collections.Generic;

namespace Rynchodon
{
	/// <summary>
	/// Creates immutable copies of a list, while minimizing copy all operations. Not synchronized.
	/// </summary>
	/// <typeparam name="T">The type of elements in the list.</typeparam>
	public class ListSnapshots<T>
	{
		private ReadOnlyList<T> myList;

		/// <summary>
		/// Initializes with a list with the default capacity.
		/// </summary>
		public ListSnapshots() { myList = ReadOnlyList<T>.create_Writable(); }

		/// <summary>
		/// Initializes with a list with the specified capacity.
		/// </summary>
		/// <param name="initialCapacity"></param>
		public ListSnapshots(int initialCapacity) { myList = ReadOnlyList<T>.create_Writable(initialCapacity); }

		/// <summary>
		/// Initializes with a list with elements copied from the collection.
		/// </summary>
		/// <param name="collection">To copy elements from</param>
		public ListSnapshots(IEnumerable<T> collection) { myList = ReadOnlyList<T>.create_Writable(collection); }

		/// <summary>
		/// <para>Get a mutable copy of the list</para>
		/// <para>Will trigger a copy all operation if immutable() has been called since creation or the last call to mutable().</para>
		/// </summary>
		/// <returns>Mutable copy of the list</returns>
		public ReadOnlyList<T> mutable()
		{
			if (myList.IsReadOnly)
				myList = ReadOnlyList<T>.create_Writable(myList);

			return myList;
		}

		/// <summary>
		/// Get an immutable copy of the list
		/// </summary>
		/// <returns>Immutable copy of the list</returns>
		public ReadOnlyList<T> immutable()
		{
			myList.set_ReadOnly();
			return myList;
		}

		/// <summary>
		/// Safe and fast Count
		/// </summary>
		/// <returns>The number of items in the list</returns>
		public int Count
		{ get { return myList.Count; } }
	}
}