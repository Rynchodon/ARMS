using System;

namespace Rynchodon.Utility
{
	public static class DelegateExtensions
	{

		public static void InvokeIfExists(this Action action)
		{
			Action act = action;
			if (act != null)
				act.Invoke();
		}

		public static void InvokeIfExists<T>(this Action<T> action, T arg)
		{
			Action<T> act = action;
			if (act != null)
				act.Invoke(arg);
		}

		public static void InvokeIfExists<T1, T2>(this Action<T1, T2> action, T1 arg1, T2 arg2)
		{
			Action<T1, T2> act = action;
			if (act != null)
				act.Invoke(arg1, arg2);
		}

		public static TResult InvokeIfExists<TResult>(this Func<TResult> function)
		{
			Func<TResult> func = function;
			if (func != null)
				return func.Invoke();
			return default(TResult);
		}

		public static TResult InvokeIfExists<T, TResult>(this Func<T, TResult> function, T arg)
		{
			Func<T, TResult> func = function;
			if (func != null)
				return func.Invoke(arg);
			return default(TResult);
		}

		public static TResult InvokeIfExists<T1, T2, TResult>(this Func<T1, T2, TResult> function, T1 arg1, T2 arg2)
		{
			Func<T1, T2, TResult> func = function;
			if (func != null)
				return func.Invoke(arg1, arg2);
			return default(TResult);
		}

	}
}
