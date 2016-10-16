
namespace Rynchodon.Utility
{
	public class MathExtension
	{

		public static float RoundTo(float value, float roundTo)
		{
			float mod = value % roundTo;
			value -= mod;
			if (mod >= roundTo * 0.5f)
				value += roundTo;
			return value;
		}

		public static double RoundTo(double value, double roundTo)
		{
			double mod = value % roundTo;
			value -= mod;
			if (mod >= roundTo * 0.5d)
				value += roundTo;
			return value;
		}

		public static decimal RoundTo(decimal value, decimal roundTo)
		{
			decimal mod = value % roundTo;
			value -= mod;
			if (mod >= roundTo * 0.5m)
				value += roundTo;
			return value;
		}

	}
}
