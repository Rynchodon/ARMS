using System;
using System.Text.RegularExpressions;

namespace Rynchodon
{
	/// <summary>
	/// Convert a number to a pretty string using a SI multiple. If the number is too large or small, uses E notation.
	/// </summary>
	public static class PrettySI
	{
		private static readonly string[]
			SI_1000_multiples = { "k", "M", "G", "T", "P", "E", "Z", "Y" },
			SI_1000_subMulti = { "m", "µ", "n", "p", "f", "a", "z", "y" },
			SI_10_multiples = { "da", "h", "k" },
			SI_10_subMulti = { "d", "c", "m" };

		private const double k = 1000;
		private const double m = 0.001f;

		//private static readonly Logger Log = new Logger(null, "PrettySI");

		/// <summary>
		/// For a double between 0.1 and 999, round to a number of significant figures. Minimum of three significant figures.
		/// </summary>
		/// <param name="toRound">number to round</param>
		/// <param name="sigFig">number of significant figures in result</param>
		/// <returns>string representation of the number</returns>
		public static string toSigFigs(double toRound, byte sigFig = 3)
		{
			if (sigFig < 3)
				sigFig = 3;

			double toRoundAbs = Math.Abs(toRound);

			if (toRoundAbs >= 100)
				return toRound.ToString("F" + (sigFig - 3));
			if (toRoundAbs >= 10)
				return toRound.ToString("F" + (sigFig - 2));
			if (toRoundAbs >= 1)
				return toRound.ToString("F" + (sigFig - 1));
			return toRound.ToString("F" + sigFig);
		}

		/// <summary>
		/// Treats multiples as separated by k and sub multiples as separated by m.
		/// </summary>
		private static string makePretty(double toPretty, string[] multiple, string[] subMutli, byte sigFig, bool space)
		{
			if (toPretty == 0)
				return "0 ";

			if (sigFig < 3)
				sigFig = 3;

			double toPrettyAbs = Math.Abs(toPretty);

			if (toPrettyAbs >= 1)
			{
				if (toPrettyAbs < k)
					return toSigFigs(toPretty, sigFig) + (space ? " " : ""); // no multiple

				double nextMulti = k;
				for (int i = 0; i < multiple.Length; i++)
				{
					double multi = nextMulti;
					nextMulti *= k;

					if (toPrettyAbs < nextMulti)
						return toSigFigs(toPretty / multi) + (space ? " " : "") + multiple[i];
				}

				// more than a thousand of highest multi
				//Log.DebugLog("more than a thousand of highest multi: " + toPretty, "makePretty()");
				return toPretty.ToString("E" + (sigFig - 1)) + (space ? " " : "");
			}

			// toPrettyAbs < 1
			{
				double nextMulti = m;
				for (int i = 0; i < subMutli.Length; i++)
				{
					double multi = nextMulti;
					nextMulti *= m;

					if (toPrettyAbs > multi)
						return toSigFigs(toPretty / multi) + (space ? " " : "") + subMutli[i];
				}

				// less than a thousandth of lowest sub-multi
				//Log.DebugLog("less than a thousandth of lowest sub-multi: " + toPretty, "makePretty()");
				return toPretty.ToString("E" + (sigFig - 1)) + (space ? " " : "");
			}
		}

		/// <summary>
		/// Convert a pretty multiple to a double value.
		/// </summary>
		/// <returns>0 if pretty is not found, or else a double value to multiply the base by.</returns>
		private static double getMultiplier(string[] multiple, string[] subMutli, string pretty)
		{
			for (int index = 0; index < multiple.Length; index++)
				if (pretty.StartsWith(multiple[index]))
					return Math.Pow(1000d, index + 1);

			for (int index = 0; index < subMutli.Length; index++)
				if (pretty.StartsWith(subMutli[index]))
					return Math.Pow(0.001d, index + 1);

			return 0d;
		}

