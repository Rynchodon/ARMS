using Sandbox.ModAPI;
using System;
using System.Collections.Generic;
using VRage;
using VRage.Collections;

namespace Rynchodon.Threading
{
	public class ThreadManager
	{

		private const int QueueOverflow = 1000000;

		private readonly Logger myLogger = new Logger("ThreadManager");

		private readonly bool Background;
		private readonly string ThreadName;

		private readonly FastResourceLock lock_parallelTasks = new FastResourceLock();

		private LockedQueue<Action> ActionQueue = new LockedQueue<Action>(8);

		public readonly byte AllowedParallel;

		public byte ParallelTasks { get; private set; }

		public ThreadManager(byte AllowedParallel = 1, bool background = false, string threadName = null)
		{
			this.myLogger = new Logger("ThreadManager", () => threadName ?? string.Empty, () => ParallelTasks.ToString());
			this.AllowedParallel = AllowedParallel;
			this.Background = background;
			this.ThreadName = threadName;

			MyAPIGateway.Entities.OnCloseAll += Entities_OnCloseAll;
		}

		private void Entities_OnCloseAll()
		{
			MyAPIGateway.Entities.OnCloseAll -= Entities_OnCloseAll;
			ActionQueue.Clear();
		}

		public void EnqueueAction(Action toQueue)
		{
			myLogger.debugLog("item count: " + ActionQueue.Count, "EnqueueAction()");
			ActionQueue.Enqueue(toQueue);
			myLogger.debugLog("item count: " + ActionQueue.Count, "EnqueueAction()");
			VRage.Exceptions.ThrowIf<Exception>(ActionQueue.Count > QueueOverflow, "queue is too long");

			using (lock_parallelTasks.AcquireExclusiveUsing())
			{
				if (ParallelTasks >= AllowedParallel)
					return;
				ParallelTasks++;
			}

			MyAPIGateway.Utilities.InvokeOnGameThread(() => {
				if (MyAPIGateway.Parallel == null)
				{
					myLogger.debugLog("Parallel == null", "EnqueueAction()", Logger.severity.WARNING);
					return;
				}

				if (Background)
					MyAPIGateway.Parallel.StartBackground(Run);
				else
					MyAPIGateway.Parallel.Start(Run);
			});
		}

		public void EnqueueIfIdle(Action toQueue)
		{
			bool idle = ActionQueue.Count == 0;

			if (idle)
				EnqueueAction(toQueue);
		}

		private void Run()
		{
			try
			{
				if (ThreadName != null)
					ThreadTracker.ThreadName = ThreadName + '(' + ThreadTracker.ThreadNumber + ')';
				Action currentItem;
				while (ActionQueue.TryDequeue(out currentItem))
					if (currentItem != null)
					{
						myLogger.debugLog("running action", "Run()", Logger.severity.TRACE);
						currentItem();
					}
					else
						myLogger.debugLog("null action", "Run()", Logger.severity.WARNING);
				myLogger.debugLog("queue finished", "Run()");
			}
			catch (Exception ex) { myLogger.alwaysLog("Exception: " + ex, "Run()", Logger.severity.ERROR); }
			finally
			{
				using (lock_parallelTasks.AcquireExclusiveUsing())
					ParallelTasks--;
				ThreadTracker.ThreadName = null;
			}
		}

	}
}
