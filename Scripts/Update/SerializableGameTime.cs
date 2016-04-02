using System; // partial
using System.Xml.Serialization; // partial

namespace Rynchodon.Update
{
	[Serializable]
	public class SerializableGameTime
	{

		public static TimeSpan? Adjust;

		[XmlAttribute]
		public long Value;

		private readonly bool fromFile;

		/// <summary>
		/// For loading TimeSpan from file.
		/// </summary>
		public SerializableGameTime()
		{
			fromFile = true;
		}

		/// <summary>
		/// For saving TimeSpan to file.
		/// </summary>
		public SerializableGameTime(TimeSpan span)
		{
			Value = span.Ticks;
		}

		/// <summary>
		/// For TimeSpan from file.
		/// </summary>
		public TimeSpan ToTimeSpan()
		{
			if (!fromFile)
				throw new InvalidOperationException("Cannot call ToTimeSpan() on a SerializableGameTime created from a TimeSpan. Also, why would you?");
			return new TimeSpan(Value) - Adjust.Value;
		}

	}
}
