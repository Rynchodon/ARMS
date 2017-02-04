using System;
using System.Collections.Generic;
using Sandbox.ModAPI;

namespace Rynchodon.Utility.Network
{
	/// <summary>
	/// For synchronizing and saving a value that is not directly tied to a terminal control.
	/// </summary>
	/// <typeparam name="TValue">The type of value</typeparam>
	/// <typeparam name="TScript">The script that contains the value</typeparam>
	public sealed class ValueSync<TValue, TScript> : AValueSync<TValue, TScript>
	{
		public ValueSync(string valueId, GetterDelegate getter, SetterDelegate setter, bool save = true) : base(valueId, getter, setter, save) { }

		public override void SetValue(long blockId, string value)
		{
			SetValue(blockId, typeof(IConvertible).IsAssignableFrom(ValueType)
				? Convert.ChangeType(value, ValueType)
				: MyAPIGateway.Utilities.SerializeFromXML<TValue>(value));
		}

		protected override void SetValue(long entityId, TScript script, TValue value, bool send)
		{
			traceLog("entered");

			TValue currentValue = _getter(script);
			if (!EqualityComparer<TValue>.Default.Equals(value, currentValue))
			{
				traceLog("value changed from " + currentValue + " to " + value);
				_setter(script, value);
				if (send)
					SendValue(entityId, value);
			}
			else
				traceLog("equals previous value");
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
