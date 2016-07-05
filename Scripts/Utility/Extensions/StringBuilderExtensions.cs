using System.Text;

namespace Rynchodon
{
	public static class StringBuilderExtensions
	{

		public static StringBuilder Clone(this StringBuilder builder)
		{
			if (builder == null)
				return new StringBuilder();
			return new StringBuilder(builder.ToString());
		}

	}
}
