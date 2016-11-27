using Sandbox.ModAPI;
using VRage.Game.Components;

namespace SteamShipped
{
	[MySessionComponentDescriptor(MyUpdateOrder.AfterSimulation)]
	public class Notify : MySessionComponentBase
	{

		private int updateCount = 0;
		public bool HasNotified = false; // If name changes, change it in UpdateManager as well.

		/// <summary>
		/// Notifies the player that they need to download Load-ARMS. ARMS will set HasNotified to true if it is running.
		/// </summary>
		public override void UpdateAfterSimulation()
		{
			if (HasNotified)
				return;

			updateCount++;
			if (updateCount < 100 || MyAPIGateway.Session == null || MyAPIGateway.Utilities == null || MyAPIGateway.Input == null || !MyAPIGateway.Input.IsAnyKeyPress())
				return;

			HasNotified = true;

			MyAPIGateway.Utilities.ShowNotification("ARMS needs Load-ARMS to run, see the steam page for download link", 60000);
			MyAPIGateway.Session.UnregisterComponent(this);
		}

	}
}
