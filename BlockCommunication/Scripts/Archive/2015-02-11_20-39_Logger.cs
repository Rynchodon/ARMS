//	modified from script for Mission 01 by KSWH, id=315628704 on steam

using System;
using System.Collections.Generic;
//using System.Linq;
using System.Text;

//using Sandbox.Common;
using Sandbox.Definitions;
using Sandbox.ModAPI;

namespace Rynchodon
{
	/// <summary>
	/// Generates files to be read by GamutLogViewer
	/// using Log4J Pattern: [%date][%level][%Grid][%Class][%Method][%PriState][%SecState]%Message
	/// </summary>
	[Sandbox.Common.MySessionComponentDescriptor(Sandbox.Common.MyUpdateOrder.BeforeSimulation)]
	public class Logger //: Sandbox.Common.MySessionComponentBase
	{
		private static System.IO.TextWriter logWriter = null;
		private StringBuilder stringCache = new StringBuilder();

		private static int maxNumLines = 1000000;
		private static int numLines = 0;

		private string gridName, className;

		internal Logger(string gridName, string className)
		{
			this.gridName = gridName;
			this.className = className;

			if (logWriter == null)
				logWriter = MyAPIGateway.Utilities.WriteFileInLocalStorage("log.txt", typeof(Logger)); // was getting a NullReferenceException in ..cctor(), so we delay
		}

		internal void log(string toLog, string methodName, severity level, string primaryState = null, string secondaryState = null) { log(level, methodName, toLog, primaryState, secondaryState); }

		internal void log(severity level, string methodName, string toLog, string primaryState = null, string secondaryState = null)
		{
			if (logWriter == null || !canLog(level))
				return;

			numLines++;
			if (toLog == null)
				toLog = "no message";
			if (numLines > maxNumLines)
			{
				numLines = 0;
				log(severity.INFO, "log()", "reached maximum log length");
				minSeverity = severity.OFF;
				close();
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
		internal void close()
		{
			if (logWriter == null)
				return;
			logWriter.Close();
			logWriter = null;
		}

		protected void UnloadData()
		{ close(); }

		public enum severity : byte { OFF, FATAL, ERROR, WARNING, INFO, DEBUG, TRACE, ALL}
		private static severity minSeverity = severity.ALL;
		public static bool canLog(severity level)
		{
			return (level.CompareTo(minSeverity) <= 0);
		}
	}
}
