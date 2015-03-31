#define LOG_ENABLED // remove on build

using System;
//using System.Collections.Generic;
//using System.Linq;
//using System.Text;

namespace Rynchodon
{
	/// <summary>
	/// Convert a number to a pretty string using a SI multiple. If the number is too large or small, uses scientific notation.
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

		private static readonly Logger myLogger = new Logger(null, "PrettySI");

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

			if (toRound > 100)
				return toRound.ToString("F" + (sigFig - 3));
			if (toRound > 10)
				return toRound.ToString("F" + (sigFig - 2));
			if (toRound > 1)
				return toRound.ToString("F" + (sigFig - 1));
			return toRound.ToString("F" + sigFig);
		}

		/// <summary>
		/// Treats multiples as separated by k and sub multiples as separated by m.
		/// </summary>
		private static string makePretty(double toPretty, string[] multiple, string[] subMutli, byte sigFig)
		{
			if (sigFig < 3)
				sigFig = 3;

			if (toPretty >= 1)
			{
				if (toPretty < k)
					return toSigFigs(toPretty, sigFig); // no multiple

				double nextMulti = k;
				for (int i = 0; i < multiple.Length; i++)
				{
					double multi = nextMulti;
					nextMulti *= k;

					if (toPretty < nextMulti)
						return toSigFigs(toPretty / multi) + multiple[0];
				}

				// more than a thousand of highest multi
				return toPretty.ToString("E" + (sigFig - 1));
			}

			// toPretty < 1
			{
				double nextMulti = m;
				for (int i = 0; i < subMutli.Length; i++)
				{
					double multi = nextMulti;
					nextMulti *= m;

					if (toPretty > nextMulti)
						return toSigFigs(toPretty / multi) + subMutli[0];
				}

				// less than a thousandth of lowest sub-multi
				return toPretty.ToString("E" + (sigFig - 1));
			}
		}

		/// <summary>
		/// Convert a double to a string with a SI prefix.
		/// </summary>
		/// <param name="toPretty">number to make pretty</param>
		/// <param name="sigFig">number of significant figures in result</param>
		/// <returns>string representation of the number</returns>
		public static string makePretty(double toPretty, byte sigFigs = 3)
		{
			string result = makePretty(toPretty, SI_1000_multiples, SI_1000_subMulti, sigFigs);
			//myLogger.debugLog("converted " + toPretty + " to " + result, "makePretty()");
			return result;
		}

		/// <summary>
		/// Convert a cubic double (such as volume) to a string with a SI prefix.
		/// </summary>
		/// <param name="toPretty">number to make pretty</param>
		/// <param name="sigFig">number of significant figures in result</param>
		/// <returns>string representation of the number</returns>
		public static string makePrettyCubic(double toPretty, byte sigFig = 3)
		{
			string result = makePretty(toPretty, SI_10_multiples, SI_10_subMulti, sigFig);
			//myLogger.debugLog("converted " + toPretty + " to " + result, "makePrettyCubic()");
			return result;
		}

		// TODO: 
		/// <summary>
		/// Not Yet Implemented. Convert a pretty string with a SI prefix to a double.
		/// </summary>
		/// <param name="pretty">string with SI prefix</param>
		/// <returns>parsed double</returns>
		public static double fromPretty(string pretty)
		{
			VRage.Exceptions.ThrowIf<NotImplementedException>(true);
			return 0;
		}

	}
}
