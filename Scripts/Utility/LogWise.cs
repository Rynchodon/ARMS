#define TRACE // trace must be defined in calling class as well

using System.Diagnostics;
using System.Runtime.CompilerServices;

namespace Rynchodon
{
	public abstract class LogWise
	{

		protected virtual string GetContext() { return null; }
		protected virtual string GetPrimary() { return null; }
		protected virtual string GetSecondary() { return null; }

		[Conditional("TRACE")]
		protected void traceLog(string toLog, Logger.severity level = Logger.severity.TRACE, bool condition = true, [CallerFilePath] string filePath = null, [CallerMemberName] string member = null, [CallerLineNumber] int lineNumber = 0)
		{
			if (condition)
				Logger.TraceLog(toLog, level, GetContext(), GetPrimary(), GetSecondary(), condition, filePath, member, lineNumber);
		}

		[Conditional("DEBUG")]
		protected void debugLog(string toLog, Logger.severity level = Logger.severity.TRACE, bool condition = true, [CallerFilePath] string filePath = null, [CallerMemberName] string member = null, [CallerLineNumber] int lineNumber = 0)
		{
			if (condition)
				Logger.DebugLog(toLog, level, GetContext(), GetPrimary(), GetSecondary(), condition, filePath, member, lineNumber);
		}

		[Conditional("PROFILE")]
		protected void profileLog(string toLog, Logger.severity level = Logger.severity.TRACE, bool condition = true, [CallerFilePath] string filePath = null, [CallerMemberName] string member = null, [CallerLineNumber] int lineNumber = 0)
		{
			if (condition)
				Logger.ProfileLog(toLog, level, GetContext(), GetPrimary(), GetSecondary(), condition, filePath, member, lineNumber);
		}

		protected void alwaysLog(string toLog, Logger.severity level = Logger.severity.TRACE, bool condition = true, [CallerFilePath] string filePath = null, [CallerMemberName] string member = null, [CallerLineNumber] int lineNumber = 0)
		{
			if (condition)
				Logger.AlwaysLog(toLog, level, GetContext(), GetPrimary(), GetSecondary(), filePath, member, lineNumber);
		}

	}
}
