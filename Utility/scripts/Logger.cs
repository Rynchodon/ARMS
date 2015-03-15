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
	/// <summary>
	/// Generates files to be read by GamutLogViewer
	/// using Log4J Pattern: [%date][%level][%Grid][%Class][%Method][%PriState][%SecState]%Message
	/// </summary>
	[Sandbox.Common.MySessionComponentDescriptor(Sandbox.Common.MyUpdateOrder.NoUpdate)]
	public class Logger : Sandbox.Common.MySessionComponentBase
	{
		private static System.IO.TextWriter logWriter = null;
		private StringBuilder stringCache = new StringBuilder();

		private static int maxNumLines = 1000000;
		private static int numLines = 0;

		/// <summary>
		/// take precedence
		/// </summary>
		private Func<string> f_gridName, state_primary, state_secondary;
		private string gridName, className, default_primary, default_secondary;

		/// <summary>
		/// needed for MySessionComponentBase, not useful for logging
		/// </summary>
		public Logger() { }

		public Logger(string gridName, string className)
		{
			this.gridName = gridName;
			this.className = className;
		}

		public Logger(string gridName, string className, string default_primary, string default_secondary = null)
			: this(gridName, className)
		{
			this.default_primary = default_primary;
			this.default_secondary = default_secondary;
		}

		public Logger(string className, Func<string> gridName, Func<string> primary, Func<string> secondary)
		{
			this.className = className;
			this.f_gridName = gridName;
			this.state_primary = primary;
			this.state_secondary = secondary;
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
		/// For logging INFO and lower severity.
		/// </summary>
		/// <param name="toLog"></param>
		/// <param name="methodName"></param>
		/// <param name="level"></param>
		/// <param name="primaryState"></param>
		/// <param name="secondaryState"></param>
		[System.Diagnostics.Conditional("LOG_ENABLED")]
		public void debugLog(string toLog, string methodName, severity level = severity.TRACE, string primaryState = null, string secondaryState = null) 
		{ log(level, methodName, toLog, primaryState, secondaryState); }

		public void log(string toLog, string methodName, severity level = severity.TRACE, string primaryState = null, string secondaryState = null) 
		{ log(level, methodName, toLog, primaryState, secondaryState); }

		/// <summary>
		/// Only call directly for logging WARNING and higher severity.
		/// </summary>
		/// <param name="level"></param>
		/// <param name="methodName"></param>
		/// <param name="toLog"></param>
		/// <param name="primaryState"></param>
		/// <param name="secondaryState"></param>
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
					if (state_primary != null)
						default_primary = state_primary.Invoke();
					primaryState = default_primary;
				}
				if (secondaryState == null)
				{
					if (state_secondary != null)
						default_secondary = state_secondary.Invoke();
					secondaryState = default_secondary;
				}

				if (toLog == null)
					toLog = "no message";
				if (numLines >= maxNumLines)
				{
					numLines = 0;
					//new Logger(null, "Logger").log(severity.INFO, "log()", "reached maximum log length");
					close();
					return;
				}

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

		[System.Diagnostics.Conditional("LOG_ENABLED")]
		public static void ShowNotification(string message, int disappearTimeMs = 2000, MyFontEnum font = MyFontEnum.White)
		{
			if (MyAPIGateway.Utilities != null)
				MyAPIGateway.Utilities.ShowNotification(message, disappearTimeMs, font);
		}

		public enum severity : byte { OFF, FATAL, ERROR, WARNING, INFO, DEBUG, TRACE, ALL}
	}
}
