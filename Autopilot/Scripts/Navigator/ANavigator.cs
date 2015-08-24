using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Rynchodon.Autopilot.Navigator
{
	public abstract class ANavigator
	{

		public enum NavigatorState : byte
		{
			Off,
			Running,
			/// <summary>The INavigator is waiting for another INavigator to be created before it finishes.</summary>
			Waiting,
			Finished
		}

		public NavigatorState CurrentState { get; protected set; }

		public ANavigator()
		{ CurrentState = NavigatorState.Off; }

		public abstract string ReportableState { get; }

		public abstract void PerformTask();

	}
}
