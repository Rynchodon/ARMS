using System;
using System.Runtime.CompilerServices;
using System.Text;
using Rynchodon.Threading;
using Rynchodon.Utility;
using Sandbox.ModAPI;
using VRage;
using VRage.Game;
using VRage.Game.Components;
using VRage.Game.ModAPI;
using VRage.ModAPI;

namespace Rynchodon
{
	/// <summary>
	/// Generates log files to be read by GamutLogViewer.
	/// </summary>
	/// <remarks>
	/// <para>The prefered way to use Logger is as follows:</para>
	///	<para>    each class shall have "#define LOG_ENABLED // remove on build" as its first line to enable debug logging</para>
	///	<para>    each class shall declare a Logger field (usually myLogger)</para>
	///	<para>    all logging actions shall be performed by calling myLogger.alwaysLog() or myLogger.debugLog()</para>
	///	<para>    notifications shall use myLogger.notify() or myLogger.debugNotify()</para>
	///	<para>WARNING: Because Keen has murdered pre-processor symbols, build.py now searches for lines containing any of the following and removes them:</para>
	///	<para>    #define LOG_ENABLED</para>
	///	<para>removed for Dev version:</para>
	///	<para>    System.Diagnostics.Conditional</para>
	/// <para> </para>
	/// <para>Log4J Pattern for GamutLogViewer: [%date][%level][%Thread][%Context][%Class][%Member][%Line][%PriState][%SecState]%Message</para>
	/// </remarks>
	[MySessionComponentDescriptor(MyUpdateOrder.NoUpdate)]
	public class Logger : MySessionComponentBase
	{

		private struct LogItem
		{
			public Logger logger;
			public severity level;
			public string member, toLog, primaryState, secondaryState, thread;
			public int lineNumber;
			public DateTime time;
		}

		private const string s_logMaster = "log-master.txt";

		private class StaticVariables
		{
			public LockedQueue<LogItem> m_logItems = new LockedQueue<LogItem>();
			public FastResourceLock m_lockLogging = new FastResourceLock();

			public System.IO.TextWriter logWriter = null;
			public StringBuilder stringCache = new StringBuilder();

			public int maxNumLines = 1000000;
			public int numLines = 0;
		}

		private static StaticVariables Static = new StaticVariables();

		private readonly string m_classname;
		private readonly Func<string> f_context, f_state_primary, f_state_secondary;

		public severity MinimumLevel = severity.ALL;

		/// <summary>
		/// Creates a Logger that gets the context and states from supplied functions.
		/// </summary>
		/// <param name="calling_class">the name of the class this Logger belongs to</param>
		/// <param name="context">the context of this logger</param>
		/// <param name="default_primary">the primary state used when one is not supplied to alwaysLog() or debugLog()</param>
		/// <param name="default_secondary">the secondary state used when one is not supplied to alwaysLog() or debugLog()</param>
		public Logger(string calling_class, Func<string> context = null, Func<string> default_primary = null, Func<string> default_secondary = null)
		{
			this.m_classname = calling_class;
			this.f_context = context;
			this.f_state_primary = default_primary;
			this.f_state_secondary = default_secondary;
		}

		/// <summary>
		/// Creates a Logger that gets the context and states from block and supplied function.
		/// </summary>
		/// <param name="calling_class">the name of the class this Logger belongs to</param>
		/// <param name="block">The block to get context and states from</param>
		/// <param name="default_secondary">the secondary state used when one is not supplied to alwaysLog() or debugLog()</param>
		public Logger(string calling_class, IMyCubeBlock block, Func<string> default_secondary = null)
		{
			this.m_classname = calling_class;

			this.f_context = () => block.CubeGrid.DisplayName + " - " + block.CubeGrid.EntityId;
			if (default_secondary == null)
			{
				this.f_state_primary = () => block.DefinitionDisplayNameText;
				this.f_state_secondary = () => block.getNameOnly() + " - " + block.EntityId;
			}
			else
			{
				this.f_state_primary = () => block.getNameOnly() + " - " + block.EntityId;
				this.f_state_secondary = default_secondary;
			}
		}

		public Logger(string calling_class, IMyCubeGrid grid, Func<string> default_primary = null, Func<string> default_secondary = null)
		{
			this.m_classname = calling_class;

			this.f_context = () => grid.DisplayName + " - " + grid.EntityId;
			this.f_state_primary = default_primary;
			this.f_state_secondary = default_secondary;
		}

		public Logger(string calling_class, IMyEntity entity)
		{
			this.m_classname = calling_class;

			IMyCubeBlock asBlock = entity as IMyCubeBlock;
			if (asBlock != null)
			{
				this.f_context = () => asBlock.CubeGrid.DisplayName + " - " + asBlock.CubeGrid.EntityId;
				this.f_state_primary = () => asBlock.DefinitionDisplayNameText;
				this.f_state_secondary = () => asBlock.getNameOnly() + " - " + asBlock.EntityId;
				return;
			}

			IMyCubeGrid asGrid = entity as IMyCubeGrid;
			if (asGrid != null)
			{
				this.f_context = () => asGrid.DisplayName + " - " + asGrid.EntityId;
				return;
			}

			this.f_context = entity.getBestName;
		}

