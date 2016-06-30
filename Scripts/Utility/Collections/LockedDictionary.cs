using System; // (partial) from mscorlib.dll
using System.Collections.Generic;
using VRage;

namespace Rynchodon
{
	public class LockedDictionary<TKey, TValue>
	{
		public readonly Dictionary<TKey, TValue> Dictionary;
		public readonly FastResourceLock lock_Dictionary = new FastResourceLock();

		public LockedDictionary()
		{
			this.Dictionary = new Dictionary<TKey, TValue>();
		}

		public LockedDictionary(Dictionary<TKey, TValue> dictionary)
		{
			this.Dictionary = dictionary;
		}

		public LockedDictionary(IDictionary<TKey, TValue> dictionary)
		{
			this.Dictionary = new Dictionary<TKey, TValue>(dictionary);
		}

		public int Count
		{
			get
			{
				return Dictionary.Count;
			}
		}

		public TValue this[TKey key]
		{
			get
			{
				using (lock_Dictionary.AcquireSharedUsing())
					return Dictionary[key];
			}
			set
			{
				using (lock_Dictionary.AcquireExclusiveUsing())
					Dictionary[key] = value;
			}
		}

		public void Add(TKey key, TValue value)
		{
			using (lock_Dictionary.AcquireExclusiveUsing())
				Dictionary.Add(key, value);
		}

		public void Clear()
		{
			using (lock_Dictionary.AcquireExclusiveUsing())
				Dictionary.Clear();
		}

		public bool ContainsKey(TKey key)
		{
			using (lock_Dictionary.AcquireSharedUsing())
				return Dictionary.ContainsKey(key);
		}

		public bool ContainsValue(TValue value)
		{
			using (lock_Dictionary.AcquireSharedUsing())
				return Dictionary.ContainsValue(value);
		}

		public bool Remove(TKey key)
		{
			using (lock_Dictionary.AcquireExclusiveUsing())
				return Dictionary.Remove(key);
		}

		public bool TryGetValue(TKey key, out TValue value)
		{
			using (lock_Dictionary.AcquireSharedUsing())
				return Dictionary.TryGetValue(key, out value);
		}

		/// <summary>
		/// For setting a value unreliably, if the dictionary does not already contain the specified key.
		/// </summary>
		public bool TrySet(TKey key, TValue value)
		{
			bool contains;
			using (lock_Dictionary.AcquireSharedUsing())
				contains = Dictionary.ContainsKey(key);
			if (!contains)
			{
				using (lock_Dictionary.AcquireExclusiveUsing())
					Dictionary[key] = value;
				return true;
			}
			return false;
		}

		public void ForEach(Func<TKey, bool> function)
		{
			using (lock_Dictionary.AcquireSharedUsing())
				foreach (TKey key in Dictionary.Keys)
					if (function(key))
						return;
		}

		public void ForEach(Func<TValue, bool> function)
		{
			using (lock_Dictionary.AcquireSharedUsing())
				foreach (TValue value in Dictionary.Values)
					if (function(value))
						return;
		}

		public void ForEach(Func<KeyValuePair<TKey, TValue>, bool> function)
		{
			using (lock_Dictionary.AcquireSharedUsing())
				foreach (KeyValuePair<TKey, TValue> pair in Dictionary)
					if (function(pair))
						return;
		}

		public IEnumerable<TKey> KeysEnumerator()
		{
			using (lock_Dictionary.AcquireSharedUsing())
				foreach (TKey key in Dictionary.Keys)
					yield return key;
		}

		public IEnumerable<TValue> ValueEnumerator()
		{
			using (lock_Dictionary.AcquireSharedUsing())
				foreach (TValue value in Dictionary.Values)
					yield return value;
		}

		public IEnumerable<KeyValuePair<TKey, TValue>> GetEnumerator()
		{
			using (lock_Dictionary.AcquireSharedUsing())
				foreach (KeyValuePair<TKey, TValue> pair in Dictionary)
					yield return pair;
		}

	}
}
