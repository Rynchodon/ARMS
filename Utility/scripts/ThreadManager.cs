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

		private bool IsRunning = false;

		private Logger myLogger;

		public ThreadManager(string name)
		{
			myLogger = new Logger(null, "ThreadManager-" + name);
		}

		public void EnqueueAction(Action toQueue)
		{
			using (lock_ActionQueue.AcquireExclusiveUsing())
				ActionQueue.Enqueue(toQueue);
			
			// this section is safe so long as there is only one thread that calls this method
			if (IsRunning)
				return;
			IsRunning = true;

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
			finally { IsRunning = false; }
		}
	}
}
