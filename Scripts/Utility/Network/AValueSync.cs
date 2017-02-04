using System;
using System.Collections.Generic;
using System.Reflection;
using Sandbox.ModAPI;

namespace Rynchodon.Utility.Network
{
	/// <summary>
	/// Objects of this type synchronize and save values.
	/// </summary>
	/// <typeparam name="TValue">The type of value to sync</typeparam>
	/// <typeparam name="TScript">The type of script to get/set the value from/to</typeparam>
	public abstract class AValueSync<TValue, TScript> : ASync
	{

		public delegate TValue GetterDelegate(TScript script);
		public delegate void SetterDelegate(TScript script, TValue value);

		protected readonly GetterDelegate _getter;
		protected readonly SetterDelegate _setter;

		protected override sealed Type ValueType { get { return typeof(TValue); } }
		
		/// <param name="valueId">Identifier for the value</param>
		/// <param name="save">Save the value to disk.</param>
		public AValueSync(string valueId, GetterDelegate getter, SetterDelegate setter, bool save = true) 
			: base(typeof(TScript), valueId, save)
		{
			_getter = getter;
			_setter = setter;
		}

		public AValueSync(string valueId, string fieldOrPropertyName, bool save = true)
			: base(typeof(TScript), valueId, save)
		{
			Type type = typeof(TScript);

			FieldInfo field = type.GetField(fieldOrPropertyName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
			if (field != null)
			{
				_getter = (script) => (TValue)field.GetValue(script);
				_setter = (script, value) => field.SetValue(script, value);
				return;
			}

			PropertyInfo property = type.GetProperty(fieldOrPropertyName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
			if (property != null)
			{
				_getter = (script) => (TValue)property.GetValue(script);
				_setter = (script, value) => property.SetValue(script, value);
				return;
			}

			throw new ArgumentException(fieldOrPropertyName + " does not match any instance field or property of " + type.Name);
		}

		public TValue GetValue(IMyTerminalBlock block)
		{
			return GetValue(block.EntityId);
		}

		public TValue GetValue(long entityId)
		{
			TScript script;
			if (Registrar.TryGetValue(entityId, out script))
				return _getter(script);

			if (!Globals.WorldClosed)
				LogMissingFromRegistrar(entityId, false);
			return default(TValue);
		}

		public void SetValue(IMyTerminalBlock block, TValue value)
		{
			TScript script;
			if (Registrar.TryGetValue(block, out script))
			{
				SetValue(block.EntityId, script, value, true);
				return;
			}

			if (!Globals.WorldClosed)
				LogMissingFromRegistrar(block.EntityId, false);
		}

		public void SetValue(long entityId, object value)
		{
			TScript script;
			if (Registrar.TryGetValue(entityId, out script))
			{
				SetValue(entityId, script, (TValue)value, true);
				return;
			}

			if (!Globals.WorldClosed)
				LogMissingFromRegistrar(entityId, false);
		}

		protected override sealed void SetValue(SyncMessage sync)
		{
			traceLog("sync: " + sync);

			if (!ValidateType(sync.value))
			{
				alwaysLog("cannot set value to " + sync.value, Logger.severity.WARNING);
				return;
			}

			List<long> orphans = null;

			for (int index = sync.entityId.Count - 1; index >= 0; --index)
			{
				long entityId = sync.entityId[index];
				TScript script;
				if (Registrar.TryGetValue(entityId, out script))
				{
					traceLog("got script, setting value. entityId: " + entityId);
					SetValue(entityId, script, (TValue)sync.value, false);
					continue;
				}
				else
				{
					if (orphans == null)
						orphans = new List<long>();
					orphans.Add(entityId);
				}
			}

			if (orphans != null)
			{
				if (_orphanValues == null)
				{
					_orphanValues = new List<SyncMessage>();
					Registrar.AddAfterScriptAdded<TScript>(CheckForOrphan);
				}
				traceLog("orphans: " + orphans.Count);
				sync.entityId = orphans;
				_orphanValues.AddSyncMessage(sync);
			}
		}

		private void CheckForOrphan(long entityId, TScript script)
		{
			if (_orphanValues == null)
			{
				debugLog("no orphan values, unregister", Logger.severity.WARNING);
				RemoveCheckForOrphan();
				return;
			}

			for (int index = _orphanValues.Count - 1; index >= 0; --index)
			{
				SyncMessage sync = _orphanValues[index];
				if (sync.id == _id && sync.entityId.Contains(entityId))
				{
					if (ValidateType(sync.value))
					{
						debugLog("adopting orphan value for block: " + entityId + ", value: " + sync.value);
						SetValue(entityId, script, (TValue)sync.value, false);
					}
					else
						alwaysLog("cannot set value to " + sync.value);

					sync.entityId.Remove(entityId);
					if (sync.entityId.Count == 0)
					{
						debugLog("all id values have parents");
						_orphanValues.Remove(sync);
						RemoveCheckForOrphan();
						if (_orphanValues.Count == 0)
						{
							debugLog("no more orphan values");
							_orphanValues = null;
						}
					}
					return;
				}
			}

			traceLog("no orphan value for block: " + entityId);
		}

		private void RemoveCheckForOrphan()
		{
			// it's locked right now, remove later
			MyAPIGateway.Utilities.InvokeOnGameThread(() => Registrar.RemoveAfterScriptAdded<TScript>(CheckForOrphan));
		}

		/// <summary>
		/// Sets the value in the script.
		/// </summary>
		/// <param name="entityId">Id of the entity of the script</param>
		/// <param name="script">The script to set the value of</param>
		/// <param name="value">The new value</param>
		/// <param name="send">Value was set locally and may need to be broadcast</param>
		protected abstract void SetValue(long entityId, TScript script, TValue value, bool send);
		
		private bool ValidateType(object obj)
		{
			return obj != null && ValueType == obj.GetType();
		}

	}
}
