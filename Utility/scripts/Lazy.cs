using System;

namespace Rynchodon
{
	/// <summary>
	/// Simple version of System.Lazy (which is blacklisted). Not thread-safe.
	/// </summary>
	/// <typeparam name="T"></typeparam>
	public class Lazy<T>
	{
		private T value__value;
		private bool value__IsValueCreated = false;
		private Func<T> valueFactory;

		public Lazy(Func<T> valueFactory)
		{ this.valueFactory = valueFactory; }

		public T Value
		{
			get
			{
				if (!value__IsValueCreated)
				{
					value__value = valueFactory();
					value__IsValueCreated = true;
				}
				return value__value;
			}
		}

		public bool IsValueCreated
		{ get { return value__IsValueCreated; } }
	}
}