		/// <summary>
		/// needed for MySessionComponentBase
		/// </summary>
		public Logger() { }

		/// <summary>
		/// Deprecated. Creates a Logger without default states.
		/// </summary>
		/// <param name="gridName">the name of the grid this Logger belongs to, may be null</param>
		/// <param name="className">the name of the class this Logger belongs to</param>
		[Obsolete]
		public Logger(string gridName, string className)
		{
			this.f_context = () => gridName;
			this.m_classname = className;
		}

		private static void deleteIfExists(string filename)
		{
			if (MyAPIGateway.Utilities.FileExistsInLocalStorage(filename, typeof(Logger)))
				MyAPIGateway.Utilities.DeleteFileInLocalStorage(filename, typeof(Logger));
		}

		private static bool createLog()
		{
			if (MyAPIGateway.Utilities.FileExistsInLocalStorage(s_logMaster, typeof(Logger)))
			{
				for (int i = 0; i < 10; i++)
					try
					{ deleteIfExists("log-" + i + ".txt"); }
					catch { }
				FileMaster master = new FileMaster(s_logMaster, "log-", 10);
				Static.logWriter = master.GetTextWriter(DateTime.UtcNow.Ticks + ".txt");
			}
			else
			{
				for (int i = 0; i < 10; i++)
					if (Static.logWriter == null)
						try { Static.logWriter = MyAPIGateway.Utilities.WriteFileInLocalStorage("log-" + i + ".txt", typeof(Logger)); }
						catch { }
					else
						try
						{ deleteIfExists("log-" + i + ".txt"); }
						catch { }
			}

			return Static.logWriter != null;
		}

		/// <summary>
		/// For logging INFO and lower severity, conditional on LOG_ENABLED in calling class. Sometimes used for WARNING.
		/// </summary>
		/// <param name="toLog">message to log</param>
		/// <param name="methodName">calling method</param>
		/// <param name="level">severity level</param>
		/// <param name="primaryState">class specific, appears before secondary state in log</param>
		/// <param name="secondaryState">class specific, appears before message in log</param>
		[System.Diagnostics.Conditional("LOG_ENABLED")]
		public void debugLog(string toLog, severity level = severity.TRACE, string primaryState = null, string secondaryState = null, [CallerMemberName] string member = null, [CallerLineNumber] int lineNumber = 0)
		{ log(level, member, lineNumber, toLog, primaryState, secondaryState); }

		/// <summary>
		/// For logging INFO and lower severity, conditional on LOG_ENABLED in calling class. Sometimes used for WARNING.
		/// </summary>
		/// <param name="condition">only log if true</param>
		/// <param name="toLog">message to log</param>
		/// <param name="methodName">calling method</param>
		/// <param name="level">severity level</param>
		/// <param name="primaryState">class specific, appears before secondary state in log</param>
		/// <param name="secondaryState">class specific, appears before message in log</param>
		[System.Diagnostics.Conditional("LOG_ENABLED")]
		public void debugLog(bool condition, string toLog, severity level = severity.TRACE, string primaryState = null, string secondaryState = null, [CallerMemberName] string member = null, [CallerLineNumber] int lineNumber = 0)
		{
			if (condition)
				log(level, member, lineNumber, toLog, primaryState, secondaryState);
		}

		/// <summary>
		/// For logging INFO and lower severity, conditional on LOG_ENABLED in calling class. Sometimes used for WARNING.
		/// </summary>
		/// <param name="condition">only log if true</param>
		/// <param name="toLog">message to log</param>
		/// <param name="methodName">calling method</param>
		/// <param name="level">severity level</param>
		/// <param name="primaryState">class specific, appears before secondary state in log</param>
		/// <param name="secondaryState">class specific, appears before message in log</param>
		[System.Diagnostics.Conditional("LOG_ENABLED")]
		public void debugLog(bool condition, Func<string> toLog, severity level = severity.TRACE, string primaryState = null, string secondaryState = null, [CallerMemberName] string member = null, [CallerLineNumber] int lineNumber = 0)
		{
			if (condition)
				log(level, member, lineNumber, toLog.Invoke(), primaryState, secondaryState);
		}

		/// <summary>
		/// For logging WARNING and higher severity.
		/// </summary>
		/// <param name="toLog">message to log</param>
		/// <param name="methodName">calling method</param>
		/// <param name="level">severity level</param>
		/// <param name="primaryState">class specific, appears before secondary state in log</param>
		/// <param name="secondaryState">class specific, appears before message in log</param>
		public void alwaysLog(string toLog, severity level = severity.TRACE, string primaryState = null, string secondaryState = null, [CallerMemberName] string member = null, [CallerLineNumber] int lineNumber = 0)
		{
			log(level, member, lineNumber, toLog, primaryState, secondaryState);
		}

