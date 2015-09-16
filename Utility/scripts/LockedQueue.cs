using System;
using System.Collections.Generic;
using VRage.Collections;

namespace Rynchodon
{
	public class LockedQueue<T>
	{
		private MyQueue<T> Queue;
		private readonly FastResourceLock lock_Queue = new FastResourceLock();

		public LockedQueue(IEnumerable<T> collection)
		{ Queue = new MyQueue<T>(collection); }

		public LockedQueue(int capacity = 1)
		{ Queue = new MyQueue<T>(capacity); }

		public int Count
		{
			get
			{
				using (lock_Queue.AcquireSharedUsing())
					return Queue.Count;
			}
		}
		public T[] DebugItems
		{
			get
			{
				using (lock_Queue.AcquireSharedUsing())
					return Queue.DebugItems;
			}
		}

		public T this[int index]
		{
			get
			{
				using (lock_Queue.AcquireSharedUsing())
					return Queue[index];
			}
			set
			{
				using (lock_Queue.AcquireExclusiveUsing())
					Queue[index] = value;
			}
		}

		public void Clear()
		{
			using (lock_Queue.AcquireExclusiveUsing())
				Queue = new MyQueue<T>(Queue.Count);
		}

		public T Dequeue()
		{
			using (lock_Queue.AcquireExclusiveUsing())
				return Queue.Dequeue();
		}

		public void Enqueue(T item)
		{
			using (lock_Queue.AcquireExclusiveUsing())
				Queue.Enqueue(item);
		}

		public T Peek()
		{
			using (lock_Queue.AcquireSharedUsing())
				return Queue.Peek();
		}

		public void TrimExcess()
		{
			using (lock_Queue.AcquireExclusiveUsing())
				Queue.TrimExcess();
		}

		public void DequeueAll(Action<T> invoke)
		{
			using (lock_Queue.AcquireExclusiveUsing())
				while (Queue.Count > 0)
					invoke(Queue.Dequeue());
		}

		public void ForEach(Action<T> invoke)
		{
			using (lock_Queue.AcquireSharedUsing())
				for (int i = 0; i < Queue.Count; i++)
					invoke(Queue[i]);
		}
	}
}
