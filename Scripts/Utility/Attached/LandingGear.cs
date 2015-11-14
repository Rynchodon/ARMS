using System;
using Sandbox.ModAPI;
using VRage.ModAPI;

namespace Rynchodon.Attached
{
	public class LandingGear : AttachableBlockBase
	{
		private readonly Logger myLogger;

		private IMyLandingGear myGear { get { return myBlock as IMyLandingGear; } }

		public LandingGear(IMyCubeBlock block)
			: base (block, AttachedGrid.AttachmentKind.LandingGear)
		{
			this.myLogger = new Logger("LandingGear", block);
			this.myGear.StateChanged += myGear_StateChanged;

			IMyCubeGrid attached = myGear.GetAttachedEntity() as IMyCubeGrid;
			if (attached != null)
				Attach(attached);

			myGear.OnClosing += myGear_OnClosing;
		}

		private void myGear_OnClosing(IMyEntity obj)
		{
			myGear.StateChanged -= myGear_StateChanged;
		}

		private void myGear_StateChanged(bool obj)
		{
			try
			{
				if (myGear.IsLocked)
				{
					myLogger.debugLog("Is now attached to: " + myGear.GetAttachedEntity().getBestName(), "myGear_StateChanged()", Logger.severity.INFO);
					IMyCubeGrid attached = myGear.GetAttachedEntity() as IMyCubeGrid;
					if (attached != null)
						Attach(attached);
					else
						Detach();
				}
				else
				{
					myLogger.debugLog("Is now disconnected", "myGear_StateChanged()", Logger.severity.INFO);
					Detach();
				}
			}
			catch (Exception ex)
			{
				myLogger.alwaysLog("Exception: " + ex, "myGear_StateChanged()", Logger.severity.ERROR);
				Logger.debugNotify("LandingGear encountered an exception", 10000, Logger.severity.ERROR);
			}
		}
	}
}
