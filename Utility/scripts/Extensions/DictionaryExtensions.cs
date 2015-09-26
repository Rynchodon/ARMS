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

	}
}
