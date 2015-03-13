//	modified from script for Mission 01 by KSWH, id=315628704 on steam

using System;
using System.Collections.Generic;
//using System.Linq;
using System.Text;

//using Sandbox.Common;
using Sandbox.ModAPI;

namespace Rynchodon
{
	/// <summary>
	/// Generates files to be read by GamutLogViewer
	/// using Log4J Pattern: [%date][%level][%Grid][%Class][%Method][%PriState][%SecState]%Message
	/// </summary>
	public abstract class Logger
	{
		protected static string logFile;
		protected static System.IO.TextWriter logWriter = null;
		private StringBuilder stringCache = new StringBuilder();

		private static int maxNumLines = 1000000;
		private static int numLines = 0;

		protected string gridName, className;

		internal void log(string toLog, string methodName, severity level, string primaryState = null, string secondaryState = null) { log(level, methodName, toLog, primaryState, secondaryState); }

		internal void log(severity level, string methodName, string toLog, string primaryState = null, string secondaryState = null)
		{
			if (logWriter == null) return;
			if (!canLog(level)) return;

			numLines++;
			if (toLog == null)
				toLog = "no message";
			if (numLines > maxNumLines)
			{
				numLines = 0;
				log(severity.INFO, "log()", "reached maximum log length");
				minSeverity = severity.OFF;
				Close();
				return;
			}

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
		internal static void Close()
		{
			logWriter.Close();
			logWriter = null;
		}

		public enum severity : byte { OFF, FATAL, ERROR, WARNING, INFO, DEBUG, TRACE, ALL}
		private static severity minSeverity = severity.ALL;
		public static bool canLog(severity level)
		{
			return (level.CompareTo(minSeverity) <= 0);
		}

		///// <summary>
		///// erases the old log and starts a new one with a maximum length of 100 000 lines.
		///// use severity.OFF to stop logging
		///// </summary>
		///// <param name="level">minimum severity level to log</param>
		//internal static void startLogging(severity level)
		//{
		//	minSeverity = level;
		//	numLines = 0;
		//	maxNumLines = 100000;
		//	Close();
		//	logWriter = MyAPIGateway.Utilities.WriteFileInLocalStorage(logFile, typeof(AutopilotLogger));
		//}
	}
}
