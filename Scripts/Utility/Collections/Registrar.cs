using System;
using System.Collections.Generic;
using VRage.ModAPI;

namespace Rynchodon
{
	public static class Registrar
	{

		private static class Register<T>
		{

			private static Dictionary<long, T> m_dictionary = new Dictionary<long, T>();
			private static FastResourceLock m_lock = new FastResourceLock();
			private static List<Action<long, T>> m_afterScriptAdded;

			public static bool Closed { get { return m_dictionary == null; } }

			static Register()
			{
				UnloadAction.Add(Unload);
			}

			private static void Unload()
			{
				m_dictionary.Clear();
				m_afterScriptAdded = null;
			}

			public static void Add(long entityId, T script)
			{
				using (m_lock.AcquireExclusiveUsing())
					m_dictionary.Add(entityId, script);

				if (m_afterScriptAdded != null)
					using (m_lock.AcquireSharedUsing())
						if (m_afterScriptAdded != null)
							foreach (Action<long, T> act in m_afterScriptAdded)
								act.Invoke(entityId, script);
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

			public static IEnumerable<KeyValuePair<long, T>> IdScripts()
			{
				using (m_lock.AcquireSharedUsing())
					foreach (KeyValuePair<long, T> pair in m_dictionary)
						yield return pair;
			}

			public static bool Contains(long entityId)
			{
				using (m_lock.AcquireSharedUsing())
					return m_dictionary.ContainsKey(entityId);
			}

			/// <summary>
			/// Add an action that runs after a script is added, the action will also be run on all current scripts.
			/// </summary>
			/// <param name="action">The action to run after a script is added.</param>
			public static void AddAfterScriptAdded(Action<long, T> action)
			{
				using (m_lock.AcquireExclusiveUsing())
				{
					if (m_afterScriptAdded == null)
						m_afterScriptAdded = new List<Action<long, T>>();
					m_afterScriptAdded.Add(action);
					foreach (KeyValuePair<long, T> pair in m_dictionary)
						action.Invoke(pair.Key, pair.Value);
				}
			}

			/// <summary>
			/// Try to remove an action that runs after a script is added.
			/// </summary>
			/// <param name="action">The action to remove</param>
			/// <returns>True if the action was removed. False if is not present.</returns>
			public static bool RemoveAfterScriptAdded(Action<long, T> action)
			{
				using (m_lock.AcquireExclusiveUsing())
				{
					if (m_afterScriptAdded == null)
						return false;
					bool result = m_afterScriptAdded.Remove(action);
					if (m_afterScriptAdded.Count == 0)
						m_afterScriptAdded = null;
					return true;
				}
			}

		}

		private static List<Action> UnloadAction = new List<Action>();

		[OnWorldClose]
		private static void Unload()
		{
			foreach (Action act in UnloadAction)
				act.Invoke();
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

		public static bool TryGetValue<TValue, TStored>(long entityId, out TValue value) where TValue : class, TStored
		{
			TStored item;
			bool result = Register<TStored>.TryGetValue(entityId, out item);
			value = item as TValue;
			return result && value != null;
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

		public static IEnumerable<KeyValuePair<long, T>> IdScripts<T>()
		{
			return Register<T>.IdScripts();
		}

		public static IEnumerable<TValue> Scripts<TValue, TStored>() where TValue : class, TStored
		{
			foreach (TStored item in Scripts<TStored>())
			{
				TValue value = item as TValue;
				if (value != null)
					yield return value;
			}
		}

		public static bool Contains<T>(long entityId)
		{
			return Register<T>.Contains(entityId);
		}

		/// <summary>
		/// Add an action that runs after a script is added, the action will also be run on all current scripts.
		/// </summary>
		/// <typeparam name="T">The type of script</typeparam>
		/// <param name="action">The action to run after a script is added.</param>
		public static void AddAfterScriptAdded<T>(Action<long, T> action)
		{
			Register<T>.AddAfterScriptAdded(action);
		}

		/// <summary>
		/// Try to remove an action that runs after a script is added.
		/// </summary>
		/// <typeparam name="T">The type of script</typeparam>
		/// <param name="action">The action to remove</param>
		/// <returns>True if the action was removed. False if is not present.</returns>
		public static bool RemoveAfterScriptAdded<T>(Action<long, T> action)
		{
			return Register<T>.RemoveAfterScriptAdded(action);
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
