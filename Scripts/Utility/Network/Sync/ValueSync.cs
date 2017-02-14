using Sandbox.ModAPI.Interfaces.Terminal;

namespace Rynchodon.Utility.Network
{
	/// <summary>
	/// For synchronizing and saving a value.
	/// </summary>
	/// <typeparam name="TValue">The type of value</typeparam>
	/// <typeparam name="TScript">The script that contains the value</typeparam>
	public sealed class ValueSync<TValue, TScript> : AValueSync<TValue, TScript>
	{

		private readonly IMyTerminalControl _control;

		/// <summary>
		/// Synchronize and save a value associated with a terminal control. The value will be synchronized everytime it changes.
		/// </summary>
		/// <param name="control">GUI control for getting/setting the value.</param>
		/// <param name="getter">Function to get the value from a script.</param>
		/// <param name="setter">Function to set a value in a script.</param>
		/// <param name="save">Iff true, save the value to disk.</param>
		/// <param name="defaultValue">The default value to use. The value in the script will be set to defaultValue by ValueSync.</param>
		public ValueSync(IMyTerminalValueControl<TValue> control, GetterDelegate getter, SetterDelegate setter, bool save = true, TValue defaultValue = default(TValue))
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
		public ValueSync(IMyTerminalValueControl<TValue> control, string fieldName, bool save = true)
			: base(control.Id, fieldName, save)
		{
			control.Getter = GetValue;
			control.Setter = SetValue;

			_control = (IMyTerminalControl)control;
		}

		/// <summary>
		/// Synchronize a value that is not directly tied to a terminal control. The value will be synchronized every time it changes.
		/// </summary>
		/// <param name="valueId">Identifier for the value</param>
		/// <param name="getter">Method to get the value from a script</param>
		/// <param name="setter">Method to set the value in a script</param>
		/// <param name="save">Save the value to disk</param>
		/// <param name="defaultValue">The default value to use. The value in the script will be set to defaultValue by ValueSync.</param>
		public ValueSync(string valueId, GetterDelegate getter, SetterDelegate setter, bool save = true, TValue defaultValue = default(TValue)) 
			: base(valueId, getter, setter, save, defaultValue) { }

		/// <summary>
		/// Synchronize a value that is not directly tied to a terminal control. The value will be synchronized every time it changes.
		/// </summary>
		/// <param name="valueId">Identifier for the value</param>
		/// <param name="fieldName">The name of a field in the script to get/set the value from/to. If the field has a default value, the DefaultValueAttribute should be used.</param>
		/// <param name="save">Save the value to disk</param>
		public ValueSync(string valueId, string fieldName, bool save = true)
			: base(valueId, fieldName, save) { }

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

				_control?.UpdateVisual();
			}
			else
				traceLog("equals previous value");
		}

	}
}
