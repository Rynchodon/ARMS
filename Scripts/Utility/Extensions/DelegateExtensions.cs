using System;

namespace Rynchodon.Utility
{
	public static class DelegateExtensions
	{

		public static void InvokeIfExists(this Action action)
		{
			Action handler = action;
			if (handler != null)
				handler.Invoke();
		}

	}
}
