using System.Collections.Generic;
using Sandbox.Game.Entities;
using Sandbox.Game.Gui;
using Sandbox.ModAPI;
using Sandbox.ModAPI.Interfaces.Terminal;
using VRage.Game.ModAPI;
using VRage.ModAPI;
using VRage.Utils;
using VRageMath;

namespace Rynchodon.Autopilot.Instruction
{
	/// <summary>
	/// Create a GOLIS from the GPS list.
	/// </summary>
	public class CommandGolisGps : CommandGolisCoordinate
	{

		public override ACommand CreateCommand()
		{
			return new CommandGolisGps();
		}

		public override void AddControls(List<IMyTerminalControl> controls)
		{
			IMyTerminalControlListbox gpsList = new MyTerminalControlListbox<MyShipController>("GolisGpsList", MyStringId.GetOrCompute("GPS list"), MyStringId.NullOrEmpty, false, 18);
			gpsList.ListContent = FillWaypointList;
			gpsList.ItemSelected = OnItemSelected;
			controls.Add(gpsList);
		}

		private void FillWaypointList(IMyTerminalBlock dontCare, List<MyTerminalControlListBoxItem> allItems, List<MyTerminalControlListBoxItem> selected)
		{
			List<IMyGps> gpsList = MyAPIGateway.Session.GPS.GetGpsList(MyAPIGateway.Session.Player.IdentityId);
			bool select = destination.IsValid();
			foreach (IMyGps gps in gpsList)
			{
				// this will leak memory, as MyTerminalControlListBoxItem uses MyStringId for some stupid reason
				MyTerminalControlListBoxItem item = new MyTerminalControlListBoxItem(MyStringId.GetOrCompute(gps.Name), MyStringId.GetOrCompute(gps.Description), gps);
				allItems.Add(item);

				if (select && selected.Count == 0 && gps.Coords == destination)
					selected.Add(item);
			}
		}

		private void OnItemSelected(IMyTerminalBlock dontCare, List<MyTerminalControlListBoxItem> selected)
		{
			Logger.debugLog("CommandGolisGps", "selected.Count: " + selected.Count, Logger.severity.ERROR, condition: selected.Count > 1);

			if (selected.Count == 0)
				destination = new Vector3D(double.NaN, double.NaN, double.NaN);
			else
				destination = ((IMyGps)selected[0].UserData).Coords;
		}

	}

}
