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
					string assemblyName = mod.Name + "_SteamShipped, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null";

					// Type.GetType(string typeName) with the fully qualified name doesn't seem to work, maybe it's something to do with CodeDOM
					foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
						if (assembly.FullName == assemblyName)
						{
							string typeName = "SteamShipped.Notify";
							Type notify = assembly.GetType(typeName);
							if (notify == null)
							{
								Logger.AlwaysLog("Failed to get type from assembly. Assembly: " + assembly.FullName + ", Type: " + typeName, Logger.severity.ERROR);
								return;
							}
							else
							{
								CachingDictionary<Type, MySessionComponentBase> m_sessionComponents = (CachingDictionary<Type, MySessionComponentBase>)
									typeof(MySession).GetField("m_sessionComponents", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(MySession.Static);
								if (m_sessionComponents == null)
								{
									Logger.AlwaysLog("Failed to get m_sessionComponents", Logger.severity.ERROR);
									return;
								}
								else
								{
									MySessionComponentBase notifyObj;
									if (!m_sessionComponents.TryGetValue(notify, out notifyObj))
									{
										Logger.DebugLog("Failed to get MySessionComponentBase", Logger.severity.TRACE);
										// there can be more than one assembly with the right name
										continue;
									}
									else
										notifyObj.GetType().GetField("HasNotified").SetValue(notifyObj, true);
								}
							}

							Logger.DebugLog("Unregistered " + typeName);
							return;
						}

					Logger.AlwaysLog("Failed to find assembly: " + assemblyName, Logger.severity.ERROR);
					return;
				}

			Logger.AlwaysLog("Failed to find mod", Logger.severity.ERROR);
			return;
		}

	}
}
