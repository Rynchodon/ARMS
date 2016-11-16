using System;
using System.Collections;
using System.Collections.Generic;
using Sandbox.ModAPI;
using VRage;
using VRage.ModAPI;

namespace Rynchodon
{
	public static class Registrar
	{

		private static class Register<T>
		{

			private static Dictionary<long, T> m_dictionary = new Dictionary<long, T>();
			private static FastResourceLock m_lock = new FastResourceLock();

			public static bool Closed { get { return m_dictionary == null; } }

			static Register()
			{
				Registers.Add(m_dictionary);
			}

			public static void Add(long entityId, T script)
			{
				using (m_lock.AcquireExclusiveUsing())
					m_dictionary.Add(entityId, script);
			}

			public static bool Remove(long entityId)
			{
				using (m_lock.AcquireExclusiveUsing())
					return m_dictionary.Remove(entityId);
			}

			public static bool TryGetValue(long entityId, out T value)
			{
				using (m_lock.AcquireSharedUsing())
					return m_dictionary.TryGetValue(entityId, out value);
			}

			public static void ForEach(Action<T> function)
			{
				using (m_lock.AcquireSharedUsing())
					foreach (T script in m_dictionary.Values)
						function(script);
			}

			public static void ForEach(Func<T, bool> function)
			{
				using (m_lock.AcquireSharedUsing())
					foreach (T script in m_dictionary.Values)
						if (function(script))
							return;
			}

			public static IEnumerable<T> Scripts()
			{
				using (m_lock.AcquireSharedUsing())
					foreach (T script in m_dictionary.Values)
						yield return script;
			}

			public static bool Contains(long entityId)
			{
				using (m_lock.AcquireSharedUsing())
					return m_dictionary.ContainsKey(entityId);
			}

		}

		private static List<IDictionary> Registers = new List<IDictionary>();

		[OnWorldClose]
		private static void Unload()
		{
			foreach (IDictionary dict in Registers)
				dict.Clear();
		}

		public static void Add<T>(IMyEntity entity, T item)
		{
			if (Globals.WorldClosed || entity.Closed)
				return;
			Register<T>.Add(entity.EntityId, item);
			entity.OnClose += OnClose<T>;
		}

		public static void Remove<T>(IMyEntity entity)
		{
			if (Globals.WorldClosed)
				return;
			entity.OnClose -= OnClose<T>;
			Register<T>.Remove(entity.EntityId);
		}

		public static void Remove<T>(long entityId)
		{
			if (Globals.WorldClosed)
				return;
			Register<T>.Remove(entityId);
		}

		public static bool TryGetValue<T>(long entityId, out T value)
		{
			return Register<T>.TryGetValue(entityId, out value);
		}

		public static bool TryGetValue<T>(IMyEntity entity, out T value)
		{
			return TryGetValue(entity.EntityId, out value);
		}

		public static void ForEach<T>(Action<T> function)
		{
			if (Globals.WorldClosed)
				return;
			Register<T>.ForEach(function);
		}

		public static void ForEach<T>(Func<T, bool> function)
		{
			if (Globals.WorldClosed)
				return;
			Register<T>.ForEach(function);
		}

		public static IEnumerable<T> Scripts<T>()
		{
			return Register<T>.Scripts();
		}

		public static bool Contains<T>(long entityId)
		{
			return Register<T>.Contains(entityId);
		}

		private static void OnClose<T>(IMyEntity obj)
		{
			if (Globals.WorldClosed)
				return;
			obj.OnClose -= OnClose<T>;
			Register<T>.Remove(obj.EntityId);
		}

	}
}
