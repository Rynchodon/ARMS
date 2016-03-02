using Rynchodon.Utility.Network;
using Sandbox.ModAPI; // from Sandbox.Common.dll

namespace Rynchodon
{
	public static class IMyMultiplayerExtensions
	{

		public static bool SendMessageToServer(this IMyMultiplayer Multiplayer, byte[] message, bool reliable = true)
		{
			return Multiplayer.SendMessageToServer(MessageHandler.ModId, message, reliable);
		}

		public static bool SendMessageToSelf(this IMyMultiplayer Multiplayer, byte[] message, bool reliable = true)
		{
			return Multiplayer.SendMessageTo(MessageHandler.ModId, message, Multiplayer.MyId, reliable);
		}

	}
}
