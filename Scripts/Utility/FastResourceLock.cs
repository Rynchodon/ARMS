#if DEBUG
//#define DEBUG_LOCKS
#if DEBUG_LOCKS
#define STACK_TRACE
#endif
#endif

using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text;
using VRage;
using VRage.Collections;

namespace Rynchodon
{
	/// <summary>
	/// Wrapper for FastResourceLock to log problems.
	/// </summary>
	public class FastResourceLock
	{
#if DEBUG_LOCKS

		private struct Activity
		{
			private readonly string m_message;
			private readonly bool m_owned;
			private readonly int m_sharedOwners, m_exclusiveWaiters, m_sharedWaiters;
			private readonly DateTime m_time;
#if STACK_TRACE
			private readonly StackTrace m_stackTrace;
#endif

			public Activity(VRage.FastResourceLock fastLock, string message)
			{
				this.m_message = message;
				this.m_owned = fastLock.Owned;
				this.m_sharedOwners = fastLock.SharedOwners;
				this.m_exclusiveWaiters = fastLock.ExclusiveWaiters;
				this.m_sharedWaiters = fastLock.SharedWaiters;
				this.m_time = DateTime.Now;
#if STACK_TRACE
				this.m_stackTrace = new StackTrace();
#endif
			}

			public void AppendTo(StringBuilder builder)
			{
				builder.Append(m_time.ToString("yyyy-MM-dd HH:mm:ss,fff"));
				builder.Append(": ");
				builder.AppendLine(m_message);

				builder.Append("   State: Owned=");
				builder.Append(m_owned);
				builder.Append(", SharedOwners=");
				builder.Append(m_sharedOwners);
				builder.Append(", ExclusiveWaiters=");
				builder.Append(m_exclusiveWaiters);
				builder.Append(", SharedWaiters=");
				builder.Append(m_sharedWaiters);
				builder.AppendLine();

#if STACK_TRACE
				Logger.AppendStack(builder, m_stackTrace, typeof(FastResourceLock), typeof(Activity));
#endif
			}
		}

		private string m_callerFilePath;
		private const int recentActivityCount = 20;
		private static readonly TimeSpan timeout = new TimeSpan(0, 0, 10);
		private MyQueue<Activity> recentActivity = new MyQueue<Activity>(recentActivityCount);
		private VRage.FastResourceLock lock_recentActivity = new VRage.FastResourceLock();

		private Logable Log { get { return Logable(callerFilePath, lockName); } }
#endif

		private VRage.FastResourceLock FastLock = new VRage.FastResourceLock();

		public FastResourceLock(string lockName = "N/A", [CallerFilePath]string callerFilePath = null)
		{
#if DEBUG_LOCKS
			m_callerFilePath = Path.GetFileName(callerFilePath);
#endif
		}

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
#if DEBUG_LOCKS
			AcquireExclusive_Debug();
#else
			FastLock.AcquireExclusive();
#endif
		}

		public void AcquireShared()
		{
#if DEBUG_LOCKS
			AcquireShared_Debug();
#else
			FastLock.AcquireShared();
#endif
		}

		public void ReleaseExclusive()
		{
#if DEBUG_LOCKS
			ReleaseExclusive_Debug();
#else
			FastLock.ReleaseExclusive();
#endif
		}

		public void ReleaseShared()
		{
#if DEBUG_LOCKS
			ReleaseShared_Debug();
#else
			FastLock.ReleaseShared();
#endif
		}

		public bool TryAcquireExclusive()
		{
#if DEBUG_LOCKS
			return TryAcquireExclusive_Debug();
#else
			return FastLock.TryAcquireExclusive();
#endif
		}

		public bool TryAcquireShared()
		{
#if DEBUG_LOCKS
			return TryAcquireShared_Debug();
#else
			return FastLock.TryAcquireShared();
#endif
		}

#if DEBUG_LOCKS
		public ExclusiveLock AcquireExclusiveUsing()
		{
			return new ExclusiveLock(this);
		}
#else
		public VRage.FastResourceLockExtensions.MyExclusiveLock AcquireExclusiveUsing()
		{
			return FastLock.AcquireExclusiveUsing();
		}
#endif

#if DEBUG_LOCKS
		public SharedLock AcquireSharedUsing()
		{
			return new SharedLock(this);
		}
#else
		public VRage.FastResourceLockExtensions.MySharedLock AcquireSharedUsing()
		{
			return FastLock.AcquireSharedUsing();
		}
#endif

		#endregion
#if DEBUG_LOCKS
		#region Debug

		private void AddRecent(string message)
		{
			using (lock_recentActivity.AcquireExclusiveUsing())
			{
				if (recentActivity.Count == recentActivityCount)
					recentActivity.Dequeue();

				recentActivity.Enqueue(new Activity(FastLock, message));
			}
		}

		private void PrintRecent(string reason, Logger.severity level = Logger.severity.FATAL)
		{
			StringBuilder sb = new StringBuilder();
			sb.AppendLine(reason);
			sb.Append("Current state: ");
			sb.AppendLine(State());
			sb.AppendLine("Recent activity:");
			sb.AppendLine();

			using (lock_recentActivity.AcquireExclusiveUsing())
				while (recentActivity.Count != 0)
				{
					recentActivity.Dequeue().AppendTo(sb);
					sb.AppendLine();
				}

			sb.AppendLine("End of recent activity");
			string log = sb.ToString();
			VRage.Utils.MyLog.Default.WriteLine(log);
			Log.AlwaysLog(log, level);
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
			PrintRecent("Lock timed out while trying to acquire exclusive.");
			throw new TimeoutException("lock timed out");
		}

		/// <summary>
		/// Times out if it too much time passes without acquiring the lock.
		/// </summary>
		private void AcquireShared_Debug()
		{
			//Log.AlwaysLog("entered AcquireShared_Debug(). " + State(), "AcquireShared_Debug()");
			AddRecent("entered AcquireShared_Debug().");

			DateTime timeoutAt = DateTime.UtcNow + timeout;
			while (DateTime.UtcNow < timeoutAt)
				if (FastLock.TryAcquireShared())
				{
					AddRecent("acquired shared lock.");
					return;
				}

			// timed out
			PrintRecent("Lock timed out while trying to acquire shared.");
			throw new TimeoutException("lock timed out");
		}

		private void ReleaseExclusive_Debug()
		{
			AddRecent("entered ReleaseExclusive_Debug().");

			bool issue = !FastLock.Owned || FastLock.SharedOwners != 0;
			if (issue)
				PrintRecent("Might not be able to release exclusive lock.", Logger.severity.WARNING);

			FastLock.ReleaseExclusive();

			if (issue)
				Log.AlwaysLog("Released exclusive lock successfully.", Logger.severity.WARNING);

			AddRecent("leaving ReleaseExclusive_Debug().");
		}

		private void ReleaseShared_Debug()
		{
			AddRecent("entered ReleaseShared_Debug().");

			bool issue = !FastLock.Owned || FastLock.SharedOwners == 0;
			if (issue)
				PrintRecent("Might not be able to release shared lock.", Logger.severity.WARNING);

			FastLock.ReleaseShared();

			if (issue)
				Log.AlwaysLog("Released shared lock successfully.", Logger.severity.WARNING);

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

		public struct ExclusiveLock : IDisposable
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

		public struct SharedLock : IDisposable
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
#endif
	}
}
