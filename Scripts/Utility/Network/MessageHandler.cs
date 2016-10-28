using System;
using System.Collections.Generic;
using Sandbox.ModAPI;

namespace Rynchodon.Utility.Network
{

	public static class MessageHandler
	{

		public static readonly ushort ModId = (ushort)"Autopilot, Radar, and Military Systems".GetHashCode();

		public enum SubMod : byte { FW_EngagerControl, Message, SyncEntityValue, RequestEntityValue, ServerSettings, GuidedMissile }
		public delegate void Handler(byte[] message, int position);

		private static Dictionary<SubMod, Handler> Handlers = new Dictionary<SubMod, Handler>();
		private static Logger s_logger = new Logger();

		[OnWorldLoad]
		private static void Init()
		{
			MyAPIGateway.Multiplayer.RegisterMessageHandler(ModId, HandleMessage);
		}

		[OnWorldClose]
		private static void Unload()
		{
			MyAPIGateway.Multiplayer.UnregisterMessageHandler(ModId, HandleMessage);
		}

		/// <summary>
		/// Register a Handler for messages, only register once or an exception will be thrown.
		/// </summary>
		public static void AddHandler(SubMod id, Handler messageHandler)
		{
			Handlers.Add(id, messageHandler);
		}

		/// <summary>
		/// Register a Handler for messages, can override a previous handler.
		/// </summary>
		public static void SetHandler(SubMod id, Handler messageHandler)
		{
			Handlers[id] = messageHandler;
		}

		private static void HandleMessage(byte[] received)
		{
			Handler handler;
			int position = 0;
			SubMod dest = (SubMod)ByteConverter.GetByte(received, ref position);
			if (Handlers.TryGetValue(dest, out handler))
			{
				try { handler(received, position); }
				catch (Exception ex)
				{
					s_logger.alwaysLog("Handler threw exception: " + ex, Logger.severity.ERROR);
					s_logger.alwaysLog("Removing handler for " + dest, Logger.severity.ERROR);
					Handlers.Remove(dest);
				}
			}
			else
				s_logger.alwaysLog("No handler for " + dest, Logger.severity.WARNING);
		}

	}
}
