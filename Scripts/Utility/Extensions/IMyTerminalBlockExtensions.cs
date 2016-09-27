using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Text;
using Rynchodon.Update;
using Sandbox.Game.Entities.Cube;
using Sandbox.Game.Gui;
using Sandbox.ModAPI;
using VRage.Input;

namespace Rynchodon
{
	public static class IMyTerminalBlockExtensions
	{

		private class StaticVariables
		{
			public IMyTerminalBlock switchTo;
			public MyKeys[] importantKeys = new MyKeys[] { MyKeys.Enter, MyKeys.Space };
			public List<MyKeys> pressedKeys = new List<MyKeys>();
		}

		private static StaticVariables Static = new StaticVariables();

		static IMyTerminalBlockExtensions()
		{
			MyAPIGateway.Entities.OnCloseAll += Entities_OnCloseAll;
		}

		private static void Entities_OnCloseAll()
		{
			MyAPIGateway.Entities.OnCloseAll -= Entities_OnCloseAll;
			Static = null;
		}

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
		public static void SwitchTerminalTo(this IMyTerminalBlock block, [CallerMemberName] string caller = null)
		{
			if (Static == null)
				return;

			//Logger.debugLog("IMyTerminalBlockExtensions", "block: " + block.getBestName());
			Logger.DebugLog("null block from " + caller, Logger.severity.FATAL, condition: block == null);
			UpdateManager.Unregister(1, SwitchTerminalWhenNoInput);
			UpdateManager.Register(1, SwitchTerminalWhenNoInput);
			Static.switchTo = block;

			//Static.pressedKeys.Clear();
			//MyAPIGateway.Input.GetPressedKeys(Static.pressedKeys);
			//Logger.DebugLog("IMyTerminalBlockExtensions", "pressed: " + string.Join(", ", Static.pressedKeys));
		}

		private static void SwitchTerminalWhenNoInput()
		{
			if (Static == null)
				return;

			Logger.DebugLog("MyAPIGateway.Input == null", Logger.severity.FATAL, condition: MyAPIGateway.Input == null);
			Logger.DebugLog("switchTo == null", Logger.severity.FATAL, condition: Static.switchTo == null);

			if (MyAPIGateway.Input.IsAnyMouseOrJoystickPressed())
				return;

			if (MyAPIGateway.Input.IsAnyKeyPress())
			{
				Static.pressedKeys.Clear();
				MyAPIGateway.Input.GetPressedKeys(Static.pressedKeys);
				foreach (MyKeys key in Static.importantKeys)
					if (Static.pressedKeys.Contains(key))
						return;
			}

			//Logger.debugLog("IMyTerminalBlockExtensions", "switching to: " + switchTo.getBestName());
			UpdateManager.Unregister(1, SwitchTerminalWhenNoInput);
			MyGuiScreenTerminal.SwitchToControlPanelBlock((MyTerminalBlock)Static.switchTo);
			Static.switchTo = null;
		}

	}
}
