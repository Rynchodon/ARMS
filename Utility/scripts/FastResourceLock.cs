using System;
using VRage;

namespace Rynchodon
{
	/// <summary>
	/// Wrapper for VRage.FastResourceLock to log problems.
	/// </summary>
	public class FastResourceLock
	{
		private static bool Debug = false;

		private Logger myLogger;
		private VRage.FastResourceLock FastLock = new VRage.FastResourceLock();

		//static FastResourceLock()
		//{ Set_Debug_Conditional(); }

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
			return FastLock.TryAcquireExclusive();
			//myLogger.debugLog("entered TryAcquireExclusive(). " + State(), "TryAcquireExclusive()");
			//try
			//{ return FastLock.TryAcquireExclusive(); }
			//finally
			//{ myLogger.debugLog("leaving TryAcquireExclusive(). " + State(), "TryAcquireExclusive()"); }
		}

		public bool TryAcquireShared()
		{
			return FastLock.TryAcquireShared();
			//myLogger.debugLog("entered TryAcquireShared(). " + State(), "TryAcquireShared()");
			//try
			//{ return FastLock.TryAcquireShared(); }
			//finally
			//{ myLogger.debugLog("leaving TryAcquireShared(). " + State(), "TryAcquireShared()"); }
		}

		public ExclusiveLock AcquireExclusiveUsing()
		{
			return new ExclusiveLock(this);
			//myLogger.debugLog("entered AcquireExclusiveUsing(). " + State(), "AcquireExclusiveUsing()");
			//try
			//{ return new ExclusiveLock(this); }
			//finally
			//{ myLogger.debugLog("leaving AcquireExclusiveUsing(). " + State(), "AcquireExclusiveUsing()"); }
		}

		public SharedLock AcquireSharedUsing()
		{
			return new SharedLock(this);
			//myLogger.debugLog("entered AcquireSharedUsing(). " + State(), "AcquireSharedUsing()");
			//try
			//{ return new SharedLock(this); }
			//finally
			//{ myLogger.debugLog("leaving AcquireSharedUsing(). " + State(), "AcquireSharedUsing()"); }
		}

		#endregion
		#region Debug

		/// <summary>
		/// Times out if it more than a second passes without acquiring the lock.
		/// </summary>
		private void AcquireExclusive_Debug()
		{
			myLogger.alwaysLog("entered AcquireExclusive_Debug(). " + State(), "AcquireExclusive_Debug()");

			DateTime timeout = DateTime.UtcNow + new TimeSpan(0, 0, 1);
			while (DateTime.UtcNow < timeout)
				if (FastLock.TryAcquireExclusive())
				{
					myLogger.alwaysLog("acquired exclusive lock.", "AcquireExclusive_Debug()");
					return;
				}

			// timed out
			myLogger.alwaysLog("Lock timed out while trying to acquire exclusive. " + State(), "AcquireExclusive_Debug()", Logger.severity.ERROR);
			throw new TimeoutException("lock timed out");
		}

		/// <summary>
		/// Times out if it more than a second passes without acquiring the lock.
		/// </summary>
		private void AcquireShared_Debug()
		{
			myLogger.alwaysLog("entered AcquireShared_Debug(). " + State(), "AcquireShared_Debug()");

			DateTime timeout = DateTime.UtcNow + new TimeSpan(0, 0, 1);
			while (DateTime.UtcNow < timeout)
				if (FastLock.TryAcquireShared())
				{
					myLogger.alwaysLog("acquired shared lock.", "AcquireShared_Debug()");
					return;
				}

			// timed out
			myLogger.alwaysLog("Lock timed out while trying to acquire shared. " + State(), "AcquireShared_Debug()", Logger.severity.ERROR);
			throw new TimeoutException("lock timed out");
		}

		private void ReleaseExclusive_Debug()
		{
			myLogger.alwaysLog("entered ReleaseExclusive_Debug(). " + State(), "ReleaseExclusive_Debug()");

			bool issue = !FastLock.Owned || FastLock.SharedOwners != 0;
			if (issue)
				myLogger.alwaysLog("Might not be able to release exclusive lock. " + State(), "ReleaseExclusive_Debug()", Logger.severity.WARNING);

			FastLock.ReleaseExclusive();

			if (issue)
				myLogger.alwaysLog("Released exclusive lock successfully.", "ReleaseExclusive_Debug()", Logger.severity.WARNING);

			myLogger.alwaysLog("leaving ReleaseExclusive_Debug(). " + State(), "ReleaseExclusive_Debug()");
		}

		private void ReleaseShared_Debug()
		{
			myLogger.alwaysLog("entered ReleaseShared_Debug(). " + State(), "ReleaseShared_Debug()");

			bool issue = !FastLock.Owned || FastLock.SharedOwners == 0;
			if (issue)
				myLogger.alwaysLog("Might not be able to release shared lock. " + State(), "ReleaseShared_Debug()", Logger.severity.WARNING);

			FastLock.ReleaseShared();

			if (issue)
				myLogger.alwaysLog("Released shared lock successfully.", "ReleaseShared_Debug()", Logger.severity.WARNING);

			myLogger.alwaysLog("leaving ReleaseShared_Debug(). " + State(), "ReleaseShared_Debug()");
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
