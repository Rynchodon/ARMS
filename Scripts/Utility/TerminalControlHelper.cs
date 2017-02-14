using System;
using System.Reflection;
using Sandbox.Game.Entities.Cube;
using Sandbox.Game.Gui;

namespace Rynchodon
{
	public static class TerminalControlHelper
	{

		/// <summary>
		/// Force vanilla terminal controls to be created for a given type of block. Always invoke this before adding controls.
		/// MyTerminalControlFactory.EnsureControlsAreCreated does not work, use this method.
		/// </summary>
		public static void EnsureTerminalControlCreated<TBlock>() where TBlock : MyTerminalBlock, new()
		{
			if (MyTerminalControlFactory.AreControlsCreated<TBlock>())
				return;

			Type blockType = typeof(TBlock);
			MethodInfo method = blockType.GetMethod("CreateTerminalControls", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
			if (method != null)
			{
				if (method.GetParameters().Length != 0)
				{
					Logger.AlwaysLog("Method has parameters: " + method.Name + " of " + blockType.FullName, Logger.severity.ERROR);
					return;
				}

				Logger.DebugLog("Invoking CreateTerminalControls for " + typeof(TBlock).Name, Logger.severity.DEBUG);
				method.Invoke(new TBlock(), null);
				return;
			}

			method = blockType.GetMethod("CreateTerminalControls", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
			if (method != null)
			{
				if (method.GetParameters().Length != 0)
				{
					Logger.AlwaysLog("Method has parameters: " + method.Name + " of " + blockType.FullName, Logger.severity.ERROR);
					return;
				}

				Logger.DebugLog("Invoking CreateTerminalControls for " + typeof(TBlock).Name, Logger.severity.DEBUG);
				method.Invoke(null, null);
				return;
			}

			Logger.DebugLog("No CreateTerminalControls method for " + typeof(TBlock).Name, Logger.severity.DEBUG);
		}

	}
}
