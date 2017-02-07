#if DEBUG
#define TRACE
#endif

using System;
using Sandbox.ModAPI.Interfaces.Terminal;

namespace Rynchodon.Utility.Network
{
	/// <summary>
	/// For synchronizing and saving terminal control values where the value is synchronized everytime it changes.
	/// </summary>
	/// <typeparam name="TValue">The type of value</typeparam>
	/// <typeparam name="TScript">The script that contains the value</typeparam>
	public sealed class TerminalValueSync<TValue, TScript> : AValueSync<TValue, TScript>
	{

		private readonly IMyTerminalControl _control;

		/// <summary>
		/// Synchronize and save a value associated with a terminal control. The value will be synchronized everytime it changes.
		/// </summary>
		/// <param name="control">GUI control for getting/setting the value.</param>
		/// <param name="getter">Function to get the value from a script.</param>
		/// <param name="setter">Function to set a value in a script.</param>
		/// <param name="save">Iff true, save the value to disk.</param>
		/// <param name="defaultValue">Do not get value from server when it equals defaultValue. The value in the script will NOT be set to defaultValue by ValueSync.</param>
		public TerminalValueSync(IMyTerminalValueControl<TValue> control, GetterDelegate getter, SetterDelegate setter, bool save = true, TValue defaultValue = default(TValue))
			: base(control.Id, getter, setter, save, defaultValue)
		{
			control.Getter = GetValue;
			control.Setter = SetValue;

			_control = (IMyTerminalControl)control;
		}

		/// <summary>
		/// Synchronize and save a value associated with a terminal control. The value will be synchronized everytime it changes.
		/// </summary>
		/// <param name="control">GUI control for getting/setting the value.</param>
		/// <param name="fieldName">The name of a field in the script to get/set the value from/to. If the field has a default value, the DefaultValueAttribute should be used.</param>
		/// <param name="save">Iff true, save the value to disk.</param>
		public TerminalValueSync(IMyTerminalValueControl<TValue> control, string fieldName, bool save = true)
			: base(control.Id, fieldName, save)
		{
			control.Getter = GetValue;
			control.Setter = SetValue;

			_control = (IMyTerminalControl)control;
		}

		/// <summary>
		/// Set value from saved string.
		/// </summary>
		/// <param name="entityId">Id of the script's entity</param>
		/// <param name="value">The value as a string</param>
		public override void SetValueFromSave(long blockId, string value)
		{
			SetValue(blockId, (TValue)Convert.ChangeType(value, typeof(TValue)), false);
		}

		protected override void SetValue(long blockId, TScript script, TValue value, bool send)
		{
			traceLog("entered");

			TValue currentValue = _getter(script);
			if (!EqualityComparer.Equals(value, currentValue))
			{
				traceLog("value changed from " + currentValue + " to " + value);
				_setter(script, value);
				if (send)
					SendValue(blockId, value);

				UpdateVisual();
			}
			else
				traceLog("equals previous value");
		}

		private void UpdateVisual()
		{
			_control.UpdateVisual();
		}

	}
}
