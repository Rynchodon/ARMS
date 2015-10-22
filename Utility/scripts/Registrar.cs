using System;
using System.Collections.Generic;
using Sandbox.ModAPI;
using VRage.ModAPI;

namespace Rynchodon
{
	public static class Registrar
	{

		private static class Register<T>
		{

			private static Dictionary<long, T> m_dictionary = new Dictionary<long, T>();
			private static FastResourceLock m_lock = new FastResourceLock();

			public static bool Closed { get; private set; }

			static Register()
			{
				MyAPIGateway.Entities.OnCloseAll += Entities_OnCloseAll;
			}

			static void Entities_OnCloseAll()
			{
				MyAPIGateway.Entities.OnCloseAll -= Entities_OnCloseAll;
				Closed = true;
				m_dictionary = null;
				m_lock = null;
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

		}

		public static void Add<T>(IMyEntity entity, T item)
		{
			Register<T>.Add(entity.EntityId, item);
			entity.OnClosing += OnClosing<T>;
		}

		public static bool TryGetValue<T>(long entityId, out T value)
		{
			try { return Register<T>.TryGetValue(entityId, out value); }
			catch (NullReferenceException nre)
			{
				if (!Register<T>.Closed)
					throw nre;
				value = default(T);
				return false;
			}
		}

		public static bool TryGetValue<T>(IMyEntity entity, out T value)
		{
			return TryGetValue(entity.EntityId, out value);
		}

		public static void ForEach<T>(Action<T> function)
		{
			Register<T>.ForEach(function);
		}

		public static void ForEach<T>(Func<T, bool> function)
		{
			Register<T>.ForEach(function);
		}

		private static void OnClosing<T>(IMyEntity obj)
		{
			obj.OnClosing -= OnClosing<T>;
			try
			{ Register<T>.Remove(obj.EntityId); }
			catch (NullReferenceException nre)
			{
				if (!Register<T>.Closed)
					throw nre;
			}
		}

	}
}