		/// <summary>
		/// Convert a double to a string with a SI prefix.
		/// </summary>
		/// <param name="toPretty">number to make pretty</param>
		/// <param name="sigFig">number of significant figures in result</param>
		/// <returns>string representation of the number</returns>
		public static string makePretty(double toPretty, byte sigFigs = 3, bool space = true)
		{
			string result = makePretty(toPretty, SI_1000_multiples, SI_1000_subMulti, sigFigs, space);
			//Log.DebugLog("converted \"" + toPretty + "\" to \"" + result + '"', "makePretty()");
			return result;
		}

		/// <summary>
		/// Convert a cubic double (such as volume) to a string with a SI prefix.
		/// </summary>
		/// <param name="toPretty">number to make pretty</param>
		/// <param name="sigFig">number of significant figures in result</param>
		/// <returns>string representation of the number</returns>
		public static string makePrettyCubic(double toPretty, byte sigFig = 3, bool space = true)
		{
			string result = makePretty(toPretty, SI_10_multiples, SI_10_subMulti, sigFig, space);
			//Log.DebugLog("converted \"" + toPretty + "\" to \"" + result + '"', "makePretty()");
			return result;
		}
		
		public static string makePretty(TimeSpan span)
		{
			if (span.Days >= 1d)
				return span.Days + " days";
			if (span.Hours >= 1)
				return span.Hours + " h";
			if (span.Minutes >= 1)
				return span.Minutes + " min";
			if (span.Seconds >= 1)
				return span.Seconds + " s";
			return makePretty(span.TotalSeconds) + 's';
		}

		public static bool TryParse(string pretty, out double value)
		{
			pretty = pretty.Trim();

			if (string.IsNullOrWhiteSpace(pretty))
			{
				value = 0d;
				return true;
			}

			if (double.TryParse(pretty, out value) && value.IsValid())
				return true;

			Match match = Regex.Match(pretty, @"(-?\d*\.?\d*)\s*(\w*)");

			Logger.DebugLog("pretty: " + pretty + ", group count: " + match.Groups.Count);

			if (match.Groups.Count == 0)
			{
				value = default(double);
				return false;
			}

			if (!double.TryParse(match.Groups[1].Value, out value))
				return false;

			if (match.Groups.Count == 1 || string.IsNullOrWhiteSpace(match.Groups[2].Value))
				return true;

			double multi = getMultiplier(SI_1000_multiples, SI_1000_subMulti, match.Groups[2].Value);
			if (multi == 0d)
				return false;
			value *= multi;
			return true;
		}

		public static bool TryParse(string pretty, out float value)
		{
			double dv;
			bool result = TryParse(pretty, out dv);
			value = (float)dv;
			return result;
		}

		public static bool TryParse(string pretty, out TimeSpan span)
		{
			Match match = Regex.Match(pretty, @"(-?\d*\.?\d*)\s*(\w*)");

			if (match.Groups.Count == 0)
			{
				span = default(TimeSpan);
				return false;
			}

			double number;
			if (!double.TryParse(match.Groups[1].Value, out number))
			{
				Logger.DebugLog("failed to parse as double: " + match.Groups[1].Value);
				span = default(TimeSpan);
				return false;
			}

			if (match.Groups.Count == 1 || string.IsNullOrWhiteSpace(match.Groups[2].Value))
			{
				span = TimeSpan.FromSeconds(number);
				return true;
			}

			switch (match.Groups[2].Value)
			{
				case "days":
					span = TimeSpan.FromDays(number);
					return true;
				case "h":
					span = TimeSpan.FromHours(number);
					return true;
				case "m":
				case "min":
					span = TimeSpan.FromMinutes(number);
					return true;
				case "s":
					span = TimeSpan.FromSeconds(number);
					return true;
				default:
					double multi = getMultiplier(SI_1000_multiples, SI_1000_subMulti, match.Groups[2].Value);
					if (multi == 0d)
					{
						Logger.DebugLog("failed to get multiple: " + match.Groups[2].Value);
						span = default(TimeSpan);
						return false;
					}
					span = TimeSpan.FromSeconds(number * multi);
					return true;
			}
		}

	}
}
