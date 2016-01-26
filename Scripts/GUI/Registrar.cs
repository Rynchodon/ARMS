using System;
using System.Collections.Generic;
using VRage;
using VRage.ModAPI;

namespace Rynchodon.GUI
{

	/// <summary>
	/// Modified Registrar that does not shut down cleanly.
	/// </summary>
	public static class Registrar
	{

		private static class Register_Dirty<T>
		{

			private static readonly Logger s_logger = new Logger("Rynchodon.GUI.Register_Dirty<T>", () => typeof(T).ToString());

			private static Dictionary<long, T> m_dictionary = new Dictionary<long, T>();
			private static FastResourceLock m_lock = new FastResourceLock();

			public static void Add(long entityId, T script)
			{
				using (m_lock.AcquireExclusiveUsing())
					m_dictionary.Add(entityId, script);
				s_logger.debugLog("Added " + script + ", for " + entityId, "Add()");
			}

			public static bool Remove(long entityId)
			{
				s_logger.debugLog("Removing script, for " + entityId, "Remove()");
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
			Register_Dirty<T>.Add(entity.EntityId, item);
			entity.OnClosing += OnClosing<T>;
		}

		public static bool TryGetValue<T>(long entityId, out T value)
		{
			return Register_Dirty<T>.TryGetValue(entityId, out value); 
		}

		public static bool TryGetValue<T>(IMyEntity entity, out T value)
		{
			return TryGetValue(entity.EntityId, out value);
		}

		public static void ForEach<T>(Action<T> function)
		{
			Register_Dirty<T>.ForEach(function);
		}

		public static void ForEach<T>(Func<T, bool> function)
		{
			Register_Dirty<T>.ForEach(function);
		}

		private static void OnClosing<T>(IMyEntity obj)
		{
			obj.OnClosing -= OnClosing<T>;
			Register_Dirty<T>.Remove(obj.EntityId); 
		}

	}
}
