using System;
using System.Text;
using Rynchodon.Update;
using Sandbox.Game.Entities.Cube;
using Sandbox.Game.Gui;
using Sandbox.ModAPI;

namespace Rynchodon
{
	public static class IMyTerminalBlockExtensions
	{

		private static IMyTerminalBlock switchTo;

		public static void AppendCustomInfo(this IMyTerminalBlock block, string message)
		{
			Action<IMyTerminalBlock, StringBuilder> action = (termBlock, builder) => builder.Append(message);

			block.AppendingCustomInfo += action;
			block.RefreshCustomInfo();
			block.AppendingCustomInfo -= action;
		}

		/// <summary>
		/// Wait for input to finish, then switch control panel to the specified block.
		/// </summary>
		/// <param name="block">The block to switch to.</param>
		public static void SwitchTerminalTo(this IMyTerminalBlock block)
		{
			//Logger.debugLog("IMyTerminalBlockExtensions", "block: " + block.getBestName());
			UpdateManager.Unregister(1, SwitchTerminalWhenNoInput);
			UpdateManager.Register(1, SwitchTerminalWhenNoInput);
			switchTo = block;
		}

		private static void SwitchTerminalWhenNoInput()
		{
			if (MyAPIGateway.Input == null)
				Logger.debugLog("IMyTerminalBlockExtensions", "MyAPIGateway.Input == null", Logger.severity.FATAL);
			if (switchTo == null)
				Logger.debugLog("IMyTerminalBlockExtensions", "switchTo == null", Logger.severity.FATAL);

			if (MyAPIGateway.Input.IsAnyKeyPress() || MyAPIGateway.Input.IsAnyMouseOrJoystickPressed())
				return;

			//Logger.debugLog("IMyTerminalBlockExtensions", "switching to: " + switchTo.getBestName());
			UpdateManager.Unregister(1, SwitchTerminalWhenNoInput);
			MyGuiScreenTerminal.SwitchToControlPanelBlock((MyTerminalBlock)switchTo);
			switchTo = null;
		}

	}
}
