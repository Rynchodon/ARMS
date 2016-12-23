using Rynchodon.Utility.Network;
using VRage.Game.ModAPI;

namespace Rynchodon
{
	public static class IMyMultiplayerExtensions
	{
		public static bool TrySendMessageToServer(this IMyMultiplayer Multiplayer, byte[] message, bool reliable = true)
		{
			return Multiplayer.SendMessageToServer(MessageHandler.ModId, message, reliable);
		}

		public static bool TrySendMessageToSelf(this IMyMultiplayer Multiplayer, byte[] message, bool reliable = true)
		{
			return Multiplayer.SendMessageTo(MessageHandler.ModId, message, Multiplayer.MyId, reliable);
		}

		public static bool TrySendMessageTo(this IMyMultiplayer Multiplayer, byte[] message, ulong recipient, bool reliable = true)
		{
			return Multiplayer.SendMessageTo(MessageHandler.ModId, message, recipient, reliable);
		}

		public static void SendMessageToServer(this IMyMultiplayer Multiplayer, byte[] message, bool reliable = true)
		{
			if (!Multiplayer.SendMessageToServer(MessageHandler.ModId, message, reliable))
				throw new MessageTooLongException(message.Length);
		}

		public static void SendMessageToSelf(this IMyMultiplayer Multiplayer, byte[] message, bool reliable = true)
		{
			if (!Multiplayer.SendMessageTo(MessageHandler.ModId, message, Multiplayer.MyId, reliable))
				throw new MessageTooLongException(message.Length);
		}

		public static void SendMessageTo(this IMyMultiplayer Multiplayer, byte[] message, ulong recipient, bool reliable = true)
		{
			if (!Multiplayer.SendMessageTo(MessageHandler.ModId, message, recipient, reliable))
				throw new MessageTooLongException(message.Length);
		}

	}
}
