using System;
using Sandbox.ModAPI;

namespace Rynchodon.Autopilot
{
	/// <summary>
	/// For any block that may be directed to point at the sun using motors.
	/// </summary>
	public class Solar
	{
		private readonly IMyCubeBlock myBlock;
		private readonly Logger myLogger;

		private MotorTurret myMotorTurret;
		private ulong updateCount;
		private byte sinceNameChange = 0;

		/// <param name="block">Must be an IMyTerminalBlock</param>
		public Solar(IMyCubeBlock block)
		{
			myBlock = block;
			myLogger = new Logger("Solar", block);
			(myBlock as IMyTerminalBlock).CustomNameChanged += Solar_CustomNameChanged;
			myBlock.OnClose += myBlock_OnClose;
		}

		private void myBlock_OnClose(VRage.ModAPI.IMyEntity obj)
		{
			(myBlock as IMyTerminalBlock).CustomNameChanged -= Solar_CustomNameChanged;
			myBlock.OnClose -= myBlock_OnClose;
		}

		public void Update1()
		{
			try
			{
				updateCount++;

				if (myMotorTurret != null)
					myMotorTurret.FaceTowards(RelativeDirection3F.FromWorld(myBlock.CubeGrid, SunProperties.SunDirection));

				if (sinceNameChange < 2)
				{
					sinceNameChange++;
					myMotorTurret = null;
					return;
				}
				if (sinceNameChange > 2)
					return;

				sinceNameChange++;
				bool nowFace = myBlock.DisplayNameText.looseContains("[face sun]");
				if (nowFace == (myMotorTurret == null))
				{
					if (nowFace)
						myLogger.debugLog("now set to face sun", "Update10()", Logger.severity.INFO);
					else
						myLogger.debugLog("no longer set to face sun", "Update10()", Logger.severity.INFO);

					myMotorTurret = new MotorTurret(myBlock);
					myMotorTurret.RotationSpeedMultiplier = 10f;
					//myMotorTurret.SpeedLimt = 0.25f;
				}
			}
			catch (Exception ex)
			{ myLogger.alwaysLog("Exception: " + ex, "Update1()", Logger.severity.ERROR); }
		}

		private void Solar_CustomNameChanged(IMyTerminalBlock obj)
		{ sinceNameChange = 0; }

	}
}
