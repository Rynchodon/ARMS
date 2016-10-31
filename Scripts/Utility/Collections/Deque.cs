using System;
using System.Collections.Generic;

namespace Rynchodon.Utility.Collections
{
	public class Deque<T> : IList<T>
	{

		public const int DefaultCapacity = 4;

		private T[] _array;
		private int _head, _tail;

		public Deque(int capacity = DefaultCapacity)
		{
			_array = new T[capacity];
		}

		public Deque(ICollection<T> collection)
		{
			Count = collection.Count;
			_tail = 0;
			_array = new T[Count];
			collection.CopyTo(_array, 0);
		}

		#region Defined Members

		public int Capacity { get { return _array.Length; } }

		public void AddHead(T item)
		{
			if (_array.Length < Count + 1)
				Resize();

			_head = (_head - 1 + _array.Length) % _array.Length;
			_array[_head] = item;
			Count++;
		}

		public void AddHead(ref T item)
		{
			if (_array.Length < Count + 1)
				Resize();

			_head = (_head - 1 + _array.Length) % _array.Length;
			_array[_head] = item;
			Count++;
		}

		public void AddTail(T item)
		{
			if (_array.Length < Count + 1)
				Resize();

			_array[_tail] = item;
			_tail = (_tail + 1) % _array.Length;
			Count++;
		}

		public void AddTail(ref T item)
		{
			if (_array.Length < Count + 1)
				Resize();

			_array[_tail] = item;
			_tail = (_tail + 1) % _array.Length;
			Count++;
		}

		public T PeekHead()
		{
			Logger.DebugLog("Empty", Logger.severity.ERROR, condition: Count == 0);
			return _array[_head];
		}

		public void PeekHead(out T item)
		{
			Logger.DebugLog("Empty", Logger.severity.ERROR, condition: Count == 0);
			item = _array[_head];
		}

		public T PeekTail()
		{
			Logger.DebugLog("Empty", Logger.severity.ERROR, condition: Count == 0);
			return _array[(_tail - 1 + _array.Length) % _array.Length];
		}

		public void PeekTail(out T item)
		{
			Logger.DebugLog("Empty", Logger.severity.ERROR, condition: Count == 0);
			item = _array[(_tail - 1 + _array.Length) % _array.Length];
		}

		public T PopHead()
		{
			Logger.DebugLog("Empty", Logger.severity.ERROR, condition: Count == 0);

			T result = _array[_head];
			_array[_head] = default(T);
			_head = (_head + 1) % _array.Length;
			Count--;
			return result;
		}

		public void PopHead(out T item)
		{
			Logger.DebugLog("Empty", Logger.severity.ERROR, condition: Count == 0);

			item = _array[_head];
			_array[_head] = default(T);
			_head = (_head + 1) % _array.Length;
			Count--;
		}

		public T PopTail()
		{
			Logger.DebugLog("Empty", Logger.severity.ERROR, condition: Count == 0);

			_tail = (_tail - 1 + _array.Length) % _array.Length;
			T result = _array[_tail];
			_array[_tail] = default(T);
			Count--;
			return result;
		}

		public void PopTail(out T item)
		{
			Logger.DebugLog("Empty", Logger.severity.ERROR, condition: Count == 0);

			_tail = (_tail - 1 + _array.Length) % _array.Length;
			item = _array[_tail];
			_array[_tail] = default(T);
			Count--;
		}

		public void RemoveHead()
		{
			Logger.DebugLog("Empty", Logger.severity.ERROR, condition: Count == 0);

			_array[_head] = default(T);
			_head = (_head + 1) % _array.Length;
			Count--;
		}

		public void RemoveTail()
		{
			Logger.DebugLog("Empty", Logger.severity.ERROR, condition: Count == 0);

			_tail = (_tail - 1 + _array.Length) % _array.Length;
			_array[_tail] = default(T);
			Count--;
		}

		public void TrimExcess()
		{
			if (Count > _array.Length * 0.9)
				return;
			Resize(Count);
		}

		/// <summary>
		/// Resize the internal array, if capacity == -1, double the size.
		/// </summary>
		private void Resize(int capacity = -1)
		{
			Logger.DebugLog("Empty", Logger.severity.ERROR, condition: Count == 0);

			if (capacity == -1)
				capacity = _array.Length << 1;
			T[] newArray = new T[capacity];

			int newArrayIndex = 0;
			if (_head < _tail)
			{
				for (int index = _head; index < _tail; index++)
					newArray[newArrayIndex++] = _array[index];
			}
			else
			{
				for (int index = _head; index < _array.Length; index++)
					newArray[newArrayIndex++] = _array[index];
				for (int index = 0; index < _tail; index++)
					newArray[newArrayIndex++] = _array[index];
			}

			_array = newArray;
			_head = 0;
			_tail = Count % _array.Length;
		}

