using System.Collections.Generic;
using System.Linq;
using Sandbox.Game.Entities;
using Sandbox.Game.Gui;
using Sandbox.Graphics.GUI;
using Sandbox.ModAPI;
using Sandbox.ModAPI.Interfaces.Terminal;
using VRage.Game.ModAPI;
using VRage.Utils;
using VRageMath;

namespace Rynchodon.Autopilot.Instruction.Command
{
	/// <summary>
	/// Create a GOLIS from the GPS list.
	/// </summary>
	public class GolisGps : GolisCoordinate
	{

		public override ACommand Clone()
		{
			return new GolisGps() { destination = destination };
		}

		public override string Identifier
		{
			get { return "cg"; }
		}

		public override string AddName
		{
			get { return "GPS"; }
		}

		public override string AddDescription
		{
			get { return "Fly to coordinates chosen from the GPS list."; }
		}

		public override void AddControls(List<IMyTerminalControl> controls)
		{
			MyTerminalControlListbox<MyShipController> gpsList = new MyTerminalControlListbox<MyShipController>("GolisGpsList", MyStringId.GetOrCompute("GPS list"), MyStringId.NullOrEmpty, false, 18);
			gpsList.ListContent = FillWaypointList;
			gpsList.ItemSelected = OnItemSelected;
			controls.Add(gpsList);
		}

		private void FillWaypointList(IMyTerminalBlock dontCare, ICollection<MyGuiControlListbox.Item> allItems, ICollection<MyGuiControlListbox.Item> selected)
		{
			List<IMyGps> gpsList = MyAPIGateway.Session.GPS.GetGpsList(MyAPIGateway.Session.Player.IdentityId);
			bool select = destination.IsValid();
			foreach (IMyGps gps in gpsList)
			{
				MyGuiControlListbox.Item item = new MyGuiControlListbox.Item(new System.Text.StringBuilder(gps.Name), gps.Description, userData: gps);
				allItems.Add(item);

				if (select && selected.Count == 0 && gps.Coords == destination)
					selected.Add(item);
			}
		}

		private void OnItemSelected(IMyTerminalBlock dontCare, ICollection<MyGuiControlListbox.Item> selected)
		{
			Logger.DebugLog("selected.Count: " + selected.Count, Logger.severity.ERROR, condition: selected.Count > 1);

			if (selected.Count == 0)
				destination = new Vector3D(double.NaN, double.NaN, double.NaN);
			else
				destination = ((IMyGps)selected.First().UserData).Coords;
		}

	}
}
