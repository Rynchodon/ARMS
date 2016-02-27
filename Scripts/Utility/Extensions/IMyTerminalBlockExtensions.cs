using System; // (partial) from mscorlib.dll
using System.Text; // from mscorlib.dll
using Sandbox.ModAPI; // from Sandbox.Common.dll

namespace Rynchodon
{
	public static class IMyTerminalBlockExtensions
	{

		public static void AppendCustomInfo(this IMyTerminalBlock block, string message)
		{
			Action<IMyTerminalBlock, StringBuilder> action = (termBlock, builder) => builder.Append(message);

			block.AppendingCustomInfo += action;
			block.RefreshCustomInfo();
			block.AppendingCustomInfo -= action;
		}

	}
}
