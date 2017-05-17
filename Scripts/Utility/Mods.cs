using System;
using System.Linq;
using System.Reflection;
using Sandbox.Game.World;
using VRage.Collections;
using VRage.Game.Components;

namespace Rynchodon
{
	public static class Mods
	{
		public static MySessionComponentBase FindModSessionComponent(string modName, string modScriptsFolder, string typeName)
		{
			string assemblyName = $"{modName}_{modScriptsFolder}, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null";

			CachingDictionary<Type, MySessionComponentBase> sessionComponents = (CachingDictionary<Type, MySessionComponentBase>)
				typeof(MySession).GetField("m_sessionComponents", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(MySession.Static);
			if (sessionComponents == null)
			{
				Logger.AlwaysLog("Failed to get m_sessionComponents", Logger.severity.ERROR);
				return null;
			}

			// Type.GetType(string typeName) with the fully qualified name doesn't seem to work, maybe it's something to do with CodeDOM
			// there can be more than one assembly with the right name
			foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies().Where(x => x.FullName == assemblyName))
			{
					Type componentType = assembly.GetType(typeName);
					if (componentType == null)
					{
						Logger.DebugLog($"Failed to get type from assembly. Assembly: {assemblyName}, Type: {typeName}", Logger.severity.TRACE);
						continue;
					}

					MySessionComponentBase component;
					if (!sessionComponents.TryGetValue(componentType, out component))
					{
						Logger.DebugLog($"Failed to get MySessionComponentBase. Assembly: {assemblyName}, Type: {typeName}", Logger.severity.TRACE);
						continue;
					}

					return component;
			}

			return null;
		}
	}
}
