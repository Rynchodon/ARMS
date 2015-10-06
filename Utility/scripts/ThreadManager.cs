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
		private const int QueueOverflow = 1000000;

		private static Logger myLogger = new Logger("ThreadManager");

		public byte AllowedParallel { get; private set; }

		public byte parallelTasks { get; private set; }
		private readonly FastResourceLock lock_parallelTasks = new FastResourceLock();

		public readonly bool Background;
	
		private readonly MyQueue<Action> ActionQueue = new MyQueue<Action>(128);
		private readonly FastResourceLock lock_ActionQueue = new FastResourceLock();

		public ThreadManager(byte AllowedParallel = 1, bool background = false)
		{
			this.AllowedParallel = AllowedParallel;
			this.Background = background;
			MyAPIGateway.Entities.OnCloseAll += Entities_OnCloseAll;
		}

		private void Entities_OnCloseAll()
		{
			myLogger.debugLog("stopping thread", "Entities_OnCloseAll()", Logger.severity.INFO);
			using (lock_ActionQueue.AcquireExclusiveUsing())
				ActionQueue.Clear();
		}

		public void EnqueueAction(Action toQueue)
		{
			using (lock_ActionQueue.AcquireExclusiveUsing())
			{
				ActionQueue.Enqueue(toQueue);

				VRage.Exceptions.ThrowIf<OverflowException>(ActionQueue.Count > QueueOverflow, "queue is too long");
			}

			using (lock_parallelTasks.AcquireExclusiveUsing())
			{
				if (parallelTasks >= AllowedParallel)
					return;
				parallelTasks++;
			}

			if (Background)
				MyAPIGateway.Parallel.StartBackground(Run);
			else
				MyAPIGateway.Parallel.Start(Run);
		}

		public void EnqueueIfIdle(Action toQueue)
		{
			bool idle;
			using (lock_ActionQueue.AcquireSharedUsing())
				idle = ActionQueue.Count == 0;

			if (idle)
				EnqueueAction(toQueue);
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
			catch (Exception ex) { myLogger.alwaysLog("Exception: " + ex, "Run()", Logger.severity.ERROR); }
			finally
			{
				using (lock_parallelTasks.AcquireExclusiveUsing())
					parallelTasks--;
			}
		}
	}
}
