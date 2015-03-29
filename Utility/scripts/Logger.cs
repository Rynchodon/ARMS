using System;
using System.Collections.Generic;
//using System.Linq;
using System.Text;

using Sandbox.Common;
using Sandbox.Definitions;
using Sandbox.ModAPI;
using VRage;

namespace Rynchodon
{
	// 
	/// <summary>
	/// Generates log files to be read by GamutLogViewer.
	/// </summary>
	/// <remarks>
	/// <para>The prefered way to use Logger is as follows:</para>
	///	<para>    each class shall have "#define LOG_ENABLED // remove on build" as its first line to enable debug logging</para>
	///	<para>    each class shall declare a Logger field (usually myLogger) that must be initialized</para>
	///	<para>    the constructor/initializer may replace myLogger with a more verbose one</para>
	///	<para>    all logging actions shall be performed by calling myLogger.log() or myLogger.debugLog()</para>
	///	<para>    notifications shall use myLogger.notify() or myLogger.debugNotify()</para>
	/// <para> </para>
	/// <para>Log4J Pattern for GamutLogViewer: [%date][%level][%Grid][%Class][%Method][%PriState][%SecState]%Message</para>
	/// </remarks>
	[Sandbox.Common.MySessionComponentDescriptor(Sandbox.Common.MyUpdateOrder.NoUpdate)]
	public class Logger : Sandbox.Common.MySessionComponentBase
	{
		private static System.IO.TextWriter logWriter = null;
		private StringBuilder stringCache = new StringBuilder();

		private static int maxNumLines = 1000000;
		private static int numLines = 0;

		/// <summary>
		/// take precedence over strings
		/// </summary>
		private Func<string> f_gridName, f_state_primary, f_state_secondary;
		private string gridName, className, default_primary, default_secondary;

		/// <summary>
		/// needed for MySessionComponentBase, not useful for logging
		/// </summary>
		public Logger() { }

		/// <summary>
		/// Creates a Logger without default states.
		/// </summary>
		/// <param name="gridName">the name of the grid this Logger belongs to, may be null</param>
		/// <param name="className">the name of the class this Logger belongs to</param>
		public Logger(string gridName, string className)
		{
			this.gridName = gridName;
			this.className = className;
		}

		/// <summary>
		/// Creates a Logger with default states.
		/// </summary>
		/// <param name="gridName">the name of the grid this Logger belongs to, may be null</param>
		/// <param name="className">the name of the class this Logger belongs to</param>
		/// <param name="default_primary">the primary state used when one is not supplied to log() or debugLog()</param>
		/// <param name="default_secondary">the secondary state used when one is not supplied to log() or debugLog()</param>
		public Logger(string gridName, string className, string default_primary, string default_secondary = null)
			: this(gridName, className)
		{
			this.default_primary = default_primary;
			this.default_secondary = default_secondary;
		}

		/// <summary>
		/// Creates a Logger that gets the states from supplied functions.
		/// </summary>
		/// <param name="gridName">the name of the grid this Logger belongs to, may be null</param>
		/// <param name="className">the name of the class this Logger belongs to</param>
		/// <param name="default_primary">the primary state used when one is not supplied to log() or debugLog()</param>
		/// <param name="default_secondary">the secondary state used when one is not supplied to log() or debugLog()</param>
		public Logger(string gridName, string className, Func<string> default_primary, Func<string> default_secondary = null)
			: this(gridName, className)
		{
			this.f_state_primary = default_primary;
			this.f_state_secondary = default_secondary;
		}

		/// <summary>
		/// Creates a Logger that gets the grid name and states from supplied functions.
		/// </summary>
		/// <param name="gridName">the name of the grid this Logger belongs to</param>
		/// <param name="className">the name of the class this Logger belongs to</param>
		/// <param name="default_primary">the primary state used when one is not supplied to log() or debugLog()</param>
		/// <param name="default_secondary">the secondary state used when one is not supplied to log() or debugLog()</param>
		public Logger(string className, Func<string> gridName, Func<string> default_primary = null, Func<string> default_secondary = null)
		{
			this.className = className;
			this.f_gridName = gridName;
			this.f_state_primary = default_primary;
			this.f_state_secondary = default_secondary;
		}

		private void deleteIfExists(string filename)
		{
			if (MyAPIGateway.Utilities.FileExistsInLocalStorage(filename, typeof(Logger)))
				MyAPIGateway.Utilities.DeleteFileInLocalStorage(filename, typeof(Logger));
		}

		private bool createLog()
		{
			//using (lock_log.AcquireExclusiveUsing())
			//{
			try
			{ deleteIfExists("Autopilot.log"); }
			catch { }
			try
			{ deleteIfExists("log.txt"); }
			catch { }
			for (int i = 0; i < 10; i++)
				if (logWriter == null)
					try { logWriter = MyAPIGateway.Utilities.WriteFileInLocalStorage("log-" + i + ".txt", typeof(Logger)); }
					catch { }
				else
					try
					{ deleteIfExists("log-" + i + ".txt"); }
					catch { }

			return logWriter != null;
			//}
		}

