using System;
using SpaceEngineers.Game.ModAPI;
using VRage.Game.ModAPI;
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
			this.myLogger = new Logger(block);
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
					myLogger.debugLog("Is now attached to: " + myGear.GetAttachedEntity().getBestName(), Logger.severity.INFO);
					IMyCubeGrid attached = myGear.GetAttachedEntity() as IMyCubeGrid;
					if (attached != null)
						Attach(attached);
					else
						Detach();
				}
				else
				{
					myLogger.debugLog("Is now disconnected", Logger.severity.INFO);
					Detach();
				}
			}
			catch (Exception ex)
			{
				myLogger.alwaysLog("Exception: " + ex, Logger.severity.ERROR);
				Logger.DebugNotify("LandingGear encountered an exception", 10000, Logger.severity.ERROR);
			}
		}
	}
}
