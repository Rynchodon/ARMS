using System.Collections.Generic;

namespace Rynchodon
{
	public static class DictionaryExtensions
	{

		/// <summary>
		/// If the key cannot be added, it will be incremented until it can be.
		/// </summary>
		public static void AddIncrement<TValue>(this Dictionary<float, TValue> dictionary, float key, TValue value)
		{
			while (dictionary.ContainsKey(key))
				key.IncrementSignificand();

			dictionary.Add(key, value);
		}

		/// <summary>
		/// If the key cannot be added, it will be incremented until it can be.
		/// </summary>
		public static void AddIncrement<TValue>(this SortedDictionary<float, TValue> dictionary, float key, TValue value)
		{
			while (dictionary.ContainsKey(key))
				key.IncrementSignificand();

			dictionary.Add(key, value);
		}

		/// <summary>
		/// Add an item to a collection in a dictionary, creating a new collection if one is not present.
		/// </summary>
		/// <typeparam name="TDKey">TKey of the dictionary.</typeparam>
		/// <typeparam name="TDValue">TValue of the dictionary</typeparam>
		/// <typeparam name="TCValue">T of TValue.</typeparam>
		/// <param name="dictionary">The dictionary that contains collections.</param>
		/// <param name="key">The key of the collection.</param>
		/// <param name="value">The value to add to the collection.</param>
		public static void Add<TDKey, TDValue, TCValue>(this IDictionary<TDKey, TDValue> dictionary, TDKey key, TCValue value) where TDValue : ICollection<TCValue>, new()
		{
			TDValue collection;
			if (!dictionary.TryGetValue(key, out collection))
			{
				collection = new TDValue();
				dictionary.Add(key, collection);
			}
			collection.Add(value);
		}

		/// <summary>
		/// Get an item from a dictionary or create a new one if it does not exist.
		/// </summary>
		/// <typeparam name="TDKey">TKey of dictionary</typeparam>
		/// <typeparam name="TDValue">TValue of dictionary</typeparam>
		/// <param name="dictionary">The dictionary to get the item from</param>
		/// <param name="key">The key for the dictionary</param>
		/// <returns>The value from the dictionary</returns>
		public static TDValue GetOrAdd<TDKey, TDValue>(this IDictionary<TDKey, TDValue> dictionary, TDKey key) where TDValue : new()
		{
			TDValue value;
			if (dictionary.TryGetValue(key, out value))
				return value;

			value = new TDValue();
			dictionary.Add(key, value);
			return value;
		}

	}
}
