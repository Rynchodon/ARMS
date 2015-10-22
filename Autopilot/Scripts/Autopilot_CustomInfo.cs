using System.Text;

using Sandbox.ModAPI;

namespace Rynchodon.Autopilot
{
	
	/// <summary>
	/// Received custom info updates from server.
	/// </summary>
	public class Autopilot_CustomInfo
	{

		private static Logger s_logger = new Logger("Autopilot_CustomInfo");

		static Autopilot_CustomInfo()
		{
			MyAPIGateway.Entities.OnCloseAll += Entities_OnCloseAll;
			MyAPIGateway.Multiplayer.RegisterMessageHandler(ShipController_Autopilot.ModId_CustomInfo, MessageHandler);
			s_logger.debugLog("Registerd for messages", "Autopilot_CustomInfo()", Logger.severity.DEBUG);
		}

		public static void MessageHandler(byte[] message)
		{
			//s_logger.debugLog("Received a message, length: " + message.Length, "MessageHandler()", Logger.severity.DEBUG);

			int pos = 0;
			long entityId = ByteConverter.GetLong(message, ref pos);
			Autopilot_CustomInfo recipient;
			if (!Registrar.TryGetValue(entityId, out recipient))
			{
				s_logger.debugLog("Recipient block is closed: " + entityId, "MessageHandler()", Logger.severity.INFO);
				return;
			}
			recipient.MessageHandler(message, ref pos);
		}

		private static void Entities_OnCloseAll()
		{
			MyAPIGateway.Entities.OnCloseAll -= Entities_OnCloseAll;
			MyAPIGateway.Multiplayer.UnregisterMessageHandler(ShipController_Autopilot.ModId_CustomInfo, MessageHandler);
			s_logger.debugLog("Unregisterd for messages", "Autopilot_CustomInfo()", Logger.severity.DEBUG);
			s_logger = null;
		}

		private readonly Logger m_logger;
		private readonly IMyTerminalBlock m_block;

		private StringBuilder m_customInfo = new StringBuilder();

		public Autopilot_CustomInfo(IMyCubeBlock block)
		{
			this.m_logger = new Logger("Autopilot_CustomInfo", block);
			this.m_block = block as IMyTerminalBlock;

			m_block.AppendingCustomInfo += AppendingCustomInfo;

			Registrar.Add(block, this);
			m_logger.debugLog("Initialized", "Autopilot_CustomInfo()", Logger.severity.INFO);
		}

		private void MessageHandler(byte[] message, ref int pos)
		{
			m_logger.debugLog("Received a message, length: " + message.Length, "MessageHandler()", Logger.severity.DEBUG);

			m_customInfo.Clear();
			while (pos < message.Length)
				m_customInfo.Append(ByteConverter.GetChar(message, ref pos));

			m_logger.debugLog("Message:\n" + m_customInfo, "MessageHandler()");

			m_block.RefreshCustomInfo();
		}

		private void AppendingCustomInfo(IMyTerminalBlock arg1, StringBuilder arg2)
		{
			arg2.Append(m_customInfo);
		}

	}

}
