#define LOG_ENABLED //remove on build

using System;
using System.Collections.Generic;
//using System.Linq;
//using System.Text;

using Sandbox.Common;
using Sandbox.ModAPI;
using VRage;

namespace Rynchodon.Autopilot.Turret
{
	[Sandbox.Common.MySessionComponentDescriptor(Sandbox.Common.MyUpdateOrder.NoUpdate)]
	public class TurretThread : MySessionComponentBase
	{
		private static Logger myLogger = new Logger(null, "TurretThread");

		// Stack works much better than a Queue or LinkedList
		private static Stack<Action> TurretActions = new Stack<Action>();
		private static FastResourceLock lock_TurretActions = new FastResourceLock();

		private static bool isRunning = false;
		private static FastResourceLock lock_isRunning = new FastResourceLock();

		protected override void UnloadData()
		{ TurretActions = null; }

		public static void EnqueueAction(Action item, FastResourceLock lock_MyAPIGateway = null)
		{
			using (lock_TurretActions.AcquireExclusiveUsing())
				TurretActions.Push(item);
			using (lock_isRunning.AcquireExclusiveUsing())
			{
				if (isRunning)
					return;
				isRunning = true;
			}
			if (lock_MyAPIGateway != null)
				lock_MyAPIGateway.AcquireShared();
			try { MyAPIGateway.Parallel.Start(Run); }
			finally
			{
				if (lock_MyAPIGateway != null)
					lock_MyAPIGateway.ReleaseShared();
			}
		}

		private static void Run()
		{
			while (true)
			{
				using (lock_TurretActions.AcquireSharedUsing())
					if (TurretActions.Count == 0)
						break;

				Action currentItem;
				using (lock_TurretActions.AcquireExclusiveUsing())
					currentItem = TurretActions.Pop();

				currentItem();
				//logFinished();
			}
			isRunning = false;
		}

		[System.Diagnostics.Conditional("LOG_ENABLED")]
		private static void logFinished()
		{
			using (lock_TurretActions.AcquireSharedUsing())
				myLogger.debugLog("finished invoke, " + TurretActions.Count + " remaining", "Run()");
		}
	}
}