		/// <summary>
		/// Do not call from outside Logger class, use debugLog or alwaysLog.
		/// </summary>
		/// <param name="level">severity level</param>
		/// <param name="methodName">calling method</param>
		/// <param name="toLog">message to log</param>
		/// <param name="primaryState">class specific, appears before secondary state in log</param>
		/// <param name="secondaryState">class specific, appears before message in log</param>
		private void log(severity level, string member, int lineNumber, string toLog, string primaryState = null, string secondaryState = null)
		{
			if (Static == null)
				return;

			if (Static.numLines >= Static.maxNumLines)
				return;

			if (level <= severity.WARNING)
				debugNotify("Logger: " + level, 2000, level);
			else if (level > MinimumLevel)
				return;

			Static.m_logItems.Enqueue(new LogItem()
			{
				logger = this,
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
			if (Static == null || MyAPIGateway.Utilities == null || !Static.m_lockLogging.TryAcquireExclusive())
				return;

			try
			{
				LogItem item;
				while (Static.m_logItems.TryDequeue(out item))
					log(item);
			}
			finally
			{
				Static.m_lockLogging.ReleaseExclusive();
			}
		}

		private static void log(LogItem item)
		{
			if (Static.numLines >= Static.maxNumLines)
				return;

			if (item.toLog.Contains("\n") || item.toLog.Contains("\r"))
			{
				string[] split = item.toLog.Split(new char[] { '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries);
				foreach (string line in split)
				{
					item.toLog = line;
					log(item);
				}
				return;
			}

			if (Static.logWriter == null)
				if (!createLog())
					return; // cannot log

			string context;
			if (item.logger.f_context != null)
				try { context = item.logger.f_context.Invoke(); }
				catch { context = string.Empty; }
			else
				context = string.Empty;
			if (item.primaryState == null)
			{
				if (item.logger.f_state_primary != null)
					try { item.primaryState = item.logger.f_state_primary.Invoke(); }
					catch { item.primaryState = string.Empty; }
			}
			if (item.secondaryState == null)
			{
				if (item.logger.f_state_secondary != null)
					try { item.secondaryState = item.logger.f_state_secondary.Invoke(); }
					catch { item.secondaryState = string.Empty; }
			}

			Static.numLines++;
			appendWithBrackets(item.time.ToString("yyyy-MM-dd HH:mm:ss,fff"));
			appendWithBrackets(item.level.ToString());
			appendWithBrackets(item.thread);
			appendWithBrackets(context);
			appendWithBrackets(item.logger.m_classname);
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

		private void close()
		{
			Static.m_lockLogging.AcquireExclusive();
			
			log(new LogItem()
			{
				logger = this,
				time = DateTime.Now,
				level = severity.INFO,
				member = string.Empty,
				lineNumber = 0,
				toLog = "Closing log",
				primaryState = null,
				secondaryState = null,
				thread = ThreadTracker.GetNameOrNumber()
			});

			StaticVariables temp = Static;
			Static = null;

			try
			{
				if (temp.logWriter != null)
				{
					temp.logWriter.Flush();
					temp.logWriter.Close();
				}
			}
			catch (ObjectDisposedException) { }
			finally
			{
				temp.m_lockLogging.ReleaseExclusive();
			}
		}

		protected override void UnloadData()
		{
			base.UnloadData();
			close();
		}

		/// <summary>
		/// For a safe way to display a message as a notification, conditional on LOG_ENABLED.
		/// </summary>
		/// <param name="message">the notification message</param>
		/// <param name="disappearTimeMs">time on screen, in milliseconds</param>
		/// <param name="level">severity level</param>
		/// <returns>true iff the message was displayed</returns>
		[System.Diagnostics.Conditional("LOG_ENABLED")]
		public static void debugNotify(string message, int disappearTimeMs = 2000, severity level = severity.TRACE)
		{ notify(message, disappearTimeMs, level); }

		/// <summary>
		/// For a safe way to display a message as a notification, not conditional.
		/// </summary>
		/// <param name="message">the notification message</param>
		/// <param name="disappearTimeMs">time on screen, in milliseconds</param>
		/// <param name="level">severity level</param>
		/// <returns>true iff the message was displayed</returns>
		public static void notify(string message, int disappearTimeMs = 2000, severity level = severity.TRACE)
		{
			if (Static == null)
				return;

			MyFontEnum font = fontForSeverity(level);
			if (MyAPIGateway.Utilities != null)
				MyAPIGateway.Utilities.TryInvokeOnGameThread(() => MyAPIGateway.Utilities.ShowNotification(message, disappearTimeMs, font));
			//else
			//	log(severity.WARNING, "ShowNotificationDebug()", "MyAPIGateway.Utilities == null");
		}

		public enum severity : byte { OFF, FATAL, ERROR, WARNING, INFO, DEBUG, TRACE, ALL }

		private static MyFontEnum fontForSeverity(severity level = severity.TRACE)
		{
			switch (level)
			{
				case severity.INFO:
					return MyFontEnum.Green;
				case severity.TRACE:
					return MyFontEnum.White;
				case severity.DEBUG:
					return MyFontEnum.Debug;
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

	}
}
