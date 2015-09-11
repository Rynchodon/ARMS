using System.Collections.Generic;
using System.Linq;

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
		/// <para>Adds an element to the SortedDictionary. If NumberToKeep is exceeded, elements will be removed.</para>
		/// <para>If the key cannot be added, it will be incremented until it can be.</para>
		/// </summary>
		/// <param name="NumberToKeep">Maximum number of elements in the SortedDictionary</param>
		public static void AddIfBetter<TValue>(this SortedDictionary<float, TValue> sorted, float key, TValue value, int NumberToKeep)
		{
			while (sorted.ContainsKey(key))
				key.IncrementSignificand();

			sorted.Add(key, value);

			while (sorted.Count > NumberToKeep)
				sorted.Remove(sorted.Keys.Last());
		}

	}
}
