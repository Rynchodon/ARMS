using System; // partial
using System.Collections.Generic;
using System.IO;
using System.Runtime.CompilerServices;
using System.Timers;
using Sandbox.ModAPI;
using VRage;
using VRage.Library.Utils;

namespace Rynchodon.Utility
{
	/// <summary>
	/// Measures the execution time of methods.
	/// Keep in mind that this will include time spent waiting on a lock.
	/// Sum counts the time spent in the outermost blocks.
	/// </summary>
	public class Profiler
	{

		/// <summary>
		/// Fields are in a subclass so they are never initialized if we are not profiling.
		/// </summary>
		private static class ProfileValues
		{
			static ProfileValues()
			{
				MyAPIGateway.Utilities.ShowNotification("Started Profiling", 60000, VRage.Game.MyFontEnum.Green);
			}

			public static MyGameTimer m_timer = new MyGameTimer();
			public static Stats m_total = new Stats();
			public static Dictionary<string, Stats> m_profile = new Dictionary<string, Stats>();
			public static FastResourceLock m_lock = new FastResourceLock();

			[ThreadStatic]
			public static Stack<Block> m_block;
		}

		private struct Block
		{
			public string Name;
			public long Started;
		}

		private class Stats
		{
			public MyTimeSpan TimeSpent;
			public long Invokes;
		}

		/// <summary>
		/// If profiling is enabled, time an action. Otherwise, simply invoke it.
		/// </summary>
		/// <param name="action">Action to be timed.</param>
		public static void Profile(Action action)
		{
			StartProfileBlock(action.Method.Name, action.Target == null ? "N/A" : action.Target.GetType().Name);
			action.Invoke();
			EndProfileBlock();
		}

		/// <summary>
		/// Write all the profile data to a .csv file.
		/// </summary>
		[System.Diagnostics.Conditional("PROFILE")]
		public static void Write()
		{
			FileMaster master = new FileMaster("Profiler master.txt", "Profiler - ", 10);
			System.IO.TextWriter writer = master.GetTextWriter(DateTime.UtcNow.Ticks + ".csv");
			writer.WriteLine("Class Name, Method Name, Seconds, Invokes, Seconds per Invoke, Ratio of Sum, Ratio of Game Time");

			using (ProfileValues.m_lock.AcquireExclusiveUsing())
			{
				WriteBlock(writer, "Game Time,", new Stats() { TimeSpent = MyTimeSpan.FromSeconds(Globals.ElapsedTime.TotalSeconds) });
				WriteBlock(writer, "Sum,", ProfileValues.m_total);
				foreach (var pair in ProfileValues.m_profile)
					WriteBlock(writer, pair.Key, pair.Value);
			}

			writer.Close();
		}

		/// <summary>
		/// Start a profiling block, EndProfileBlock() must be invoked even if an exception is thrown.
		/// </summary>
		/// <param name="name">The name of the block.</param>
		[System.Diagnostics.Conditional("PROFILE")]
		public static void StartProfileBlock([CallerMemberName] string memberName = null, [CallerFilePath] string fileName = null)
		{
			if (ProfileValues.m_block == null)
				ProfileValues.m_block = new Stack<Block>();
			if (fileName.Contains("\\"))
				fileName = Path.GetFileName(fileName);
			if (ProfileValues.m_block.Count > 1000)
				throw new OverflowException("Profile stack is too large: " + ProfileValues.m_block.Count);
			ProfileValues.m_block.Push(new Block() { Name = fileName + ',' + memberName, Started = ProfileValues.m_timer.ElapsedTicks });
		}

		/// <summary>
		/// End a profiling block, must be invoked even if an exception is thrown.
		/// </summary>
		[System.Diagnostics.Conditional("PROFILE")]
		public static void EndProfileBlock()
		{
			Block ended = ProfileValues.m_block.Pop();
			MyTimeSpan elapsed = new MyTimeSpan(ProfileValues.m_timer.ElapsedTicks - ended.Started);

			using (ProfileValues.m_lock.AcquireExclusiveUsing())
			{
				Stats s;
				if (!ProfileValues.m_profile.TryGetValue(ended.Name, out s))
				{
					s = new Stats();
					ProfileValues.m_profile.Add(ended.Name, s);
				}
				s.TimeSpent += elapsed;
				s.Invokes++;

				if (ProfileValues.m_block.Count == 0)
					ProfileValues.m_total.TimeSpent += elapsed;
				ProfileValues.m_total.Invokes++;
			}
		}

		/// <summary>
		/// Write stats for a block.
		/// </summary>
		/// <param name="writer">Text writer to write to.</param>
		/// <param name="name">The name of the block</param>
		/// <param name="s">Stats of the block.</param>
		[System.Diagnostics.Conditional("PROFILE")]
		private static void WriteBlock(System.IO.TextWriter writer, string name, Stats s)
		{
			writer.Write(name);
			writer.Write(',');
			writer.Write(s.TimeSpent.Seconds);
			writer.Write(',');
			writer.Write(s.Invokes);
			writer.Write(',');
			writer.Write(s.TimeSpent.Seconds / (double)s.Invokes);
			writer.Write(',');
			writer.Write((double)s.TimeSpent.Ticks / (double)ProfileValues.m_total.TimeSpent.Ticks);
			writer.Write(',');
			writer.Write(s.TimeSpent.Seconds / Globals.ElapsedTime.TotalSeconds);
			writer.WriteLine();
		}

	}
}
