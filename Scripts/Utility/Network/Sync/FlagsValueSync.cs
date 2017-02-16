#if DEBUG
#define TRACE
#endif

using System;
using System.Collections.Generic;
using System.Globalization;
using Sandbox.ModAPI.Interfaces.Terminal;

namespace Rynchodon.Utility.Network.Sync
{
	/// <summary>
	/// Sync an enum that represents flags.
	/// </summary>
	public sealed class FlagsValueSync<TValue, TScript> : AValueSync<TValue, TScript> where TValue : IConvertible
	{

		private readonly List<IMyTerminalControl> _controls = new List<IMyTerminalControl>();

		public FlagsValueSync(string valueId, GetterDelegate getter, SetterDelegate setter, bool save = true, TValue defaultValue = default(TValue)) : base(valueId, getter, setter, save, defaultValue) { }

		public FlagsValueSync(string valueId, string fieldName, bool save = true) : base(valueId, fieldName, save) { }

		/// <summary>
		/// Add a boolean control to get/set a flag; the getter and setter of the control are set by this function.
		/// </summary>
		/// <param name="control">The control to get/set the flag.</param>
		/// <param name="flag">The flag to get/set.</param>
		public void AddControl(IMyTerminalValueControl<bool> control, TValue flag)
		{
			int flagValue = flag.ToInt32(CultureInfo.InvariantCulture);

			_controls.Add((IMyTerminalControl)control);
			control.Getter = (block) => {
				int currentValue = GetValue(block).ToInt32(CultureInfo.InvariantCulture);
				return (currentValue & flagValue) != 0;
			};

			control.Setter = (block, value) => {
				int currentValue = GetValue(block).ToInt32(CultureInfo.InvariantCulture);
				if (value)
					currentValue |= flagValue;
				else
					currentValue &= ~flagValue;
				SetValue(block, (TValue)Convert.ChangeType(currentValue, Enum.GetUnderlyingType(typeof(TValue))));
			};
		}

		public void AddInverseControl(IMyTerminalValueControl<bool> control, TValue flag)
		{
			int flagValue = flag.ToInt32(CultureInfo.InvariantCulture);

			_controls.Add((IMyTerminalControl)control);
			control.Getter = (block) => {
				int currentValue = GetValue(block).ToInt32(CultureInfo.InvariantCulture);
				return (currentValue & flagValue) == 0;
			};

			control.Setter = (block, value) => {
				int currentValue = GetValue(block).ToInt32(CultureInfo.InvariantCulture);
				if (value)
					currentValue &= ~flagValue;
				else
					currentValue |= flagValue;
				SetValue(block, (TValue)Convert.ChangeType(currentValue, Enum.GetUnderlyingType(typeof(TValue))));
			};
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

		private void UpdateVisual()
		{
			foreach (IMyTerminalControl control in _controls)
				control.UpdateVisual();
		}

	}
}
