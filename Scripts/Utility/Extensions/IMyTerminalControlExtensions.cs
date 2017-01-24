using System;
using System.Text;
using Rynchodon.Utility.Network;
using Sandbox.Game.Entities.Cube;
using Sandbox.Game.Gui;
using Sandbox.Game.Screens.Terminal.Controls;
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

		/// <summary>
		/// Add a getter and a setter for a MyTerminalValueControl where the value will be synced by EntityValue.
		/// </summary>
		/// <typeparam name="TBlock">The type of block</typeparam>
		/// <typeparam name="TData">The type of value</typeparam>
		/// <typeparam name="TScript">The type of script that has the EntityValue.</typeparam>
		/// <param name="control">The control to add the getter and setter to.</param>
		/// <param name="entityValue">Function for getting the EntityValue.</param>
		public static void AddGetSetEntityValue<TBlock, TData, TScript>(this MyTerminalValueControl<TBlock, TData> control, Func<TScript, EntityValue<TData>> entityValue) where TBlock : MyTerminalBlock
		{
			control.Getter = (block) => {
				TScript script;
				if (!Registrar.TryGetValue(block.EntityId, out script))
				{
					if (Globals.WorldClosed)
						return control.GetDefaultValue(block);
					Logger.AlwaysLog("block not found in Registrar: " + block.nameWithId() + ", script type: " + typeof(TScript).FullName);
					return control.GetDefaultValue(block);
				}
				return entityValue(script).Value;
			};
			control.Setter = (block, value) => {
				TScript script;
				if (!Registrar.TryGetValue(block.EntityId, out script))
				{
					if (Globals.WorldClosed)
						return;
					Logger.AlwaysLog("block not found in Registrar: " + block.nameWithId() + ", script type: " + typeof(TScript).FullName);
					return;
				}
				entityValue(script).Value = value;
			};
		}

		/// <summary>
		/// Add a getter and a setter for a MyTerminalControlTextbox where the value will be synced by EntityValue.
		/// </summary>
		/// <typeparam name="TBlock">The type of block</typeparam>
		/// <typeparam name="TScript">The type of script that has the EntityValue.</typeparam>
		/// <param name="control">The control to add the getter and setter to.</param>
		/// <param name="entityValue">Function for getting the EntityValue.</param>
		/// <remarks>
		/// duplicate for MyTerminalControlTextbox because it hides members of MyTerminalValueControl
		/// </remarks>
		public static void AddGetSetEntityValue<TBlock, TScript>(this MyTerminalControlTextbox<TBlock> control, Func<TScript, EntityValue<StringBuilder>> entityValue) where TBlock : MyTerminalBlock
		{
			control.Getter = (block) => {
				Logger.DebugLog("getting for: " + block.nameWithId() + ", script type: " + typeof(TScript).FullName);
				TScript script;
				if (!Registrar.TryGetValue(block.EntityId, out script))
				{
					if (Globals.WorldClosed)
						return control.GetDefaultValue(block);
					Logger.AlwaysLog("block not found in Registrar: " + block.nameWithId() + ", script type: " + typeof(TScript).FullName);
					return control.GetDefaultValue(block);
				}
				return entityValue(script).Value;
			};
			control.Setter = (block, value) => {
				Logger.DebugLog("setting for: " + block.nameWithId() + ", script type: " + typeof(TScript).FullName);
				TScript script;
				if (!Registrar.TryGetValue(block.EntityId, out script))
				{
					if (Globals.WorldClosed)
						return;
					Logger.AlwaysLog("block not found in Registrar: " + block.nameWithId() + ", script type: " + typeof(TScript).FullName);
					return;
				}
				entityValue(script).Value = value;
			};
		}
	}
}
