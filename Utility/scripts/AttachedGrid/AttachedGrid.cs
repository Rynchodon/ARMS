using System.Collections.Generic;
using Sandbox.ModAPI;
using VRage.ModAPI;
using Ingame = Sandbox.ModAPI.Ingame;

namespace Rynchodon.AttachedGrid
{
	public class AttachedGrid
	{
		public enum AttachmentKind : byte { None, Piston, Motor, Connector, LandingGear }

		private static Dictionary<IMyCubeGrid, AttachedGrid> registry = new Dictionary<IMyCubeGrid, AttachedGrid>();

		private readonly Logger myLogger;

		private Dictionary<AttachmentKind, Dictionary<IMyCubeGrid, byte>> Connections = new Dictionary<AttachmentKind, Dictionary<IMyCubeGrid, byte>>();

		internal static void AddRemoveConnection(AttachmentKind kind, Ingame.IMyCubeGrid grid1, Ingame.IMyCubeGrid grid2, bool add)
		{ AddRemoveConnection(kind, grid1 as IMyCubeGrid, grid2 as IMyCubeGrid, add); }

		internal static void AddRemoveConnection(AttachmentKind kind, IMyCubeGrid grid1, IMyCubeGrid grid2, bool add)
		{
			AttachedGrid attached1 = GetFor(grid1),
				attached2 = GetFor(grid2);

			attached1.AddRemoveConnection(kind, grid2, add);
			attached2.AddRemoveConnection(kind, grid1, add);
		}

		private static AttachedGrid GetFor(IMyCubeGrid grid)
		{
			AttachedGrid attached;
			if (!registry.TryGetValue(grid, out attached))
				attached = new AttachedGrid(grid);

			return attached;
		}

		private AttachedGrid(IMyCubeGrid grid)
		{
			this.myLogger = new Logger("AttachedGrid", () => grid.DisplayName);
			registry.Add(grid, this);
			grid.OnClose += grid_OnClose;
		}

		private void grid_OnClose(IMyEntity obj)
		{
			obj.OnClose -= grid_OnClose;
			registry.Remove(obj as IMyCubeGrid);
		}

		private void AddRemoveConnection(AttachmentKind kind, IMyCubeGrid grid, bool add)
		{
			Dictionary<IMyCubeGrid, byte> AttachmentsOfKind;
			if (!Connections.TryGetValue(kind, out AttachmentsOfKind))
			{
				AttachmentsOfKind = new Dictionary<IMyCubeGrid, byte>();
				Connections.Add(kind, AttachmentsOfKind);
			}

			byte numConns;
			if (!AttachmentsOfKind.TryGetValue(grid, out numConns))
				numConns = 0;

			if (add)
				numConns++;
			else
				numConns--;

			if (numConns == 0)
			{
				myLogger.debugLog("For kind " + kind + ", removed all connections to " + grid.DisplayName, "AddRemoveConnection()", Logger.severity.INFO);
				AttachmentsOfKind.Remove(grid);
			}
			else
			{
				myLogger.debugLog("For kind " + kind + ", there are now " + numConns + " connections to " + grid.DisplayName, "AddRemoveConnection()", Logger.severity.INFO);
				AttachmentsOfKind[grid] = numConns;
			}
		}
	}
}
