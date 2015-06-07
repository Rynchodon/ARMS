#define LOG_ENABLED //remove on build

using System;
using System.Collections.Generic;
using Sandbox.Common;
using Sandbox.ModAPI;
using VRage;

namespace Rynchodon.Autopilot.Turret
{
	//[Sandbox.Common.MySessionComponentDescriptor(Sandbox.Common.MyUpdateOrder.NoUpdate)]
	public class TurretThread : MySessionComponentBase
	{
		private static Logger myLogger = new Logger(null, "TurretThread");

		// Stack works much better than a Queue or LinkedList
		private static Stack<Action> TurretActions = new Stack<Action>();
		private static FastResourceLock lock_TurretActions = new FastResourceLock();

		private static bool isRunning = false;
		private static FastResourceLock lock_isRunning = new FastResourceLock();

		//protected override void UnloadData()
		//{ TurretActions = null; }

		public static void EnqueueAction(Action item, FastResourceLock lock_MyAPIGateway = null)
		{
			lock_TurretActions.AcquireExclusive();
			try { TurretActions.Push(item); }
			finally { lock_TurretActions.ReleaseExclusive(); }

			lock_isRunning.AcquireExclusive();
			try
			{
				if (isRunning)
					return;
				isRunning = true;
			}
			finally { lock_isRunning.ReleaseExclusive(); }

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
				lock_TurretActions.AcquireShared();
				try
				{
					if (TurretActions.Count == 0)
						break;
				}
				finally { lock_TurretActions.ReleaseShared(); }

				Action currentItem;
				lock_TurretActions.AcquireExclusive();
				try { currentItem = TurretActions.Pop(); }
				finally { lock_TurretActions.ReleaseExclusive(); }

				currentItem();
				//logFinished();
			}
			isRunning = false;
		}

		[System.Diagnostics.Conditional("LOG_ENABLED")]
		private static void logFinished()
		{
			lock_TurretActions.AcquireShared();
			try { myLogger.debugLog("finished invoke, " + TurretActions.Count + " remaining", "Run()"); }
			finally { lock_TurretActions.ReleaseShared(); }
		}
	}
}
