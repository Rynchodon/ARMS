using System;

namespace Rynchodon
{
	public static class DelegateExtensions
	{

		public static void InvokeIfExists(this Action action)
		{
			if (action != null)
				action.Invoke();
		}

		public static void InvokeIfExists<T>(this Action<T> action, T arg)
		{
			if (action != null)
				action.Invoke(arg);
		}

		public static void InvokeIfExists<T1, T2>(this Action<T1, T2> action, T1 arg1, T2 arg2)
		{
			if (action != null)
				action.Invoke(arg1, arg2);
		}

		public static TResult InvokeIfExists<TResult>(this Func<TResult> function)
		{
			if (function != null)
				return function.Invoke();
			return default(TResult);
		}

		public static TResult InvokeIfExists<T, TResult>(this Func<T, TResult> function, T arg)
		{
			if (function != null)
				return function.Invoke(arg);
			return default(TResult);
		}

		public static TResult InvokeIfExists<T1, T2, TResult>(this Func<T1, T2, TResult> function, T1 arg1, T2 arg2)
		{
			if (function != null)
				return function.Invoke(arg1, arg2);
			return default(TResult);
		}

	}
}
