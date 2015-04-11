using System;
using System.Collections.Generic;

namespace Rynchodon
{
	/// <summary>
	/// Creates immutable copies of a list, while minimizing copy all operations. Not synchronized.
	/// </summary>
	/// <typeparam name="T">The type of elements in the list.</typeparam>
	public class ListCacher<T>
	{
		private List<T> mutableList;
		private ReadOnlyList<T> immutableList;

		/// <summary>
		/// Initializes with a List&lt;T&gt; with the default capacity.
		/// </summary>
		public ListCacher() { mutableList = new List<T>(); }

		/// <summary>
		/// Initializes with a List&lt;T&gt; with elements copied from the collection.
		/// </summary>
		/// <param name="collection">To copy elements from</param>
		public ListCacher(IEnumerable<T> collection) { mutableList = new List<T>(collection); }

		/// <summary>
		/// <para>Get a mutable copy of the list</para>
		/// <para>Will trigger a copy all operation if immutable() has been called since creation or the last call to mutable().</para>
		/// </summary>
		/// <returns>Mutable copy of the list</returns>
		public List<T> mutable()
		{
			if (immutableList != null)
			{
				immutableList = null;
				mutableList = new List<T>(mutableList);
			}

			return mutableList;
		}

		/// <summary>
		/// Get an immutable copy of the list
		/// </summary>
		/// <returns>Immutable copy of the list</returns>
		public ReadOnlyList<T> immutable()
		{
			if (immutableList == null)
				immutableList = new ReadOnlyList<T>(mutableList);

			return immutableList;
		}
	}
}