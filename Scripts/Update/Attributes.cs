using System;
using System.Collections.Generic;
using System.Reflection;
using Sandbox.ModAPI;

namespace Rynchodon
{
	public interface Ordered
	{
		int Order { get; set; }
	}

	/// <summary>
	/// Identifies a static method that is used to initialize a class after a world is loaded.
	/// </summary>
	[AttributeUsage(AttributeTargets.Method)]
	public class OnWorldLoad : Attribute, Ordered
	{
		public int Order { get; set; }
	}

	/// <summary>
	/// Identifies a static method that is run after ARMS is fully initialized.
	/// </summary>
	[AttributeUsage(AttributeTargets.Method)]
	public class AfterArmsInit : Attribute, Ordered
	{
		public int Order { get; set; }
	}

	/// <summary>
	/// Identifies a static method that is used to clear/reset a class when a world is closed.
	/// </summary>
	[AttributeUsage(AttributeTargets.Method)]
	public class OnWorldClose : Attribute, Ordered
	{
		public int Order { get; set; }
	}

	/// <summary>
	/// Identifies a static method that is invoked when the world is saved.
	/// </summary>
	[AttributeUsage(AttributeTargets.Method)]
	public class OnWorldSave : Attribute, Ordered
	{
		public int Order { get; set; }
	}

	public static class AttributeFinder
	{
		private class Sorter : IComparable<Sorter>
		{
			public readonly int PrimaryKey;
			public readonly int SecondaryKey;

			public Sorter(int PrimaryKey, int SecondaryKey)
			{
				this.PrimaryKey = PrimaryKey;
				this.SecondaryKey = SecondaryKey;
			}

			public int CompareTo(Sorter other)
			{
				int value = PrimaryKey.CompareTo(other.PrimaryKey);
				if (value != 0)
					return value;
				return SecondaryKey.CompareTo(other.SecondaryKey);
			}
		}

		/// <summary>
		/// Invokes static, no argument methods with the specified Attribute
		/// </summary>
		public static void InvokeMethodsWithAttribute<T>() where T : Attribute, Ordered
		{
			if (MyAPIGateway.Entities == null)
			{
				Logger.DebugLog("MyAPIGateway.Entities == null", Logger.severity.INFO);
				return;
			}

			SortedDictionary<Sorter, MethodInfo> sorted = new SortedDictionary<Sorter, MethodInfo>();
			foreach (Type type in Assembly.GetExecutingAssembly().GetTypes())
				foreach (MethodInfo method in type.GetMethods(BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Static))
					if (method.IsDefined(typeof(T)))
						if (method.GetParameters().Length != 0)
							Logger.AlwaysLog("method has parameters: " + type + "." + method, Logger.severity.ERROR, primaryState: typeof(T).ToString());
						else
							sorted.Add(new Sorter(method.GetCustomAttribute<T>().Order, method.GetHashCode()), method);

			foreach (MethodInfo method in sorted.Values)
			{
				//Logger.DebugLog("invoking " + method.DeclaringType + "." + method, Logger.severity.TRACE, primaryState: typeof(T).ToString());
				method.Invoke(null, null);
			}
		}
	}
}
