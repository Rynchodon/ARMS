using System;
using System.Collections.Generic;
using System.Reflection;
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
			public MyKeys[] importantKeys = new MyKeys[] { MyKeys.Enter, MyKeys.Space };
			public List<MyKeys> pressedKeys = new List<MyKeys>();
		}

		private static StaticVariables Static = new StaticVariables();

		public static void AppendCustomInfo(this IMyTerminalBlock block, string message)
		{
			Action<IMyTerminalBlock, StringBuilder> action = (termBlock, builder) => builder.Append(message);

			block.AppendingCustomInfo += action;
			block.UpdateCustomInfo();
			block.AppendingCustomInfo -= action;
		}

		/// <summary>
		/// Refresh custom info & raise properties changed.
		/// </summary>
		public static void UpdateCustomInfo(this IMyTerminalBlock block)
		{
			block.RefreshCustomInfo();
			((MyTerminalBlock)block).RaisePropertiesChanged();
		}

		/// <summary>
		/// Wait for input to finish, then switch control panel to the currently selected block(s).
		/// </summary>
		/// <param name="block">Not used.</param>
		public static void RebuildControls(this IMyTerminalBlock block)
		{
			if (Globals.WorldClosed)
				return;

			if (MyAPIGateway.Gui.GetCurrentScreen != VRage.Game.ModAPI.MyTerminalPageEnum.ControlPanel)
			{
				Logger.DebugLog("Control panel not open");
				return;
			}

			UpdateManager.Unregister(1, SwitchTerminalWhenNoInput);
			UpdateManager.Register(1, SwitchTerminalWhenNoInput);
		}

		private static void SwitchTerminalWhenNoInput()
		{
			if (Globals.WorldClosed)
				return;

			Logger.DebugLog("MyAPIGateway.Input == null", Logger.severity.FATAL, condition: MyAPIGateway.Input == null);

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

			UpdateManager.Unregister(1, SwitchTerminalWhenNoInput);

			Type type = typeof(MyGuiScreenTerminal);
			object obj = type.GetField("m_instance", BindingFlags.Static | BindingFlags.NonPublic)?.GetValue(null);
			Logger.DebugLog("m_instance not found", Logger.severity.ERROR, condition: obj == null);

			obj = type.GetField("m_controllerControlPanel", BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(obj);
			Logger.DebugLog("m_controllerControlPanel not found", Logger.severity.ERROR, condition: obj == null);

			type = type.Assembly.GetType("Sandbox.Game.Gui.MyTerminalControlPanel", true);
			Logger.DebugLog("MyTerminalControlPanel not found", Logger.severity.ERROR, condition: type == null);

			MethodInfo method = type.GetMethod("SelectBlocks", BindingFlags.Instance | BindingFlags.NonPublic);
			if (method == null)
				Logger.AlwaysLog("SelectBlocks not found", Logger.severity.ERROR);
			else
				method.Invoke(obj, null);
		}

	}
}