		/// <summary>
		/// Shift every item between inIndex and _tail, exclusive, towards head.
		/// </summary>
		private void Remove(int inIndex)
		{
			Logger.DebugLog("Empty", Logger.severity.ERROR, condition: Count == 0);

			if (_head < _tail)
			{
				_tail--;
				for (int index = inIndex; index < _tail; index++)
					_array[index] = _array[index + 1];
			}
			else
			{
				for (int index = inIndex; index < _array.Length - 1; index++)
					_array[index] = _array[index + 1];
				if (_tail == 0)
				{
					_tail = _array.Length - 1;
					_array[_tail] = default(T);
				}
				else
				{
					_tail--;
					_array[_array.Length - 1] = _array[0];
					for (int index = 0; index < _tail; index++)
						_array[index] = _array[index + 1];
					_array[_tail] = default(T);
				}
			}
			Count--;
		}

		#endregion

		public int IndexOf(T item)
		{
			if (Count == 0)
				return -1;
			EqualityComparer<T> comparer = EqualityComparer<T>.Default;

			int extIndex = 0;
			if (_head < _tail)
			{
				for (int index = _head; index < _tail; index++)
				{
					if (comparer.Equals(_array[index], item))
						return extIndex;
					extIndex++;
				}
			}
			else
			{
				for (int index = _head; index < _array.Length; index++)
				{
					if (comparer.Equals(_array[index], item))
						return extIndex;
					extIndex++;
				}
				for (int index = 0; index < _tail; index++)
				{
					if (comparer.Equals(_array[index], item))
						return extIndex;
					extIndex++;
				}
			}
			return -1;
		}

		public void Insert(int index, T item)
		{
			if (index < 0 || Count < index) // can insert at Count
				throw new ArgumentOutOfRangeException("index", "index: " + index + ", Count: " + Count);

			int insertIndex = (index + _head) % _array.Length;
			if (Count == 0)
			{
				AddTail(ref item);
				return;
			}
			if (_array.Length < Count + 1)
				Resize();

			if (_head < _tail)
			{
				for (index = insertIndex; index < _tail; index++)
					_array[index + 1] = _array[index];
			}
			else
			{
				for (index = insertIndex; index < _array.Length - 1; index++)
					_array[index + 1] = _array[index];
				_array[0] = _array[_array.Length - 1];
				for (index = 0; index < _tail; index++)
					_array[index + 1] = _array[index];
			}

			_tail = (_tail + 1) % _array.Length;
			_array[insertIndex] = item;
			Count++;
		}

		public void RemoveAt(int index)
		{
			if (index < 0 || Count <= index)
				throw new ArgumentOutOfRangeException("index", "index: " + index + ", Count: " + Count);

			int inIndex = (index + _head) % _array.Length;
			Remove(inIndex);
		}

		public T this[int index]
		{
			get
			{
				if (index < 0 || Count <= index)
					throw new ArgumentOutOfRangeException("index", "index: " + index + ", Count: " + Count);

				int inIndex = (index + _head) % _array.Length;
				return _array[inIndex];
			}
			set
			{
				if (index < 0 || Count <= index)
					throw new ArgumentOutOfRangeException("index", "index: " + index + ", Count: " + Count);

				int inIndex = (index + _head) % _array.Length;
				_array[inIndex] = value;
			}
		}

		void ICollection<T>.Add(T item)
		{
			AddTail(ref item);
		}

		public void Clear()
		{
			if (Count == 0)
				return;

			if (_head < _tail)
			{
				for (int index = _head; index < _tail; index++)
					_array[index] = default(T);
			}
			else
			{
				for (int index = _head; index < _array.Length; index++)
					_array[index] = default(T);
				for (int index = 0; index < _tail; index++)
					_array[index] = default(T);
			}
			_head = _tail = Count = 0;
		}

		public bool Contains(T item)
		{
			return IndexOf(item) != -1;
		}

		public void CopyTo(T[] array, int arrayIndex)
		{
			if (Count == 0)
				return;

			int otherArrayIndex = 0;
			if (_head < _tail)
			{
				for (int index = _head; index < _tail; index++)
					array[otherArrayIndex++] = _array[index];
			}
			else
			{
				for (int index = _head; index < _array.Length; index++)
					array[otherArrayIndex++] = _array[index];
				for (int index = 0; index < _tail; index++)
					array[otherArrayIndex++] = _array[index];
			}
		}

		public int Count { get; private set; }

		public bool IsReadOnly
		{
			get { return false; }
		}

		public bool Remove(T item)
		{
			if (Count == 0)
				return false;

			EqualityComparer<T> comparer = EqualityComparer<T>.Default;

			if (_head < _tail)
			{
				for (int index = _head; index < _tail; index++)
					if (comparer.Equals(_array[index], item))
					{
						Remove(index);
						return true;
					}
			}
			else
			{
				for (int index = _head; index < _array.Length; index++)
					if (comparer.Equals(_array[index], item))
					{
						Remove(index);
						return true;
					}
				for (int index = 0; index < _tail; index++)
					if (comparer.Equals(_array[index], item))
					{
						Remove(index);
						return true;
					}
			}
				

			return false;
		}

		public IEnumerator<T> GetEnumerator()
		{
			if (Count == 0)
				yield break;

			if (_head < _tail)
			{
				for (int index = _head; index < _tail; index++)
					yield return _array[index];
			}
			else
			{
				for (int index = _head; index < _array.Length; index++)
					yield return _array[index];
				for (int index = 0; index < _tail; index++)
					yield return _array[index];
			}
		}

		System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator()
		{
			return GetEnumerator();
		}

	}
}
