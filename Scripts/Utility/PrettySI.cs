using System;

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

		//private static readonly Logger myLogger = new Logger(null, "PrettySI");

		/// <summary>
		/// For a double between 0 and 1000, round to a number of significant figures. Minimum of three significant figures.
		/// </summary>
		/// <param name="toRound">number to round</param>
		/// <param name="sigFig">number of significant figures in result</param>
		/// <returns>string representation of the number</returns>
		public static string toSigFigs(double toRound, byte sigFig = 3)
		{
			if (sigFig < 3)
				sigFig = 3;

			double toRoundAbs = Math.Abs(toRound);

			if (toRoundAbs > 100)
				return toRound.ToString("F" + (sigFig - 3));
			if (toRoundAbs > 10)
				return toRound.ToString("F" + (sigFig - 2));
			if (toRoundAbs > 1)
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
				//myLogger.debugLog("more than a thousand of highest multi: " + toPretty, "makePretty()");
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
				//myLogger.debugLog("less than a thousandth of lowest sub-multi: " + toPretty, "makePretty()");
				return toPretty.ToString("E" + (sigFig - 1)) + (space ? " " : "");
			}
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
			//myLogger.debugLog("converted \"" + toPretty + "\" to \"" + result + '"', "makePretty()");
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
			//myLogger.debugLog("converted \"" + toPretty + "\" to \"" + result + '"', "makePretty()");
			return result;
		}

		//// TODO: 
		///// <summary>
		///// Not Yet Implemented. Convert a pretty string with a SI prefix to a double.
		///// </summary>
		///// <param name="pretty">string with SI prefix</param>
		///// <returns>parsed double</returns>
		//public static double fromPretty(string pretty)
		//{
		//	VRage.Exceptions.ThrowIf<NotImplementedException>(true);
		//	return 0;
		//}

	}
}
