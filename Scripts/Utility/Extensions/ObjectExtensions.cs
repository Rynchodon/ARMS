using Sandbox.ModAPI;

namespace Rynchodon
{
	public static class ObjectExtensions
	{

		/// <summary>
		/// Clone an object using XML serialization. If the object is not serializable, this method will throw an exception.
		/// </summary>
		/// <typeparam name="T">The type of object to clone.</typeparam>
		/// <param name="obj">The object to clone.</param>
		/// <returns>A clone of the original object.</returns>
		public static T SerialClone<T>(this T obj)
		{
			if (obj.Equals(default(T)))
				return default(T);
			string serial = MyAPIGateway.Utilities.SerializeToXML(obj);
			return MyAPIGateway.Utilities.SerializeFromXML<T>(serial);
		}

	}
}
