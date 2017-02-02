#if DEBUG
#define TRACE
#endif

using System;
using System.Collections.Generic;
using Sandbox.ModAPI;
using Sandbox.ModAPI.Interfaces.Terminal;

namespace Rynchodon.Utility.Network
{
	/// <summary>
	/// For synchronizing and saving terminal control values where the value is synchronized everytime it changes.
	/// </summary>
	/// <typeparam name="TValue">The type of value</typeparam>
	/// <typeparam name="TScript">The script that contains the value</typeparam>
	public sealed class TerminalValueSync<TValue, TScript> : TerminalSync<TValue, TScript> where TValue : struct
	{

		/// <summary>
		/// Synchronize and save a value associated with a terminal control. The value will be synchronized everytime it changes.
		/// </summary>
		/// <param name="id">Unique id for sending accross a network.</param>
		/// <param name="control">GUI control for getting/setting the value.</param>
		/// <param name="getter">Function to get the value from a script.</param>
		/// <param name="setter">Function to set a value in a script.</param>
		/// <param name="save">Iff true, save the value to disk.</param>
		public TerminalValueSync(Id id, IMyTerminalValueControl<TValue> control, GetterDelegate getter, SetterDelegate setter, bool save = true) : base(id, control, getter, setter, save) { }

		public TerminalValueSync(Id id, GetterDelegate getter, SetterDelegate setter, bool save = true) : base(id, getter, setter, save) { }

		public override void SetValue(long blockId, string value)
		{
			SetValue(blockId, typeof(IConvertible).IsAssignableFrom(ValueType)
				? typeof(Enum).IsAssignableFrom(ValueType) ? Enum.Parse(ValueType, value) : Convert.ChangeType(value, ValueType)
				: MyAPIGateway.Utilities.SerializeFromXML<TValue>(value));
		}

		protected override void SetValue(long blockId, TScript script, TValue value, bool send)
		{
			_logger.traceLog("entered");

			TValue currentValue = _getter(script);
			if (!EqualityComparer<TValue>.Default.Equals(value, currentValue))
			{
				_logger.traceLog("value changed from " + currentValue + " to " + value);
				_setter(script, value);
				if (send)
					SendValue(blockId, value);

				UpdateVisual();
			}
			else
				_logger.traceLog("equals previous value");
		}

		protected override IEnumerable<KeyValuePair<long, object>> AllValues()
		{
			foreach (KeyValuePair<long, TScript> pair in Registrar.IdScripts<TScript>())
			{
				TValue value = _getter(pair.Value);
				if (EqualityComparer<TValue>.Default.Equals(value, default(TValue)))
					continue;
				yield return new KeyValuePair<long, object>(pair.Key, value);
			}
		}

	}
}
