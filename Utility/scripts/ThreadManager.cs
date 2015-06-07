#define LOG_ENABLED //remove on build

using Sandbox.ModAPI;
using System;
using System.Collections.Generic;
using VRage;
using VRage.Collections;

namespace Rynchodon
{
	public class ThreadManager
	{
		private MyQueue<Action> ActionQueue = new MyQueue<Action>(128);
		private FastResourceLock lock_ActionQueue = new FastResourceLock();

		public byte AllowedParallel { get; private set; }
		public byte parallelTasks { get; private set; }
		private FastResourceLock lock_parallelTasks = new FastResourceLock();

		public ThreadManager(byte AllowedParallel = 1)
		{
			this.AllowedParallel = AllowedParallel;
		}

		public void EnqueueAction(Action toQueue)
		{
			lock_ActionQueue.AcquireExclusive();
			try { ActionQueue.Enqueue(toQueue); }
			finally { lock_ActionQueue.ReleaseExclusive(); }

			lock_parallelTasks.AcquireExclusive();
			try
			{
				if (parallelTasks >= AllowedParallel)
					return;
				parallelTasks++;
			}
			finally { lock_parallelTasks.ReleaseExclusive(); }

			MyAPIGateway.Parallel.Start(Run);
		}

		private void Run()
		{
			try
			{
				Action currentItem;
				while (true)
				{
					lock_ActionQueue.AcquireExclusive();
					try
					{
						if (ActionQueue.Count == 0)
							return;
						currentItem = ActionQueue.Dequeue();
					}
					finally { lock_ActionQueue.ReleaseExclusive(); }
					currentItem();
				}
			}
			finally
			{
				lock_parallelTasks.AcquireExclusive();
				try
				{ parallelTasks--; }
				finally { lock_parallelTasks.ReleaseExclusive(); }
			}
		}
	}
}
