using System;
using System.Collections.Generic;
using System.Text;
using Rynchodon.Update;
using Sandbox.ModAPI;
using Sandbox.ModAPI.Interfaces.Terminal;
using VRage.ModAPI;

namespace Rynchodon.Utility.Network
{
	/// <summary>
	/// Sync a value that is typed into a text box but must be parsed.
	/// </summary>
	public sealed class TypedValueSync<TValue, TScript> : AValueSync<TValue, TScript>
	{

		private readonly IMyTerminalControl _control;

		private Dictionary<long, StringBuilder> _recentlySet;
		private ulong _waitUntil;

		public TypedValueSync(IMyTerminalControlTextbox control, GetterDelegate getter, SetterDelegate setter, bool save = true, TValue defaultValue = default(TValue))
			: base(((IMyTerminalControl)control).Id, getter, setter, save, defaultValue)
		{
			// MyTerminalControlTextbox has different Getter/Setter
			control.Getter = GetStringBuilder;
			control.Setter = SetStringBuilder;

			_control = control;
		}

		public TypedValueSync(IMyTerminalControlTextbox control, string fieldName, bool save = true)
			: base(((IMyTerminalControl)control).Id, fieldName, save)
		{
			// MyTerminalControlTextbox has different Getter/Setter
			control.Getter = GetStringBuilder;
			control.Setter = SetStringBuilder;

			_control = control;
		}

		private StringBuilder GetStringBuilder(IMyTerminalBlock block)
		{
			return GetStringBuilder(block.EntityId);
		}

		private StringBuilder GetStringBuilder(long entityId)
		{
			if (_recentlySet != null)
			{
				StringBuilder value;
				if (_recentlySet.TryGetValue(entityId, out value))
					return value;
			}

			TScript script;
			if (Registrar.TryGetValue(entityId, out script))
				return new StringBuilder(_getter(script).ToString());

			if (!Globals.WorldClosed)
				LogMissingFromRegistrar(entityId, false);
			return new StringBuilder();
		}

		private void SetStringBuilder(IMyTerminalBlock block, StringBuilder sb)
		{
			SetStringBuilder(block.EntityId, sb);
		}

		private void SetStringBuilder(long entityId, StringBuilder sb)
		{
			if (_recentlySet == null)
			{
				_recentlySet = new Dictionary<long, StringBuilder>();
				UpdateManager.Register(10, Update10);
			}

			_recentlySet[entityId] = sb;
			_waitUntil = Globals.UpdateCount + 120uL;
		}

		protected override void SetValue(long entityId, TScript script, TValue value, bool send)
		{
			traceLog("entered");

			TValue currentValue = _getter(script);
			if (!EqualityComparer.Equals(value, currentValue))
			{
				traceLog("value changed from " + currentValue + " to " + value);
				_setter(script, value);
				if (send)
					SendValue(entityId, value);

				UpdateVisual();
			}
			else
				traceLog("equals previous value");
		}

		private void Update10()
		{
			if (_waitUntil > Globals.UpdateCount)
				return;

			debugLog("Finished wait, parse and send", Logger.severity.DEBUG);

			foreach (KeyValuePair<long, StringBuilder> item in _recentlySet)
			{
				TValue value;
				try { value = FromString(item.Value.ToString()); }
				catch (Exception ex)
				{
					debugLog(ex.ToString(), Logger.severity.WARNING);
					FailedToSet(item.Key, item.Value.ToString());
					SetValue(item.Key, GetDefaultValue(), true);
					continue;
				}
				SetValue(item.Key, value, true);
			}

			UpdateManager.Unregister(10, Update10);
			_recentlySet = null;
		}

		private void UpdateVisual()
		{
			_control?.UpdateVisual();
		}

		private void FailedToSet(long entityId, string value)
		{
			string message = "Cannot convert \"" + value + "\" to " + typeof(TValue);
			Logger.Notify(message, level: Logger.severity.WARNING);
			alwaysLog(message + ", entity ID: " + entityId, Logger.severity.INFO);

			IMyEntity entity;
			if (!MyAPIGateway.Entities.TryGetEntityById(entityId, out entity))
			{
				alwaysLog("Failed to get entity for " + entityId, Logger.severity.WARNING);
				return;
			}

			IMyTerminalBlock termBlock = entity as IMyTerminalBlock;
			if (termBlock != null)
			{
				termBlock.AppendCustomInfo(message);
				return;
			}

			alwaysLog("Expected IMyTerminalBlock, got: " + entity.nameWithId(), Logger.severity.ERROR);
		}

	}
}
