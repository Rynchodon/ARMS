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
			using (lock_ActionQueue.AcquireExclusiveUsing())
				ActionQueue.Enqueue(toQueue);

			using (lock_parallelTasks.AcquireExclusiveUsing())
			{
				if (parallelTasks >= AllowedParallel)
					return;
				parallelTasks++;
			}

			MyAPIGateway.Parallel.Start(Run);
		}

		private void Run()
		{
			try
			{
				Action currentItem;
				while (true)
				{
					using (lock_ActionQueue.AcquireExclusiveUsing())
					{
						if (ActionQueue.Count == 0)
							return;
						currentItem = ActionQueue.Dequeue();
					}
					currentItem();
				}
			}
			finally
			{
				using (lock_parallelTasks.AcquireExclusiveUsing())
					parallelTasks--;
			}
		}
	}
}
