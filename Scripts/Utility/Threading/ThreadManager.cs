using System;
using Rynchodon.Utility;
using Rynchodon.Utility.Collections;
using Sandbox.ModAPI;

namespace Rynchodon.Threading
{
	public class ThreadManager
	{

		private const int QueueOverflow = 1000000;

		private static readonly int StaticMaxParallel;
		private static int StaticParallel;
		private static readonly FastResourceLock lock_parallelTasks = new FastResourceLock();

		static ThreadManager()
		{
			StaticMaxParallel = Math.Max(Environment.ProcessorCount - 2, 1);
		}

		private readonly bool Background;
		private readonly string ThreadName;

		private LockedDeque<Action> ActionQueue = new LockedDeque<Action>(8);

		public readonly byte AllowedParallel;

		public byte ParallelTasks { get; private set; }

		private Logable Log { get { return new Logable(ThreadName ?? string.Empty, ParallelTasks.ToString()); } }

		public ThreadManager(byte AllowedParallel = 1, bool background = false, string threadName = null)
		{
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
			if (Globals.WorldClosed)
			{
				Log.DebugLog("Cannot enqueue, world is closed");
				return;
			}

			ActionQueue.AddTail(toQueue);
			VRage.Exceptions.ThrowIf<Exception>(ActionQueue.Count > QueueOverflow, "queue is too long");

			if (ParallelTasks >= AllowedParallel || StaticParallel >= StaticMaxParallel)
				return;

			MyAPIGateway.Utilities.InvokeOnGameThread(() => {
				if (MyAPIGateway.Parallel == null)
				{
					Log.DebugLog("Parallel == null", Logger.severity.WARNING);
					return;
				}

				if (Background)
					MyAPIGateway.Parallel.StartBackground(Run);
				else
					MyAPIGateway.Parallel.Start(Run);
			});
		}

		private void Run()
		{
			using (lock_parallelTasks.AcquireExclusiveUsing())
			{
				if (ParallelTasks >= AllowedParallel || StaticParallel >= StaticMaxParallel)
					return;
				ParallelTasks++;
				StaticParallel++;
			}
			try
			{
				if (ThreadName != null)
					ThreadTracker.ThreadName = ThreadName + '(' + ThreadTracker.ThreadNumber + ')';
				Action currentItem;
				while (ActionQueue.TryPopTail(out currentItem))
				{
					if (currentItem != null)
					{
						Profiler.StartProfileBlock(currentItem);
						currentItem.Invoke();
						Profiler.EndProfileBlock();
					}
					else
						Log.DebugLog("null action", Logger.severity.WARNING);
				}
			}
			catch (Exception ex) { Log.AlwaysLog("Exception: " + ex, Logger.severity.ERROR); }
			finally
			{
				using (lock_parallelTasks.AcquireExclusiveUsing())
				{
					ParallelTasks--;
					StaticParallel--;
				}
				ThreadTracker.ThreadName = null;
			}
		}

	}
}
