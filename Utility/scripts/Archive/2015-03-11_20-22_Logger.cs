#define DEBUG_LOGGING //remove on build

using System;
using System.Collections.Generic;
//using System.Linq;
using System.Text;

//using Sandbox.Common;
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

		private static int logNum = 0;

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

		private static FastResourceLock lock_log = new FastResourceLock();

		//[System.Diagnostics.Conditional("DEBUG_LOGGING")]
		public void debugLog(string toLog, string methodName, severity level = severity.TRACE, string primaryState = null, string secondaryState = null) 
		{ log(level, methodName, toLog, primaryState, secondaryState); }

		public void log(string toLog, string methodName, severity level = severity.TRACE, string primaryState = null, string secondaryState = null) 
		{ log(level, methodName, toLog, primaryState, secondaryState); }

		public void log(severity level, string methodName, string toLog, string primaryState = null, string secondaryState = null)
		{
			try
			{
				if (logWriter == null)
				{
					if (MyAPIGateway.Utilities == null)
						return; // cannot log
					try { logWriter = MyAPIGateway.Utilities.WriteFileInLocalStorage("log-" + logNum++ + ".txt", typeof(Logger)); }// was getting a NullReferenceException in ..cctor(), so we delay
					catch { }
				}

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
					new Logger(null, "Logger").log(severity.INFO, "log()", "reached maximum log length");
					close();
					return;
				}

				lock_log.AcquireExclusive();
				try
				{
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
				finally
				{
					lock_log.ReleaseExclusive();
				}
			}
			catch { } // file is probably in use or something
		}

		private void appendWithBrackets(string append)
		{
			if (append == null)
				append = String.Empty;
			stringCache.Append('[');
			stringCache.Append(append);
			stringCache.Append(']');
		}

		/// <summary>
		/// closes the static log file
		/// </summary>
		private void close()
		{
			if (logWriter == null)
				return;
			logWriter.Close();
			logWriter = null;
		}

		protected override void UnloadData()
		{ close(); }

		public enum severity : byte { OFF, FATAL, ERROR, WARNING, INFO, DEBUG, TRACE, ALL}
	}
}