		private static FastResourceLock lock_log = new FastResourceLock();

		/// <summary>
		/// For logging INFO and lower severity, conditional on LOG_ENABLED in calling class. Sometimes used for WARNING.
		/// </summary>
		/// <param name="toLog">message to log</param>
		/// <param name="methodName">calling method</param>
		/// <param name="level">severity level</param>
		/// <param name="primaryState">class specific, appears before secondary state in log</param>
		/// <param name="secondaryState">class specific, appears before message in log</param>
		[System.Diagnostics.Conditional("LOG_ENABLED")]
		public void debugLog(string toLog, string methodName, severity level = severity.TRACE, string primaryState = null, string secondaryState = null)
		{ log(level, methodName, toLog, primaryState, secondaryState); }

		/// <summary>
		/// For logging WARNING and higher severity.
		/// </summary>
		/// <param name="toLog">message to log</param>
		/// <param name="methodName">calling method</param>
		/// <param name="level">severity level</param>
		/// <param name="primaryState">class specific, appears before secondary state in log</param>
		/// <param name="secondaryState">class specific, appears before message in log</param>
		public void log(string toLog, string methodName, severity level = severity.TRACE, string primaryState = null, string secondaryState = null)
		{ log(level, methodName, toLog, primaryState, secondaryState); }

		/// <summary>
		/// For logging WARNING and higher severity.
		/// </summary>
		/// <param name="level">severity level</param>
		/// <param name="methodName">calling method</param>
		/// <param name="toLog">message to log</param>
		/// <param name="primaryState">class specific, appears before secondary state in log</param>
		/// <param name="secondaryState">class specific, appears before message in log</param>
		public void log(severity level, string methodName, string toLog, string primaryState = null, string secondaryState = null)
		{
			if (closed)
				return;
			lock_log.AcquireExclusive();
			try
			{
				if (logWriter == null)
					if (MyAPIGateway.Utilities == null || !createLog())
						return; // cannot log

				if (f_gridName != null)
					gridName = f_gridName.Invoke();
				if (primaryState == null)
				{
					if (f_state_primary != null)
						default_primary = f_state_primary.Invoke();
					primaryState = default_primary;
				}
				if (secondaryState == null)
				{
					if (f_state_secondary != null)
						default_secondary = f_state_secondary.Invoke();
					secondaryState = default_secondary;
				}

				if (toLog == null)
					toLog = "no message";
				if (numLines >= maxNumLines)
					return;

				numLines++;
				appendWithBrackets(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss,fff"));
				appendWithBrackets(level.ToString());
				appendWithBrackets(gridName);
				appendWithBrackets(className);
				appendWithBrackets(methodName);
				appendWithBrackets(primaryState);
				appendWithBrackets(secondaryState);
				stringCache.Append(toLog);

				logWriter.WriteLine(stringCache);
				logWriter.Flush();
				stringCache.Clear();
			}
			catch { }
			finally { lock_log.ReleaseExclusive(); }
		}

		private void appendWithBrackets(string append)
		{
			if (append == null)
				append = String.Empty;
			stringCache.Append('[');
			stringCache.Append(append);
			stringCache.Append(']');
		}

		private static bool closed = false;

		/// <summary>
		/// closes the static log file
		/// </summary>
		private static void close()
		{
			if (logWriter == null)
				return;
			using (lock_log.AcquireExclusiveUsing())
			{
				logWriter.Flush();
				logWriter.Close();
				logWriter = null;
				closed = true;
			}
		}

		protected override void UnloadData()
		{ close(); }

		/// <summary>
		/// For a safe way to display a message as a notification, conditional on LOG_ENABLED. Logs a warning iff message cannot be displayed.
		/// </summary>
		/// <param name="message">the notification message</param>
		/// <param name="disappearTimeMs">time on screen, in milliseconds</param>
		/// <param name="level">severity level</param>
		/// <returns>true iff the message was displayed</returns>
		[System.Diagnostics.Conditional("LOG_ENABLED")]
		public void debugNotify(string message, int disappearTimeMs = 2000, severity level = severity.TRACE)
		{ 
			notify(message, disappearTimeMs, level);
		}
		
		/// <summary>
		/// For a safe way to display a message as a notification, not conditional. Logs a warning iff message cannot be displayed.
		/// </summary>
		/// <param name="message">the notification message</param>
		/// <param name="disappearTimeMs">time on screen, in milliseconds</param>
		/// <param name="level">severity level</param>
		/// <returns>true iff the message was displayed</returns>
		public void notify(string message, int disappearTimeMs = 2000, severity level = severity.TRACE)
		{
			MyFontEnum font = fontForSeverity(level);
			if (MyAPIGateway.Utilities != null)
				MyAPIGateway.Utilities.ShowNotification(message, disappearTimeMs, font);
			else
				log(severity.WARNING, "ShowNotificationDebug()", "MyAPIGateway.Utilities == null");
		}

		public enum severity : byte { OFF, FATAL, ERROR, WARNING, INFO, DEBUG, TRACE, ALL }
		
		private MyFontEnum fontForSeverity(severity level = severity.TRACE) {
			switch (level) {
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
