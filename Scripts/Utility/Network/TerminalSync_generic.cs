#if DEBUG
#define TRACE
#endif

using System;
using System.Collections.Generic;
using Sandbox.ModAPI;
using Sandbox.ModAPI.Interfaces;
using Sandbox.ModAPI.Interfaces.Terminal;

namespace Rynchodon.Utility.Network
{

	/// <summary>
	/// Contains generic members of TerminalSync.
	/// </summary>
	/// <typeparam name="TValue">The type of value</typeparam>
	/// <typeparam name="TScript">The script that contains the value</typeparam>
	public abstract class TerminalSync<TValue, TScript> : TerminalSync
	{

		public delegate TValue GetterDelegate(TScript script);
		public delegate void SetterDelegate(TScript script, TValue value);

		private readonly object _control;
		protected readonly GetterDelegate _getter;
		protected readonly SetterDelegate _setter;

		protected override sealed Type ValueType { get { return typeof(TValue); } }

		protected TerminalSync(Id id, IMyTerminalValueControl<TValue> control, GetterDelegate getter, SetterDelegate setter, bool save = true)
			: base(id, save)
		{
			_logger.traceLog("entered");

			_control = control;
			_getter = getter;
			_setter = setter;

			control.Getter = GetValue;
			control.Setter = SetValue;
		}

		protected TerminalSync(Id id, GetterDelegate getter, SetterDelegate setter, bool save = true)
			: base(id, save)
		{
			_logger.traceLog("entered");

			if (!typeof(Enum).IsAssignableFrom(ValueType))
				throw new Exception("not enum: " + ValueType);

			_control = new List<IMyTerminalControl>();
			_getter = getter;
			_setter = setter;
		}

		public void AddFlagControl(TValue flag, IMyTerminalValueControl<bool> control)
		{
			List<IMyTerminalControl> _control = this._control as List<IMyTerminalControl>;
			if (_control == null)
				throw new InvalidOperationException("Cannot add control, did not use flags constructor");

			Type underlying = Enum.GetUnderlyingType(ValueType);

			int intFlag = (int)Convert.ChangeType(flag, underlying);

			control.Getter = (block) => {
				int allFlags = (int)Convert.ChangeType(GetValue(block), underlying);
				return (allFlags & intFlag) == intFlag;
			};
			control.Setter = (block, value) => {
				int allFlags = (int)Convert.ChangeType(GetValue(block), underlying);
				if (value)
					allFlags |= intFlag;
				else
					allFlags &= ~intFlag;
				SetValue(block, (TValue)Convert.ChangeType(Convert.ChangeType(allFlags, underlying), ValueType));
			};

			_control.Add((IMyTerminalControl)control);
		}

		protected TValue GetValue(IMyTerminalBlock block)
		{
			return GetValue(block.EntityId);
		}

		protected TValue GetValue(long blockId)
		{
			TScript script;
			if (Registrar.TryGetValue(blockId, out script))
				return _getter(script);

			if (!Globals.WorldClosed)
				LogMissingFromRegistrar(blockId, false);
			return default(TValue);
		}

		protected void SetValue(IMyTerminalBlock block, TValue value)
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

		protected void SetValue(long blockId, object value)
		{
			TScript script;
			if (Registrar.TryGetValue(blockId, out script))
			{
				SetValue(blockId, script, (TValue)value, true);
				return;
			}

			if (!Globals.WorldClosed)
				LogMissingFromRegistrar(blockId, false);
		}

		protected override sealed void SetValue(SyncMessage sync)
		{
			_logger.traceLog("sync: " + sync);

			if (!ValidateType(sync.value))
			{
				_logger.alwaysLog("cannot set value to " + sync.value, Logger.severity.WARNING);
				return;
			}

			List<long> orphans = null;

			for (int index = sync.blockId.Count - 1; index >= 0; --index)
			{
				long blockId = sync.blockId[index];
				TScript script;
				if (Registrar.TryGetValue(blockId, out script))
				{
					_logger.traceLog("got script, setting value. blockId: " + blockId);
					SetValue(blockId, script, (TValue)sync.value, false);
					continue;
				}
				else
				{
					if (orphans == null)
						orphans = new List<long>();
					orphans.Add(blockId);
				}
			}

			if (orphans != null)
			{
				if (_orphanValues == null)
				{
					_orphanValues = new List<SyncMessage>();
					Registrar.AddAfterScriptAdded<TScript>(CheckForOrphan);
				}
				_logger.traceLog("orphans: " + orphans.Count);
				sync.blockId = orphans;
				_orphanValues.AddSyncMessage(sync);
			}
		}

		protected abstract void SetValue(long blockId, TScript script, TValue value, bool send);

		protected void UpdateVisual()
		{
			if (_control is IMyTerminalControl)
				((IMyTerminalControl)_control).UpdateVisual();
			else
				foreach (IMyTerminalControl control in ((List<IMyTerminalControl>)_control))
					control.UpdateVisual();
		}

		private void CheckForOrphan(long blockId, TScript script)
		{
			if (_orphanValues == null)
			{
				_logger.debugLog("no orphan values, unregister", Logger.severity.WARNING);
				RemoveCheckForOrphan();
				return;
			}

			for (int index = _orphanValues.Count - 1; index >= 0; --index)
			{
				SyncMessage sync = _orphanValues[index];
				if (sync.id == _id && sync.blockId.Contains(blockId))
				{
					if (ValidateType(sync.value))
					{
						_logger.debugLog("adopting orphan value for block: " + blockId + ", value: " + sync.value);
						SetValue(blockId, script, (TValue)sync.value, false);
					}
					else
						_logger.alwaysLog("cannot set value to " + sync.value);

					sync.blockId.Remove(blockId);
					if (sync.blockId.Count == 0)
					{
						_logger.debugLog("all id values have parents");
						_orphanValues.Remove(sync);
						RemoveCheckForOrphan();
						if (_orphanValues.Count == 0)
						{
							_logger.debugLog("no more orphan values");
							_orphanValues = null;
						}
					}
					return;
				}
			}

			_logger.traceLog("no orphan value for block: " + blockId);
		}

		private void RemoveCheckForOrphan()
		{
			// it's locked right now, remove later
			MyAPIGateway.Utilities.InvokeOnGameThread(() => Registrar.RemoveAfterScriptAdded<TScript>(CheckForOrphan));
		}

		private bool ValidateType(object obj)
		{
			if (typeof(Enum).IsAssignableFrom(ValueType) && Enum.GetUnderlyingType(ValueType) == obj.GetType())
				return true;

			return obj != null && ValueType == obj.GetType();
		}

	}

}
