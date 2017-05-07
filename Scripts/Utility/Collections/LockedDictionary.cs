using System; // (partial) from mscorlib.dll
using System.Collections;
using System.Collections.Generic;
using VRage;

namespace Rynchodon
{
	/// <summary>
	/// Dictionary with a <see cref="FastResourceLock"/>.
	/// </summary>
	public sealed class LockedDictionary<TKey, TValue> : IDictionary<TKey, TValue>
	{
		public readonly Dictionary<TKey, TValue> Dictionary;
		public readonly FastResourceLock Lock = new FastResourceLock();

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
			get { return Dictionary.Count; }
		}

		public KeyCollection Keys
		{
			get { return new KeyCollection(this); }
		}

		public ValueCollection Values
		{
			get { return new ValueCollection(this); }
		}

		bool ICollection<KeyValuePair<TKey, TValue>>.IsReadOnly
		{
			get { return ((ICollection<KeyValuePair<TKey, TValue>>)Dictionary).IsReadOnly; }
		}

		ICollection<TKey> IDictionary<TKey, TValue>.Keys
		{
			get { return Keys; }
		}

		ICollection<TValue> IDictionary<TKey, TValue>.Values
		{
			get { return Values; }
		}

		public TValue this[TKey key]
		{
			get
			{
				using (Lock.AcquireSharedUsing())
					return Dictionary[key];
			}
			set
			{
				using (Lock.AcquireExclusiveUsing())
					Dictionary[key] = value;
			}
		}

		public void Add(KeyValuePair<TKey, TValue> pair)
		{
			Add(pair.Key, pair.Value);
		}

		public void Add(TKey key, TValue value)
		{
			using (Lock.AcquireExclusiveUsing())
				Dictionary.Add(key, value);
		}

		public void Clear()
		{
			using (Lock.AcquireExclusiveUsing())
				Dictionary.Clear();
		}

		public bool ContainsKey(TKey key)
		{
			using (Lock.AcquireSharedUsing())
				return Dictionary.ContainsKey(key);
		}

		public bool ContainsValue(TValue value)
		{
			using (Lock.AcquireSharedUsing())
				return Dictionary.ContainsValue(value);
		}

		public Enumerator GetEnumerator()
		{
			return new Enumerator(this);
		}

		public bool Remove(TKey key)
		{
			using (Lock.AcquireExclusiveUsing())
				return Dictionary.Remove(key);
		}

		public bool TryGetValue(TKey key, out TValue value)
		{
			using (Lock.AcquireSharedUsing())
				return Dictionary.TryGetValue(key, out value);
		}

		/// <summary>
		/// For setting a value unreliably, if the dictionary does not already contain the specified key.
		/// </summary>
		public bool TrySet(TKey key, TValue value)
		{
			bool contains;
			using (Lock.AcquireSharedUsing())
				contains = Dictionary.ContainsKey(key);
			if (!contains)
			{
				using (Lock.AcquireExclusiveUsing())
					Dictionary[key] = value;
				return true;
			}
			return false;
		}

		bool ICollection<KeyValuePair<TKey, TValue>>.Contains(KeyValuePair<TKey, TValue> item)
		{
			using (Lock.AcquireSharedUsing())
				return ((ICollection<KeyValuePair<TKey, TValue>>)Dictionary).Contains(item);
		}

		void ICollection<KeyValuePair<TKey, TValue>>.CopyTo(KeyValuePair<TKey, TValue>[] array, int arrayIndex)
		{
			using (Lock.AcquireSharedUsing())
				((ICollection<KeyValuePair<TKey, TValue>>)Dictionary).CopyTo(array, arrayIndex);
		}

		bool ICollection<KeyValuePair<TKey, TValue>>.Remove(KeyValuePair<TKey, TValue> item)
		{
			using (Lock.AcquireExclusiveUsing())
				return ((ICollection<KeyValuePair<TKey, TValue>>)Dictionary).Remove(item);
		}

		IEnumerator<KeyValuePair<TKey, TValue>> IEnumerable<KeyValuePair<TKey, TValue>>.GetEnumerator()
		{
			return GetEnumerator();
		}

		IEnumerator IEnumerable.GetEnumerator()
		{
			return GetEnumerator();
		}

		public struct Reader : IEnumerable<KeyValuePair<TKey, TValue>>
		{
			private readonly LockedDictionary<TKey, TValue> _dictionary;

			public Reader(LockedDictionary<TKey, TValue> dictionary)
			{
				_dictionary = dictionary;
			}

			public Enumerator GetEnumerator()
			{
				return new Enumerator(_dictionary);
			}

			IEnumerator IEnumerable.GetEnumerator()
			{
				return GetEnumerator();
			}

			IEnumerator<KeyValuePair<TKey, TValue>> IEnumerable<KeyValuePair<TKey, TValue>>.GetEnumerator()
			{
				return GetEnumerator();
			}
		}

		public struct Enumerator : IEnumerator<KeyValuePair<TKey, TValue>>
		{
			private readonly LockedDictionary<TKey, TValue> _dictionary;
			private Dictionary<TKey, TValue>.Enumerator _enumerator;

