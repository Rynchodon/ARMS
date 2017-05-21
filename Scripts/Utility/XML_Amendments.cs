using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using Rynchodon.Utility;
using Sandbox.ModAPI;

namespace Rynchodon
{
	/// <summary>
	/// Serialize an object, add data, deserialize. External exception handling is necessary.
	/// </summary>
	/// <typeparam name="T">The type of object to ammend.</typeparam>
	public class XML_Amendments<T>
	{
		private string _serial;

		/// <summary>Will contain the keys that could not be matched and their values.</summary>
		public Dictionary<string, string> Failed = new Dictionary<string, string>();

		/// <summary>Separator between data entries.</summary>
		public char[] primarySeparator = { '\n', '\r', ',', ';', ':' };
		/// <summary>Data entries shall be (key)(keyValueSeparator)(value)</summary>
		public char[] keyValueSeparator = { '=' };

		private Logable Log { get { return new Logable(typeof(T).FullName); } }

		public XML_Amendments(T obj)
		{
			this._serial = MyAPIGateway.Utilities.SerializeToXML<T>(obj);
		}

		public T Deserialize()
		{
			return MyAPIGateway.Utilities.SerializeFromXML<T>(_serial);
		}

		/// <summary>
		/// Amends an entry in the serialized object.
		/// </summary>
		/// <param name="key">The key for which the value will be amended.</param>
		/// <param name="value">The value which will be amended.</param>
		public void AmendEntry(string key, string value)
		{
			string pattern = "(?<open><" + key + ">)(?<num>.*)(?<close></" + key + ">)";
			string replacement =  "${open}" + value + "${close}";
			int matchCount = 0;
			_serial = Regex.Replace(_serial, pattern, (match) => {
				matchCount++;
				return match.Result(replacement);
			}, RegexOptions.IgnoreCase);

			if (matchCount == 0)
			{
				pattern = "<" + key + " />";
				replacement = "<" + key + ">" + value + "</" + key + ">";
				matchCount = 0;
				_serial = Regex.Replace(_serial, pattern, (match) => {
					matchCount++;
					return match.Result(replacement);
				}, RegexOptions.IgnoreCase);

				if (matchCount == 0)
				{
					Log.AlwaysLog("failed to match key: " + key + '/' + value, Logger.severity.WARNING);
					Failed.Add(key, value);
				}
			}
		}

		/// <summary>
		/// Amends an entry in the serialized object. Uses keyValueSeparator to split that data into key/value.
		/// </summary>
		/// <param name="entry">Entry to add; (key)(keyValueSeparator)(value)</param>
		/// <param name="removeWhitespace">Iff true, whitespace will be removed from the entry.</param>
		public void AmendEntry(string entry, bool removeWhitespace = false)
		{
			string ent = removeWhitespace ? entry.RemoveWhitespace() : entry;
			string[] keyValue = ent.Split(keyValueSeparator, 2, StringSplitOptions.RemoveEmptyEntries);
			if (keyValue.Length != 2)
				throw new InvalidOperationException("could not split into key/value: " + ent);
			AmendEntry(keyValue[0], keyValue[1]);
		}

		/// <summary>
		/// Amends multiple entries in the serialized object. Uses primarySeparator and keyValueSeparator to split that data into key/value pairs.
		/// </summary>
		/// <param name="data">The data as as single string.</param>
		/// <param name="removeWhitespace">Iff true, whitespace will be removed from entries.</param>
		public void AmendAll(string data, bool removeWhitespace = false)
		{
			string[] entries = data.Split(primarySeparator, StringSplitOptions.RemoveEmptyEntries);
			foreach (string e in entries)
				if (!string.IsNullOrWhiteSpace(e))
					AmendEntry(e, removeWhitespace);
		}

	}
}
