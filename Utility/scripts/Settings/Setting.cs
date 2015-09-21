using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Rynchodon.Settings
{

	public interface Setting
	{
		/// <summary>Gets the Value of this Setting as a string.</summary>
		/// <returns>string representation of Value.</returns>
		string ValueAsString();
		/// <summary>Sets the Value of this Setting from a string.</summary>
		/// <param name="value">the string to get Value from</param>
		bool ValueFromString(string value);
	}

	public class SettingSimple<T> : Setting where T : struct
	{
		public T Value { get; set; }

		public SettingSimple(T defaultValue)
		{ this.Value = defaultValue; }

		public string ValueAsString()
		{ return Value.ToString(); }

		public virtual bool ValueFromString(string value)
		{
			T temp = (T)Convert.ChangeType(value, typeof(T));
			if (temp.Equals(this.Value))
				return false;

			this.Value = temp;
			return true;
		}
	}

	public class SettingMinMax<T> : SettingSimple<T> where T : struct
	{
		public readonly T Min;
		public readonly T Max;

		public SettingMinMax(T min, T max, T defaultValue)
			: base(defaultValue)
		{
			this.Min = min;
			this.Max = max;
			this.Value = defaultValue;
		}

		public override bool ValueFromString(string value)
		{
			T temp = (T)Convert.ChangeType(value, typeof(T));
			if (Comparer<T>.Default.Compare(this.Value, Min) < 0)
				temp = Min;
			if (Comparer<T>.Default.Compare(this.Value, Max) > 0)
				temp = Max;

			if (temp.Equals(this.Value))
				return false;

			this.Value = temp;
			return true;
		}
	}

	public class SettingString : Setting
	{
		public string Value { get; set; }

		public SettingString(string defaultValue)
		{ this.Value = defaultValue; }

		public string ValueAsString()
		{ return Value; }

		public bool ValueFromString(string value)
		{
			if (value == this.Value)
				return false;
			this.Value = value;
			return true;
		}
	}
}
