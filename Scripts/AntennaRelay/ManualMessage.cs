using System;
using System.Collections.Generic;
using System.Text;
using Sandbox.Game.Entities.Cube;
using Sandbox.Game.Gui;
using Sandbox.ModAPI;
using Sandbox.ModAPI.Interfaces.Terminal;
using VRage.Game.ModAPI;
using VRage.Utils;

namespace Rynchodon.AntennaRelay
{
	/// <summary>
	/// For players sending a message through the terminal.
	/// </summary>
	class ManualMessage
	{

		private class StaticVariables
		{
			public MyTerminalControlButton<MyFunctionalBlock>
				SendMessageButton = new MyTerminalControlButton<MyFunctionalBlock>("ManualMessageId", MyStringId.GetOrCompute("Send Message"),
					MyStringId.GetOrCompute("Send a message to an Autopilot or Programmable block"), SendMessage) { SupportsMultipleBlocks = false },

				AbortMessageButton = new MyTerminalControlButton<MyFunctionalBlock>("Abort", MyStringId.GetOrCompute("Abort"),
					MyStringId.GetOrCompute("Return to main terminal screen without sending a message"), Abort) { SupportsMultipleBlocks = false };

			public MyTerminalControlTextbox<MyFunctionalBlock>
				TargetShipName = new MyTerminalControlTextbox<MyFunctionalBlock>("TargetShipName", MyStringId.GetOrCompute("Ship Name(s)"),
					MyStringId.GetOrCompute("The name of the ship(s) that will receive the message")) { SupportsMultipleBlocks = false, Getter = GetTargetShipName, Setter = SetTargetShipName },

				TargetBlockName = new MyTerminalControlTextbox<MyFunctionalBlock>("TargetBlockName", MyStringId.GetOrCompute("Block Name(s)"),
					MyStringId.GetOrCompute("The name of the block(s) that will receive the message")) { SupportsMultipleBlocks = false, Getter = GetTargetBlockName, Setter = SetTargetBlockName },

				Message = new MyTerminalControlTextbox<MyFunctionalBlock>("MessageToSend", MyStringId.GetOrCompute("Message"),
					MyStringId.GetOrCompute("The message to send")) { SupportsMultipleBlocks = false, Getter = GetMessage, Setter = SetMessage };
		}

		private static StaticVariables Static;

		[OnWorldLoad]
		private static void Initialize()
		{
			MyTerminalControls.Static.CustomControlGetter += CustomHandler;
			Static = new StaticVariables();
		}

		[OnWorldClose]
		private static void Unload()
		{
			MyTerminalControls.Static.CustomControlGetter -= CustomHandler;
			Static = null;
		}

		public static void CustomHandler(IMyTerminalBlock block, List<IMyTerminalControl> controlList)
		{
			ManualMessage instance;
			if (!Registrar.TryGetValue(block.EntityId, out instance))
				return;

			if (instance.m_sending)
			{
				controlList.Clear();
				controlList.Add(Static.AbortMessageButton);
				controlList.Add(Static.TargetShipName);
				controlList.Add(Static.TargetBlockName);
				controlList.Add(Static.Message);
				controlList.Add(Static.SendMessageButton);
			}
			else
			{
				controlList.Add(Static.SendMessageButton);
			}
		}

		private static void SendMessage(MyFunctionalBlock block)
		{
			ManualMessage instance;
			if (!Registrar.TryGetValue(block.EntityId, out instance))
				throw new ArgumentException("block id not found in registrar");

			block.RebuildControls();

			if (instance.m_sending)
			{
				if (instance.m_targetShipName.Length < 3)
				{
					block.AppendCustomInfo("Ship Name(s) must be at least 3 characters");
					return;
				}
				if (instance.m_targetBlockName.Length < 3)
				{
					block.AppendCustomInfo("Block Name(s) must be at least 3 characters");
					return;
				}

				int count = Message.CreateAndSendMessage(block.EntityId, instance.m_targetShipName.ToString(), instance.m_targetBlockName.ToString(), instance.m_message.ToString());
				if (MyAPIGateway.Session.Player != null)
					(block as IMyTerminalBlock).AppendCustomInfo("Sent message to " + count + " block" + (count == 1 ? "" : "s"));

				instance.m_sending = false;
			}
			else
			{
				instance.m_sending = true;
			}
		}

		private static void Abort(MyFunctionalBlock block)
		{
			ManualMessage instance;
			if (!Registrar.TryGetValue(block.EntityId, out instance))
				throw new ArgumentException("block id not found in registrar");

			instance.m_sending = false;
			block.RebuildControls();
		}

		#region Getter & Setter

		private static StringBuilder GetTargetShipName(MyTerminalBlock block)
		{
			ManualMessage instance;
			if (!Registrar.TryGetValue(block.EntityId, out instance))
				throw new ArgumentException("block id not found in registrar");

			return instance.m_targetShipName;
		}

		private static void SetTargetShipName(MyTerminalBlock block, StringBuilder value)
		{
			ManualMessage instance;
			if (!Registrar.TryGetValue(block.EntityId, out instance))
				throw new ArgumentException("block id not found in registrar");

			instance.m_targetShipName = value;
		}

		private static StringBuilder GetTargetBlockName(MyTerminalBlock block)
		{
			ManualMessage instance;
			if (!Registrar.TryGetValue(block.EntityId, out instance))
				throw new ArgumentException("block id not found in registrar");

			return instance.m_targetBlockName;
		}

		private static void SetTargetBlockName(MyTerminalBlock block, StringBuilder value)
		{
			ManualMessage instance;
			if (!Registrar.TryGetValue(block.EntityId, out instance))
				throw new ArgumentException("block id not found in registrar");

			instance.m_targetBlockName = value;
		}

		private static StringBuilder GetMessage(MyTerminalBlock block)
		{
			ManualMessage instance;
			if (!Registrar.TryGetValue(block.EntityId, out instance))
				throw new ArgumentException("block id not found in registrar");

			return instance.m_message;
		}

		private static void SetMessage(MyTerminalBlock block, StringBuilder value)
		{
			ManualMessage instance;
			if (!Registrar.TryGetValue(block.EntityId, out instance))
				throw new ArgumentException("block id not found in registrar");

			instance.m_message = value;
		}

		#endregion Getter & Setter

		private readonly IMyCubeBlock m_block;

		private bool m_sending;
		private StringBuilder m_targetShipName = new StringBuilder(), m_targetBlockName = new StringBuilder(), m_message = new StringBuilder();

		public ManualMessage(IMyCubeBlock block)
		{
			m_block = block;

			Registrar.Add(block, this);
		}

	}
}
