using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;
using Rynchodon.Threading;
using Rynchodon.Utility;
using Rynchodon.Utility.Collections;
using Sandbox.ModAPI;
using VRage.Game;
using VRage.Utils;

namespace Rynchodon
{
	/// <summary>
	/// Generates log files to be read by GamutLogViewer.
	/// </summary>
	/// <remarks>
	/// <para>Log4J Pattern for GamutLogViewer: [%date][%level][%Thread][%Context][%FileName][%Member][%Line][%PriState][%SecState]%Message</para>
	/// <para> </para>
	/// <para>DEBUG and PROFILE constants are specified by their respective builds.</para>
	/// <para>TRACE shall only be defined as follows:</para>
	/// <para>#if DEBUG</para>
	/// <para>#define TRACE</para>
	/// <para>#endif</para>
	/// <para>Otherwise, an exception will be thrown by traceLog and TraceLog</para>
	/// </remarks>
	public static class Logger
	{

		private struct LogItem
		{
			public severity level;
			public string context, fileName, member, toLog, primaryState, secondaryState, thread;
			public int lineNumber;
			public DateTime time;
		}

		private const string s_logMaster = "log-master.txt";

		private class StaticVariables
		{
			public LockedDeque<LogItem> m_logItems = new LockedDeque<LogItem>();
			public VRage.FastResourceLock m_lockLogging = new VRage.FastResourceLock(); // do not use debug lock or it can get stuck in an exception loop

			public System.IO.TextWriter logWriter = null;
			public StringBuilder stringCache = new StringBuilder();

			public int maxNumLines = 1000000;
			public int numLines = 0;
		}

		private static StaticVariables value_static;
		private static StaticVariables Static
		{
			get
			{
				if (value_static == null)
				{
					if (Globals.WorldClosed)
						throw new Exception("World closed");
					value_static = new StaticVariables();
				}
				return value_static;
			}
		}

		[OnWorldClose]
		private static void Unload()
		{
			if (value_static == null)
				return;

			Static.m_lockLogging.AcquireExclusive();
			try
			{
				LogItem closingLog = new LogItem()
				{
					//context = null,
					fileName = typeof(Logger).ToString(),
					time = DateTime.Now,
					level = severity.INFO,
					//member = null,
					//lineNumber = 0,
					toLog = "Closing log",
					//primaryState = null,
					//secondaryState = null,
					thread = ThreadTracker.GetNameOrNumber()
				};
				log(ref closingLog);


				if (Static.logWriter != null)
				{
					Static.logWriter.Flush();
					Static.logWriter.Close();
					Static.logWriter = null;
				}
			}
			catch (ObjectDisposedException) { }
			finally
			{
				Static.m_lockLogging.ReleaseExclusive();
				value_static = null;
			}
		}

		private static void deleteIfExists(string filename)
		{
			if (MyAPIGateway.Utilities.FileExistsInLocalStorage(filename, typeof(Logger)))
				try { MyAPIGateway.Utilities.DeleteFileInLocalStorage(filename, typeof(Logger)); }
				catch { AlwaysLog("failed to delete file: " + filename, severity.INFO); }
		}

		private static void createLog()
		{
			try
			{
				if (MyAPIGateway.Utilities.FileExistsInLocalStorage(s_logMaster, typeof(Logger)))
				{
					for (int i = 0; i < 10; i++)
						deleteIfExists("log-" + i + ".txt");
					FileMaster master = new FileMaster(s_logMaster, "log-", 10);
					Static.logWriter = master.GetTextWriter(DateTime.UtcNow.Ticks + ".txt");
				}
				else
				{
					for (int i = 0; i < 10; i++)
						if (Static.logWriter == null)
							try
							{ Static.logWriter = MyAPIGateway.Utilities.WriteFileInLocalStorage("log-" + i + ".txt", typeof(Logger)); }
							catch { AlwaysLog("failed to start writer for file: log-" + i + ".txt", severity.INFO); }
						else
							deleteIfExists("log-" + i + ".txt");
				}

				if (Static.logWriter == null)
				{
					MyLog.Default.WriteLine("ARMS Logger ERROR: failed to create a log file");
					throw new Exception("Failed to create log file");
				}
			}
			catch (Exception ex)
			{
				MyLog.Default.WriteLine("ARMS Logger ERROR: failed to create a log file");
				MyLog.Default.WriteLine(ex);
				throw;
			}
		}

