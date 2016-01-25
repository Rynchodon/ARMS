using System;
using Sandbox.ModAPI;

namespace Rynchodon
{
	public static class Globals
	{

		#region SE Constants

		public const int UpdatesPerSecond = 60;

		#endregion SE Constants

		/// <summary>Duration of one update in seconds.</summary>
		public const float UpdateDuration = 1f / (float)UpdatesPerSecond;

		public static readonly Random Random = new Random();

		/// <summary>The number of updates since mod started.</summary>
		public static ulong UpdateCount = 0;

		private static bool Reported_GUI_Error;

		public static void GUI_NotLoaded()
		{
			if (Reported_GUI_Error)
				return;
			Reported_GUI_Error = true;

			MyAPIGateway.Utilities.ShowMissionScreen("ARMS Error", string.Empty, "Mod: Autopilot, Radar, and Military Systems", 
				 "ARMS did not load correctly, terminal controls will not function. After starting Space Engineers, ARMS must be in the first world loaded for terminal controls to work correctly.");
		}

	}
}
