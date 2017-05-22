#define TRACE

using System.Diagnostics;
using System.Runtime.CompilerServices;
using Rynchodon.Autopilot.Data;
using VRage.Game.ModAPI;
using VRage.ModAPI;
using System;

namespace Rynchodon.Utility
{

	public static class LogableFrom
	{
		public static Logable Pseudo(PseudoBlock pseudo, string SecondaryState = null)
		{
			return new Logable(pseudo.Grid?.nameWithId(), pseudo?.DisplayName, SecondaryState);
		}
	}

	/// <summary>
	/// Latest attempt to make logging lighter. Classes define a property that creates a single use Logable.
	/// </summary>
	public struct Logable
	{

		public readonly string Context, PrimaryState, SecondaryState;

		public Logable(string Context, string PrimaryState = null, string SecondaryState = null)
		{
			this.Context = Context;
			this.PrimaryState = PrimaryState;
			this.SecondaryState = SecondaryState;
		}
		
		public Logable(IMyEntity entity)
		{
			if (entity == null)
			{
				Context = PrimaryState = SecondaryState = null;
			}
			else if (entity is IMyCubeBlock)
			{
				IMyCubeBlock block = (IMyCubeBlock)entity;
				Context = block.CubeGrid.nameWithId();
				PrimaryState = block.DefinitionDisplayNameText;
				SecondaryState = block.nameWithId();
			}
			else
			{
				Context = entity.nameWithId();
				PrimaryState = SecondaryState = null;
			}
		}

		public Logable(IMyEntity entity, string SecondaryState)
		{
			this.SecondaryState = SecondaryState;
			if (entity == null)
			{
				Context = PrimaryState = null;
			}
			else if (entity is IMyCubeBlock)
			{
				IMyCubeBlock block = (IMyCubeBlock)entity;
				Context = block.CubeGrid.nameWithId();
				PrimaryState = block.nameWithId();
			}
			else
			{
				Context = entity.nameWithId();
				PrimaryState = null;
			}
		}

		[Conditional("TRACE")]
		public void TraceLog(string toLog, Logger.severity level = Logger.severity.TRACE, bool condition = true, [CallerFilePath] string filePath = null, [CallerMemberName] string member = null, [CallerLineNumber] int lineNumber = 0)
		{
			if (condition)
				Logger.TraceLog(toLog, level, Context, PrimaryState, SecondaryState, condition, filePath, member, lineNumber);
		}

		[Conditional("TRACE")]
		public void TraceLog(Func<string> toLog, Logger.severity level = Logger.severity.TRACE, bool condition = true, [CallerFilePath] string filePath = null, [CallerMemberName] string member = null, [CallerLineNumber] int lineNumber = 0)
		{
			if (condition)
				Logger.TraceLog(toLog.Invoke(), level, Context, PrimaryState, SecondaryState, condition, filePath, member, lineNumber);
		}

		[Conditional("DEBUG")]
		public void DebugLog(string toLog, Logger.severity level = Logger.severity.TRACE, bool condition = true, [CallerFilePath] string filePath = null, [CallerMemberName] string member = null, [CallerLineNumber] int lineNumber = 0)
		{
			if (condition)
				Logger.DebugLog(toLog, level, Context, PrimaryState, SecondaryState, condition, filePath, member, lineNumber);
		}

		[Conditional("DEBUG")]
		public void DebugLog(Func<string> toLog, Logger.severity level = Logger.severity.TRACE, bool condition = true, [CallerFilePath] string filePath = null, [CallerMemberName] string member = null, [CallerLineNumber] int lineNumber = 0)
		{
			if (condition)
				Logger.DebugLog(toLog.Invoke(), level, Context, PrimaryState, SecondaryState, condition, filePath, member, lineNumber);
		}

		[Conditional("PROFILE")]
		public void ProfileLog(string toLog, Logger.severity level = Logger.severity.TRACE, bool condition = true, [CallerFilePath] string filePath = null, [CallerMemberName] string member = null, [CallerLineNumber] int lineNumber = 0)
		{
			if (condition)
				Logger.ProfileLog(toLog, level, Context, PrimaryState, SecondaryState, condition, filePath, member, lineNumber);
		}

		public void AlwaysLog(string toLog, Logger.severity level = Logger.severity.TRACE, bool condition = true, [CallerFilePath] string filePath = null, [CallerMemberName] string member = null, [CallerLineNumber] int lineNumber = 0)
		{
			if (condition)
				Logger.AlwaysLog(toLog, level, Context, PrimaryState, SecondaryState, filePath, member, lineNumber);
		}

		[Conditional("TRACE")]
		public void EnteredMember([CallerFilePath] string filePath = null, [CallerMemberName] string member = null, [CallerLineNumber] int lineNumber = 0)
		{
			Logger.TraceLog("entered member", Logger.severity.TRACE, Context, PrimaryState, SecondaryState, true, filePath, member, lineNumber);
		}

		[Conditional("TRACE")]
		public void LeavingMember([CallerFilePath] string filePath = null, [CallerMemberName] string member = null, [CallerLineNumber] int lineNumber = 0)
		{
			Logger.TraceLog("leaving member", Logger.severity.TRACE, Context, PrimaryState, SecondaryState, true, filePath, member, lineNumber);
		}

	}
}
