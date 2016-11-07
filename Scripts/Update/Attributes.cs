using System;
using System.Reflection;
using Sandbox.ModAPI;

namespace Rynchodon
{
	/// <summary>
	/// Identifies a static method that is used to initialize a class after a world is loaded.
	/// </summary>
	public class OnWorldLoad : System.Attribute { }
	/// <summary>
	/// Identifies a static method that is used to clear/reset a class when a world is closed.
	/// </summary>
	public class OnWorldClose : System.Attribute { }

	public static class AttributeFinder
	{
		/// <summary>
		/// Invokes static, no argument methods with the specified Attribute
		/// </summary>
		public static void InvokeMethodsWithAttribute<T>() where T : Attribute
		{
			if (MyAPIGateway.Entities == null)
			{
				Logger.DebugLog("MyAPIGateway.Entities == null", Logger.severity.INFO);
				return;
			}

			foreach (Type type in Assembly.GetCallingAssembly().GetTypes())
				foreach (MethodInfo method in type.GetMethods(BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Static))
					if (method.IsDefined(typeof(T)))
						if (method.GetParameters().Length != 0)
							Logger.AlwaysLog("method has parameters: " + type + "." + method, Logger.severity.ERROR, primaryState: typeof(T).ToString());
						else
						{
							Logger.DebugLog("invoking " + type + "." + method, Logger.severity.TRACE, primaryState: typeof(T).ToString());
							method.Invoke(null, null);
						}
		}
	}
}
