using System;
using System.Collections.Generic;
using Sandbox.ModAPI;

namespace Rynchodon.Threading
{
	/// <summary>
	/// <para>Identifies the current thread by a name and a number.</para>
	/// <para>Persistent threads must be named to avoid conflicts.</para>
	/// </summary>
	public static class ThreadTracker
	{

		public class ThreadNameException : Exception
		{
			public ThreadNameException(string msg) : base(msg) { }
		}

		public const string GameThread_Name = "Game";

		private static ushort ThreadNumberPool = 1;
		private static ushort GameThreadNumber;
		private static Dictionary<ushort, string> AllThreadNames = new Dictionary<ushort, string>();
		private static FastResourceLock lock_threadNameNumber = new FastResourceLock();

		[ThreadStatic]
		private static ushort value_threadNumber;

		/// <summary>
		/// <para>Gets or sets the name of the currently running thread.</para>
		/// <para>If a thread is named it must be unnamed before it terminates.</para>
		/// </summary>
		/// <exception cref="ThreadNameException">If an attempt is made to rename a thread or set multiple threads as game thread.</exception>
		public static string ThreadName
		{
			get
			{
				ushort curThreadNum = ThreadNumber;
				string name;
				using (lock_threadNameNumber.AcquireSharedUsing())
					if (AllThreadNames.TryGetValue(curThreadNum, out name))
						return name;
				return null;
			}
			set
			{
				ushort curThreadNum = ThreadNumber;
				if (string.IsNullOrWhiteSpace(value))
				{
					using (lock_threadNameNumber.AcquireExclusiveUsing())
						AllThreadNames.Remove(curThreadNum);
					return;
				}

				string current;
				using (lock_threadNameNumber.AcquireExclusiveUsing())
				{
					if (AllThreadNames.TryGetValue(curThreadNum, out current))
					{
						if (current != value)
							throw new ThreadNameException("Thread already has a name, current: " + current + ", set: " + value);
						return;
					}

					if (value == GameThread_Name)
					{
						if (GameThreadNumber != 0)
							throw new ThreadNameException("Game thread has already been selected, current: " + GameThreadNumber + ", set: " + curThreadNum);
						GameThreadNumber = ThreadNumber;
					}
					AllThreadNames.Add(curThreadNum, value);
				}
			}
		}

		/// <summary>
		/// Number arbitrary assigned to the current thread. May wrap-around.
		/// </summary>
		public static ushort ThreadNumber
		{
			get
			{
				if (value_threadNumber == 0)
				{
					using (lock_threadNameNumber.AcquireExclusiveUsing())
					{
						// find next available number
						while (ThreadNumberPool == 0 || AllThreadNames.ContainsKey(ThreadNumberPool))
							ThreadNumberPool++;
						value_threadNumber = ThreadNumberPool++;
					}
				}
				return value_threadNumber;
			}
		}

		/// <summary>
		/// Returns the current thread name if it has one, or the number if it does not.
		/// </summary>
		/// <returns>The current thread name if it has one, or the number if it does not.</returns>
		public static string GetNameOrNumber()
		{
			string name = ThreadName;
			if (name != null)
				return name;
			return ThreadNumber.ToString();
		}

		/// <summary>
		/// True iff the current thread is the game thread.
		/// </summary>
		public static bool IsGameThread
		{
			get
			{ return GameThreadNumber != 0 && GameThreadNumber == ThreadNumber; }
		}

		/// <summary>
		/// Sets the name of the current thread to GameThread_Name.
		/// </summary>
		public static void SetGameThread()
		{ ThreadName = GameThread_Name; }

	}
}
