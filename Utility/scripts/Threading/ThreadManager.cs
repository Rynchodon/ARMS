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

		private static readonly Logger myLogger = new Logger("ThreadManager");

		private readonly bool Background;
		private readonly string ThreadName;

		private byte value_parallelTasks;
		private readonly FastResourceLock lock_parallelTasks = new FastResourceLock();

		private readonly MyQueue<Action> ActionQueue = new MyQueue<Action>(128);
		private readonly FastResourceLock lock_ActionQueue = new FastResourceLock();

		public byte AllowedParallel { get; private set; }

		public byte ParallelTasks
		{
			get
			{
				using (lock_parallelTasks.AcquireSharedUsing())
					return value_parallelTasks;
			}
			private set
			{
				using (lock_parallelTasks.AcquireExclusiveUsing())
					value_parallelTasks = value;
			}
		}

		public ThreadManager(byte AllowedParallel = 1, bool background = false, string threadName = null)
		{
			this.AllowedParallel = AllowedParallel;
			this.Background = background;
			this.ThreadName = threadName;
			MyAPIGateway.Entities.OnCloseAll += Entities_OnCloseAll;
		}

		private void Entities_OnCloseAll()
		{
			myLogger.debugLog("stopping thread", "Entities_OnCloseAll()", Logger.severity.INFO);
			using (lock_ActionQueue.AcquireExclusiveUsing())
				ActionQueue.Clear();
		}

		public void EnqueueAction(Action toQueue, Action callback = null)
		{
			using (lock_ActionQueue.AcquireExclusiveUsing())
			{
				ActionQueue.Enqueue(toQueue);

				VRage.Exceptions.ThrowIf<Exception>(ActionQueue.Count > QueueOverflow, "queue is too long");
			}

			if (ParallelTasks >= AllowedParallel)
				return;
			ParallelTasks++;

			if (Background)
				MyAPIGateway.Parallel.StartBackground(Run, callback);
			else
				MyAPIGateway.Parallel.Start(Run, callback);
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
				if (ThreadName != null)
					ThreadTracker.ThreadName = ThreadName + '(' + ThreadTracker.ThreadNumber + ')';
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
				ParallelTasks--;
				ThreadTracker.ThreadName = null;
			}
		}

	}
}
