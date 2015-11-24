using System;
using System.Collections.Generic;

using Sandbox.ModAPI;

namespace Rynchodon.Utility.Network
{

	public static class NetworkClient
	{
		public const ushort ModId = 6995;

		public enum SubModule : byte { BlockSync }

		public static Dictionary<SubModule, Action<byte[]>> Handlers = new Dictionary<SubModule, Action<byte[]>>();

		private static Logger s_logger = new Logger("NetworkClient");

		static NetworkClient()
		{
			MyAPIGateway.Multiplayer.RegisterMessageHandler(ModId, MessageHandler);
			MyAPIGateway.Entities.OnCloseAll += Entities_OnCloseAll;
		}

		private static void Entities_OnCloseAll()
		{
			MyAPIGateway.Entities.OnCloseAll -= Entities_OnCloseAll;
			MyAPIGateway.Multiplayer.UnregisterMessageHandler(ModId, MessageHandler);
			Handlers = null;
			s_logger = null;
		}

		private static void MessageHandler(byte[] received)
		{
			Action<byte[]> handler;
			if (Handlers.TryGetValue((SubModule)received[0], out handler))
				handler(received);
			else
				s_logger.alwaysLog("No handler for " + (SubModule)received[0], "MessageHandler()", Logger.severity.WARNING);
		}

	}
}
