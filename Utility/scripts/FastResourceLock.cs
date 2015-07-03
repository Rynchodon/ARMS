﻿using System;
using VRage;
using VRage.Collections;

namespace Rynchodon
{
	/// <summary>
	/// Wrapper for VRage.FastResourceLock to log problems.
	/// </summary>
	public class FastResourceLock
	{
		private const int recentActivityCount = 20;
		private static bool Debug = false;

		private Logger myLogger;
		private VRage.FastResourceLock FastLock = new VRage.FastResourceLock();
		private MyQueue<string> recentActivity = new MyQueue<string>(recentActivityCount);
		private VRage.FastResourceLock lock_recentActivity = new VRage.FastResourceLock();
		private TimeSpan timeout = new TimeSpan(0, 0, 1);

		static FastResourceLock()
		{ Set_Debug_Conditional(); }

		[System.Diagnostics.Conditional("LOG_ENABLED")]
		private static void Set_Debug_Conditional()
		{ Debug = true; }

		public FastResourceLock(string LockName = "N/A")
		{ this.myLogger = new Logger("FastResourceLock", null, () => LockName); }

		#region Public Properties

		public bool Owned { get { return FastLock.Owned; } }
		public int SharedOwners { get { return FastLock.SharedOwners; } }
		public int ExclusiveWaiters { get { return FastLock.ExclusiveWaiters; } }
		public int SharedWaiters { get { return FastLock.SharedWaiters; } }

		#endregion
		#region Public Functions

		public string State()
		{ return "Owned=" + FastLock.Owned + ", SharedOwners=" + FastLock.SharedOwners + ", ExclusiveWaiters=" + FastLock.ExclusiveWaiters + ", SharedWaiters=" + FastLock.SharedWaiters; }

		public void AcquireExclusive()
		{
			if (Debug)
				AcquireExclusive_Debug();
			else
				FastLock.AcquireExclusive();
		}

		public void AcquireShared()
		{
			if (Debug)
				AcquireShared_Debug();
			else
				FastLock.AcquireShared();
		}

		public void ReleaseExclusive()
		{
			if (Debug)
				ReleaseExclusive_Debug();
			else
				FastLock.ReleaseExclusive();
		}

		public void ReleaseShared()
		{
			if (Debug)
				ReleaseShared_Debug();
			else
				FastLock.ReleaseShared();
		}

		public bool TryAcquireExclusive()
		{
			if (Debug)
				return TryAcquireExclusive_Debug();
			else
				return FastLock.TryAcquireExclusive();
		}

		public bool TryAcquireShared()
		{
			if (Debug)
				return TryAcquireShared_Debug();
			else
				return FastLock.TryAcquireShared();
		}

		public ExclusiveLock AcquireExclusiveUsing()
		{ return new ExclusiveLock(this); }

		public SharedLock AcquireSharedUsing()
		{ return new SharedLock(this); }

		#endregion
		#region Debug

		private void AddRecent(string message)
		{
			using (lock_recentActivity.AcquireExclusiveUsing())
			{
				if (recentActivity.Count == recentActivityCount)
					recentActivity.Dequeue();
				recentActivity.Enqueue(message + " " + State());
			}
		}

		private void PrintRecent()
		{
			using (lock_recentActivity.AcquireExclusiveUsing())
				while (recentActivity.Count != 0)
				{
					string recent = recentActivity.Dequeue();
					myLogger.debugLog("Recent: " + recent, "PrintRecent()", Logger.severity.DEBUG);
				}
		}

		/// <summary>
		/// Times out if it too much time passes without acquiring the lock.
		/// </summary>
		private void AcquireExclusive_Debug()
		{
			AddRecent("entered AcquireExclusive_Debug().");

			DateTime timeoutAt = DateTime.UtcNow + timeout;
			while (DateTime.UtcNow < timeoutAt)
				if (FastLock.TryAcquireExclusive())
				{
					AddRecent("acquired exclusive lock.");
					return;
				}

			// timed out
			PrintRecent();
			myLogger.alwaysLog("Lock timed out while trying to acquire exclusive. " + State(), "AcquireExclusive_Debug()", Logger.severity.ERROR);
			throw new TimeoutException("lock timed out");
		}

		/// <summary>
		/// Times out if it too much time passes without acquiring the lock.
		/// </summary>
		private void AcquireShared_Debug()
		{
			//myLogger.alwaysLog("entered AcquireShared_Debug(). " + State(), "AcquireShared_Debug()");
			AddRecent("entered AcquireShared_Debug().");

			DateTime timeoutAt = DateTime.UtcNow + timeout;
			while (DateTime.UtcNow < timeoutAt)
				if (FastLock.TryAcquireShared())
				{
					AddRecent("acquired shared lock.");
					return;
				}

			// timed out
			PrintRecent();
			myLogger.alwaysLog("Lock timed out while trying to acquire shared. " + State(), "AcquireShared_Debug()", Logger.severity.ERROR);
			throw new TimeoutException("lock timed out");
		}

		private void ReleaseExclusive_Debug()
		{
			AddRecent("entered ReleaseExclusive_Debug().");

			bool issue = !FastLock.Owned || FastLock.SharedOwners != 0;
			if (issue)
			{
				PrintRecent();
				myLogger.alwaysLog("Might not be able to release exclusive lock. " + State(), "ReleaseExclusive_Debug()", Logger.severity.WARNING);
			}

			FastLock.ReleaseExclusive();

			if (issue)
				myLogger.alwaysLog("Released exclusive lock successfully.", "ReleaseExclusive_Debug()", Logger.severity.WARNING);

			AddRecent("leaving ReleaseExclusive_Debug().");
		}

		private void ReleaseShared_Debug()
		{
			AddRecent("entered ReleaseShared_Debug().");

			bool issue = !FastLock.Owned || FastLock.SharedOwners == 0;
			if (issue)
			{
				PrintRecent();
				myLogger.alwaysLog("Might not be able to release shared lock. " + State(), "ReleaseShared_Debug()", Logger.severity.WARNING);
			}

			FastLock.ReleaseShared();

			if (issue)
				myLogger.alwaysLog("Released shared lock successfully.", "ReleaseShared_Debug()", Logger.severity.WARNING);

			AddRecent("leaving ReleaseShared_Debug().");
		}

		private bool TryAcquireExclusive_Debug()
		{
			bool success = FastLock.TryAcquireExclusive();
			if (success)
				AddRecent("Try acquired an exclusive lock");
			else
				AddRecent("Try failed to acquire an exclusive lock");
			return success;
		}

		private bool TryAcquireShared_Debug()
		{
			bool success = FastLock.TryAcquireShared();
			if (success)
				AddRecent("Try acquired an shared lock");
			else
				AddRecent("Try failed to acquire an shared lock");
			return success;
		}

		#endregion
		#region Internal Classes

		public class TimeoutException : Exception
		{
			public TimeoutException() : base() { }
			public TimeoutException(string message) : base(message) { }
		}

		public class ExclusiveLock : IDisposable
		{
			private FastResourceLock MyLock;

			public ExclusiveLock(FastResourceLock toLock)
			{
				this.MyLock = toLock;
				this.MyLock.AcquireExclusive();
			}

			public void Dispose()
			{ MyLock.ReleaseExclusive(); }
		}

		public class SharedLock : IDisposable
		{
			private FastResourceLock MyLock;

			public SharedLock(FastResourceLock toLock)
			{
				this.MyLock = toLock;
				this.MyLock.AcquireShared();
			}

			public void Dispose()
			{ MyLock.ReleaseShared(); }
		}

		#endregion
	}
}
