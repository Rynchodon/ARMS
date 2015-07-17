using Sandbox.ModAPI;
using VRage.ModAPI;

namespace Rynchodon.AttachedGrid
{
	public class LandingGear
	{
		private readonly Logger myLogger;
		private readonly IMyLandingGear myGear;

		private IMyEntity value_Attached;

		public LandingGear(IMyCubeBlock block)
		{
			this.myGear = block as IMyLandingGear;
			this.myLogger = new Logger("LandingGear", block);

			this.myGear.StateChanged += myGear_StateChanged;
		}

		internal IMyEntity AttachedEntity
		{
			get { return value_Attached; }
			private set
			{
				if (value_Attached != null)
				{
					IMyCubeGrid asGrid = value_Attached as IMyCubeGrid;
					if (asGrid != null)
						AttachedGrid.AddRemoveConnection(AttachedGrid.AttachmentKind.LandingGear, myGear.CubeGrid, asGrid, false);
				}

				value_Attached = value;

				if (value_Attached != null)
				{
					IMyCubeGrid asGrid = value_Attached as IMyCubeGrid;
					if (asGrid != null)
						AttachedGrid.AddRemoveConnection(AttachedGrid.AttachmentKind.LandingGear, myGear.CubeGrid, asGrid, true);
				}
			}
		}

		private void myGear_StateChanged(bool obj)
		{
			if (myGear.IsLocked)
			{
				myLogger.debugLog("Is now attached to: " + myGear.GetAttachedEntity().getBestName(), "myGear_StateChanged()", Logger.severity.INFO);
				AttachedEntity = myGear.GetAttachedEntity();
			}
			else
			{
				myLogger.debugLog("Is now disconnected", "myGear_StateChanged()", Logger.severity.INFO);
				AttachedEntity = null;
			}
		}
	}
}
