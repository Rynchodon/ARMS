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

		public static void JoinComma(this StringBuilder builder, string last, params string[] args)
		{
			switch (args.Length)
			{
				case 0:
					return;
				case 1:
					builder.Append(args);
					return;
				case 2:
					builder.Append(args[0]);
					builder.Append(' ');
					builder.Append(last);
					builder.Append(' ');
					builder.Append(args[1]);
					return;
			}

			int index;
			for (index = 0; index < args.Length - 1; index++)
			{
				builder.Append(args[index]);
				builder.Append(", ");
			}
			builder.Append(last);
			builder.Append(' ');
			builder.Append(args[index]);
		}

		public static void JoinCommaAnd(this StringBuilder builder, params string[] args)
		{
			JoinComma(builder, "and", args);
		}

		public static void JoinCommaOr(this StringBuilder builder, params string[] args)
		{
			JoinComma(builder, "or", args);
		}

		public static bool IsNullOrEmpty(this StringBuilder builder)
		{
			return builder == null || builder.Length == 0;
		}

		public static bool IsNullOrWhitespace(this StringBuilder builder)
		{
			if (builder == null || builder.Length == 0)
				return true;
			for (int index = builder.Length - 1; index >= 0; --index)
				if (!char.IsWhiteSpace(builder[index]))
					return false;
			return true;
		}

	}
}
