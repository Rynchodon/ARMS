using System;
using System.Collections;
using System.Collections.Generic;

namespace Rynchodon.Utility.Collections
{
	public static class LockedDequeExtensions
	{
		/// <summary>
		/// Pop from head and invoke every action in the deque.
		/// </summary>
		public static void PopHeadInvokeAll(this LockedDeque<Action> actions)
		{
			Action current;
			while (actions.TryPopHead(out current))
				current.Invoke();
		}
	}

	public sealed class LockedDeque<T>
	{
		private readonly Deque<T> _deque;
		private readonly FastResourceLock _lock = new FastResourceLock();

		public LockedDeque(int capacity = 1)
		{
			_deque = new Deque<T>(capacity);
		}

		public int Capacity
		{
			get { return _deque.Capacity; }
		}

		public int Count
		{
			get { return _deque.Count; }
		}

		public bool IsReadOnly
		{
			get { return _deque.IsReadOnly; }
		}

		public void AddHead(T item)
		{
			using (_lock.AcquireExclusiveUsing())
				_deque.AddHead(item);
		}

		public void AddHead(ref T item)
		{
			using (_lock.AcquireExclusiveUsing())
				_deque.AddHead(ref item);
		}

		public void AddTail(T item)
		{
			using (_lock.AcquireExclusiveUsing())
				_deque.AddTail(item);
		}

		public void AddTail(ref T item)
		{
			using (_lock.AcquireExclusiveUsing())
				_deque.AddTail(ref item);
		}

		public void Clear()
		{
			using (_lock.AcquireExclusiveUsing())
				_deque.Clear();
		}

		public Enumerator GetEnumerator()
		{
			return new Enumerator(this);
		}

		public void TrimExcess()
		{
			using (_lock.AcquireExclusiveUsing())
				_deque.TrimExcess();
		}

		public bool TryPeekHead(out T item)
		{
			using (_lock.AcquireSharedUsing())
				if (_deque.Count != 0)
				{
					_deque.PeekHead(out item);
					return true;
				}
				else
				{
					item = default(T);
					return false;
				}
		}

		public bool TryPeekTail(out T item)
		{
			using (_lock.AcquireSharedUsing())
				if (_deque.Count != 0)
				{
					_deque.PeekTail(out item);
					return true;
				}
				else
				{
					item = default(T);
					return false;
				}
		}

		public bool TryPopHead(out T item)
		{
			using (_lock.AcquireExclusiveUsing())
				if (_deque.Count != 0)
				{
					_deque.PopHead(out item);
					return true;
				}
				else
				{
					item = default(T);
					return false;
				}
		}

		public bool TryPopTail(out T item)
		{
			using (_lock.AcquireExclusiveUsing())
				if (_deque.Count != 0)
				{
					_deque.PopTail(out item);
					return true;
				}
				else
				{
					item = default(T);
					return false;
				}
		}

		public bool TryRemoveHead()
		{
			using (_lock.AcquireExclusiveUsing())
				if (_deque.Count != 0)
				{
					_deque.RemoveHead();
					return true;
				}
				else
					return false;
		}

		public bool TryRemoveTail()
		{
			using (_lock.AcquireExclusiveUsing())
				if (_deque.Count != 0)
				{
					_deque.RemoveTail();
					return true;
				}
				else
					return false;
		}

		public struct Enumerator : IEnumerator<T>
		{
			private readonly FastResourceLock _lock;
			private Deque<T>.Enumerator _enumerator;

			public Enumerator(LockedDeque<T> lockedDeque)
			{
				_enumerator = lockedDeque._deque.GetEnumerator();
				_lock = lockedDeque._lock;
				_lock.AcquireShared();
			}

			public T Current
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

			public void Reset()
			{
				throw new NotImplementedException();
			}
		}

	}
}
