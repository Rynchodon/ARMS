using System;
using System.Collections.Generic;
using Sandbox.ModAPI;

namespace Rynchodon.Utility.Network
{

	public static class MessageHandler
	{

		public static readonly ushort ModId = (ushort)"Autopilot, Radar, and Military Systems".GetHashCode();

		public enum SubMod : byte { DisplayEntities }

		public static Dictionary<SubMod, Action<byte[]>> Handlers = new Dictionary<SubMod, Action<byte[]>>();

		private static Logger s_logger = new Logger("NetworkClient");

		static MessageHandler()
		{
			MyAPIGateway.Multiplayer.RegisterMessageHandler(ModId, HandleMessage);
			MyAPIGateway.Entities.OnCloseAll += Entities_OnCloseAll;
		}

		private static void Entities_OnCloseAll()
		{
			MyAPIGateway.Entities.OnCloseAll -= Entities_OnCloseAll;
			MyAPIGateway.Multiplayer.UnregisterMessageHandler(ModId, HandleMessage);
			Handlers = null;
			s_logger = null;
		}

		private static void HandleMessage(byte[] received)
		{
			Action<byte[]> handler;
			SubMod dest = (SubMod)received[0];
			if (Handlers.TryGetValue(dest, out handler))
			{
				try { handler(received); }
				catch (Exception ex)
				{
					s_logger.alwaysLog("Handler threw exception: " + ex, "HandleMessage()", Logger.severity.ERROR);
					s_logger.alwaysLog("Removing handler for " + dest, "HandleMessage()", Logger.severity.ERROR);
					Handlers.Remove(dest);
				}
			}
			else
				s_logger.alwaysLog("No handler for " + dest, "HandleMessage()", Logger.severity.WARNING);
		}

	}
}
