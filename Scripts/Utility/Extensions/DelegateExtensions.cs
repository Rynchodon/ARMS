using System;

namespace Rynchodon.Utility
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
			return function != null ? function.Invoke() : default(TResult);
		}

		public static TResult InvokeIfExists<T, TResult>(this Func<T, TResult> function, T arg)
		{
			return function != null ? function.Invoke(arg) : default(TResult);
		}

		public static TResult InvokeIfExists<T1, T2, TResult>(this Func<T1, T2, TResult> function, T1 arg1, T2 arg2)
		{
			return function != null ? function.Invoke(arg1, arg2) : default(TResult);
		}

	}
}