			public Enumerator(LockedDictionary<TKey, TValue> dictionary)
			{
				_dictionary = dictionary;
				_enumerator = dictionary.Dictionary.GetEnumerator();
				_dictionary.Lock.AcquireShared();
			}

			public KeyValuePair<TKey, TValue> Current
			{
				get { return _enumerator.Current; }
			}

			object IEnumerator.Current
			{
				get { return Current; }
			}

			public void Dispose()
			{
				_dictionary.Lock.ReleaseShared();
				_enumerator.Dispose();
			}

			public bool MoveNext()
			{
				return _enumerator.MoveNext();
			}

			void IEnumerator.Reset()
			{
				throw new NotSupportedException();
			}
		}

		public struct KeyCollection : ICollection<TKey>
		{
			private readonly LockedDictionary<TKey, TValue> _lockedDictionary;

			public KeyCollection(LockedDictionary<TKey, TValue> dictionary)
			{
				_lockedDictionary = dictionary;
			}

			public int Count
			{
				get { return _lockedDictionary.Count; }
			}

			public bool IsReadOnly
			{
				get { return true; }
			}

			void ICollection<TKey>.Add(TKey item)
			{
				throw new NotSupportedException();
			}

			void ICollection<TKey>.Clear()
			{
				throw new NotSupportedException();
			}

			public bool Contains(TKey item)
			{
				return _lockedDictionary.ContainsKey(item);
			}

			public void CopyTo(TKey[] array, int arrayIndex)
			{
				using (_lockedDictionary.Lock.AcquireSharedUsing())
					_lockedDictionary.Dictionary.Keys.CopyTo(array, arrayIndex);
			}

			public Enumerator GetEnumerator()
			{
				return new Enumerator(_lockedDictionary);
			}

			bool ICollection<TKey>.Remove(TKey item)
			{
				throw new NotSupportedException();
			}

			IEnumerator<TKey> IEnumerable<TKey>.GetEnumerator()
			{
				return GetEnumerator();
			}

			IEnumerator IEnumerable.GetEnumerator()
			{
				return GetEnumerator();
			}

			public struct Enumerator : IEnumerator<TKey>
			{
				private readonly FastResourceLock _lock;
				private Dictionary<TKey, TValue>.KeyCollection.Enumerator _enumerator;

				public Enumerator(LockedDictionary<TKey, TValue> dictionary)
				{
					_enumerator = dictionary.Dictionary.Keys.GetEnumerator();
					_lock = dictionary.Lock;
					_lock.AcquireShared();
				}

				public TKey Current
				{
					get { return _enumerator.Current; }
				}

				object IEnumerator.Current
				{
					get { return Current; }
				}

				public void Dispose()
				{
					_lock.ReleaseShared();
					_enumerator.Dispose();
				}

				public bool MoveNext()
				{
					return _enumerator.MoveNext();
				}

				void IEnumerator.Reset()
				{
					throw new NotImplementedException();
				}
			}
		}

		public struct ValueCollection : ICollection<TValue>
		{
			private readonly LockedDictionary<TKey, TValue> _lockedDictionary;

			public ValueCollection(LockedDictionary<TKey, TValue> dictionary)
			{
				_lockedDictionary = dictionary;
			}

			public int Count
			{
				get { return _lockedDictionary.Count; }
			}

			public bool IsReadOnly
			{
				get { return true; }
			}

			void ICollection<TValue>.Add(TValue item)
			{
				throw new NotSupportedException();
			}

			void ICollection<TValue>.Clear()
			{
				throw new NotSupportedException();
			}

			public bool Contains(TValue item)
			{
				return _lockedDictionary.ContainsValue(item);
			}

			public void CopyTo(TValue[] array, int arrayIndex)
			{
				using (_lockedDictionary.Lock.AcquireSharedUsing())
					_lockedDictionary.Dictionary.Values.CopyTo(array, arrayIndex);
			}

			public Enumerator GetEnumerator()
			{
				return new Enumerator(_lockedDictionary);
			}

			bool ICollection<TValue>.Remove(TValue item)
			{
				throw new NotSupportedException();
			}

			IEnumerator<TValue> IEnumerable<TValue>.GetEnumerator()
			{
				return GetEnumerator();
			}

			IEnumerator IEnumerable.GetEnumerator()
			{
				return GetEnumerator();
			}

			public struct Enumerator : IEnumerator<TValue>
			{
				private readonly FastResourceLock _lock;
				private Dictionary<TKey, TValue>.ValueCollection.Enumerator _enumerator;

				public Enumerator(LockedDictionary<TKey, TValue> dictionary)
				{
					_enumerator = dictionary.Dictionary.Values.GetEnumerator();
					_lock = dictionary.Lock;
					_lock.AcquireShared();
				}

				public TValue Current
				{
					get { return _enumerator.Current; }
				}

				object IEnumerator.Current
				{
					get { return Current; }
				}

				public void Dispose()
				{
					_lock.ReleaseShared();
					_enumerator.Dispose();
				}

				public bool MoveNext()
				{
					return _enumerator.MoveNext();
				}

				void IEnumerator.Reset()
				{
					throw new NotImplementedException();
				}
			}
		}

	}
}