		/// <summary>
		/// Conditional on TRACE in calling class. TRACE must be manually specified. If this method is invoked and the build is not Debug an Exception will be thrown.
		/// </summary>
		/// <exception cref="Exception">When the build is not DEBUG</exception>
		[System.Diagnostics.Conditional("TRACE")]
		public static void TraceLog(string toLog, severity level = severity.TRACE, string context = null, string primaryState = null, string secondaryState = null, bool condition = true,
			[CallerFilePath] string filePath = null, [CallerMemberName] string member = null, [CallerLineNumber] int lineNumber = 0)
		{
#if DEBUG
			if (condition)
				log(context, Path.GetFileName(filePath), level, member, lineNumber, toLog, primaryState, secondaryState);
#else
			throw new Exception("DEBUG is not defined");
#endif
		}

		/// <summary>
		/// Conditional on DEBUG in calling class. DEBUG is specified by Debug build.
		/// </summary>
		[System.Diagnostics.Conditional("DEBUG")]
		public static void DebugLog(string toLog, severity level = severity.TRACE, string context = null, string primaryState = null, string secondaryState = null, bool condition = true,
			[CallerFilePath] string filePath = null, [CallerMemberName] string member = null, [CallerLineNumber] int lineNumber = 0)
		{
			if (condition)
				log(context, Path.GetFileName(filePath), level, member, lineNumber, toLog, primaryState, secondaryState);
		}

		/// <summary>
		/// Conditional on PROFILE in calling class. PROFILE is specified by Profile build.
		/// </summary>
		[System.Diagnostics.Conditional("PROFILE")]
		public static void ProfileLog(string toLog, severity level = severity.TRACE, string context = null, string primaryState = null, string secondaryState = null, bool condition = true,
			[CallerFilePath] string filePath = null, [CallerMemberName] string member = null, [CallerLineNumber] int lineNumber = 0)
		{
			if (condition)
				log(context, Path.GetFileName(filePath), level, member, lineNumber, toLog, primaryState, secondaryState);
		}

		/// <summary>
		/// For logging messages in any build.
		/// </summary>
		public static void AlwaysLog(string toLog, severity level = severity.TRACE, string context = null, string primaryState = null, string secondaryState = null,
			[CallerFilePath] string filePath = null, [CallerMemberName] string member = null, [CallerLineNumber] int lineNumber = 0)
		{
			log(context, Path.GetFileName(filePath), level, member, lineNumber, toLog, primaryState, secondaryState);
		}

		private static void log(string context, string fileName, severity level, string member, int lineNumber, string toLog, string primaryState = null, string secondaryState = null)
		{
			if (Globals.WorldClosed)
				return;

			if (Static.numLines >= Static.maxNumLines)
				return;

			if (level <= severity.WARNING)
				DebugNotify("Logger: " + level, 2000, level);

			Static.m_logItems.AddTail(new LogItem()
			{
				context = context,
				fileName = fileName,
				time = DateTime.Now,
				level = level,
				member = member,
				lineNumber = lineNumber,
				toLog = toLog,
				primaryState = primaryState,
				secondaryState = secondaryState,
				thread = ThreadTracker.GetNameOrNumber()
			});

			if (MyAPIGateway.Parallel != null)
				MyAPIGateway.Parallel.StartBackground(logLoop);
		}

		private static void logLoop()
		{
			if (Globals.WorldClosed || !Static.m_lockLogging.TryAcquireExclusive())
				return;
			try
			{
				if (Globals.WorldClosed)
					return;
				LogItem item;
				while (Static.m_logItems.TryPopHead(out item))
					log(ref item);
			}
			catch (Exception ex)
			{
				MyLog.Default.WriteLine("ARMS Logger ERROR: Exception thrown while logging");
				MyLog.Default.WriteLine(ex);
				Static.stringCache.Clear();
				throw;
			}
			finally
			{
				Static.m_lockLogging.ReleaseExclusive();
			}
		}

