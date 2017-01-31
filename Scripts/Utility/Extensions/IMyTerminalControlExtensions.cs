using System;
using Sandbox.ModAPI;
using Sandbox.ModAPI.Interfaces.Terminal;

namespace Rynchodon
{
	public static class IMyTerminalControlExtensions
	{

		public static void SetEnabledAndVisible(this IMyTerminalControl control, Func<IMyTerminalBlock, bool> function)
		{
			control.Enabled = function;
			control.Visible = function;
		}

	}
}
