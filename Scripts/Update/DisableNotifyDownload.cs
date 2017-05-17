using System;
using System.Reflection;
using Sandbox.Game.World;
using Sandbox.ModAPI;
using VRage.Collections;
using VRage.Game;
using VRage.Game.Components;

namespace Rynchodon.Update
{
	static class DisableNotifyDownload
	{

		[OnWorldLoad]
		private static void Load()
		{
			foreach (MyObjectBuilder_Checkpoint.ModItem mod in MyAPIGateway.Session.Mods)
				if (mod.PublishedFileId == 363880940uL || mod.Name == "ARMS")
				{
					Logger.DebugLog("ARMS mod: FriendlyName: " + mod.FriendlyName + ", Name: " + mod.Name + ", Published ID: " + mod.PublishedFileId);
					MySessionComponentBase component = Mods.FindModSessionComponent(mod.Name, "SteamShipped", "SteamShipped.Notify");
					if (component == null)
					{
						Logger.AlwaysLog($"Failed to find Session Component.", Logger.severity.ERROR);
						return;
					}
					component.GetType().GetField("HasNotified").SetValue(component, true);
					return;
				}

			Logger.AlwaysLog("Failed to find mod", Logger.severity.ERROR);
			return;
		}

	}
}