		private static void log(ref LogItem item)
		{
			if (Static.numLines >= Static.maxNumLines)
				return;

			if (item.toLog == null)
				item.toLog = "null";
			if (item.fileName == null)
				item.fileName = "null";

			if (item.toLog.Contains("\n") || item.toLog.Contains("\r"))
			{
				string[] split = item.toLog.Split(new char[] { '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries);
				foreach (string line in split)
				{
					item.toLog = line;
					log(ref item);
				}
				return;
			}

			if (Static.logWriter == null)
				createLog();

			Static.numLines++;
			appendWithBrackets(item.time.ToString("yyyy-MM-dd HH:mm:ss,fff"));
			appendWithBrackets(item.level.ToString());
			appendWithBrackets(item.thread);
			appendWithBrackets(item.context);
			appendWithBrackets(item.fileName.Substring(0, item.fileName.Length - 3));
			appendWithBrackets(item.member);
			appendWithBrackets(item.lineNumber.ToString());
			appendWithBrackets(item.primaryState);
			appendWithBrackets(item.secondaryState);
			Static.stringCache.Append(item.toLog);

			Static.logWriter.WriteLine(Static.stringCache);
			Static.logWriter.Flush();
			Static.stringCache.Clear();
		}

		private static void appendWithBrackets(string append)
		{
			if (append == null)
				append = String.Empty;
			else
				append = append.Replace('[', '{').Replace(']', '}');
			Static.stringCache.Append('[');
			Static.stringCache.Append(append);
			Static.stringCache.Append(']');
		}

		/// <summary>
		/// For a safe way to display a message as a notification, conditional on DEBUG.
		/// </summary>
		/// <param name="message">the notification message</param>
		/// <param name="disappearTimeMs">time on screen, in milliseconds</param>
		/// <param name="level">severity level</param>
		/// <returns>true iff the message was displayed</returns>
		[System.Diagnostics.Conditional("DEBUG")]
		public static void DebugNotify(string message, int disappearTimeMs = 2000, severity level = severity.TRACE)
		{ Notify(message, disappearTimeMs, level); }

		/// <summary>
		/// For a safe way to display a message as a notification, not conditional.
		/// </summary>
		/// <param name="message">the notification message</param>
		/// <param name="disappearTimeMs">time on screen, in milliseconds</param>
		/// <param name="level">severity level</param>
		/// <returns>true iff the message was displayed</returns>
		public static void Notify(string message, int disappearTimeMs = 2000, severity level = severity.TRACE)
		{
			if (Globals.WorldClosed)
				return;

			string font = fontForSeverity(level);
			if (MyAPIGateway.Utilities != null)
				MyAPIGateway.Utilities.TryInvokeOnGameThread(() => MyAPIGateway.Utilities.ShowNotification(message, disappearTimeMs, font));
			//else
			//	log(severity.WARNING, "ShowNotificationDebug()", "MyAPIGateway.Utilities == null");
		}

		public enum severity : byte { OFF, FATAL, ERROR, WARNING, INFO, DEBUG, TRACE, ALL }

		private static string fontForSeverity(severity level = severity.TRACE)
		{
			switch (level)
			{
				case severity.TRACE:
					return MyFontEnum.Blue;
				case severity.DEBUG:
					return MyFontEnum.DarkBlue;
				case severity.INFO:
					return MyFontEnum.Green;
				case severity.WARNING:
					return MyFontEnum.Red;
				case severity.ERROR:
					return MyFontEnum.Red;
				case severity.FATAL:
					return MyFontEnum.Red;
				default:
					return MyFontEnum.White;
			}
		}

		/// <summary>
		/// Append the relevant portion of the stack to a StringBuilder.
		/// </summary>
		public static void AppendStack(StringBuilder builder, StackTrace stackTrace, params Type[] skipTypes)
		{
			builder.AppendLine("   Stack:");
			int totalFrames = stackTrace.FrameCount, frame = 0;
			while (true)
			{
				if (frame >= totalFrames)
				{
					builder.AppendLine("Failed to skip frames, dumping all");
					builder.Append(stackTrace);
					builder.AppendLine();
					return;
				}
				Type declaringType = stackTrace.GetFrame(frame).GetMethod().DeclaringType;

				foreach (Type t in skipTypes)
					if (declaringType == t)
					{
						frame++;
						continue;
					}

				break;
			}

			bool appendedFrame = false;
			while (frame < totalFrames)
			{
				MethodBase method = stackTrace.GetFrame(frame).GetMethod();
				if (!method.DeclaringType.Namespace.StartsWith("Rynchodon"))
					break;
				appendedFrame = true;
				builder.Append("   at ");
				builder.Append(method.DeclaringType);
				builder.Append('.');
				builder.Append(method);
				builder.AppendLine();
				frame++;
			}

			if (!appendedFrame)
			{
				builder.AppendLine("Did not append any frames, dumping all");
				builder.Append(stackTrace);
				builder.AppendLine();
				return;
			}
		}

		[Conditional("DEBUG")]
		public static void DebugLogCallStack(severity level = severity.TRACE, string context = null, string primaryState = null, string secondaryState = null, bool condition = true,
			[CallerFilePath] string filePath = null, [CallerMemberName] string member = null, [CallerLineNumber] int lineNumber = 0)
		{
			if (!condition)
				return;
			StringBuilder builder = new StringBuilder(255);
			AppendStack(builder, new StackTrace(), typeof(Logger));
			DebugLog(builder.ToString(), level, context, primaryState, secondaryState, true, filePath, member, lineNumber);
		}

	}
}
