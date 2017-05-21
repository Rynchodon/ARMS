using System;
using System.Collections.Generic;
using System.Linq;
using Rynchodon.Attached;
using Rynchodon.Utility;
using Sandbox.ModAPI;
using Sandbox.ModAPI.Interfaces;
using SpaceEngineers.Game.ModAPI;
using VRage.Game.ModAPI;

namespace Rynchodon.Weapons.SystemDisruption
{
	/// <summary>
	/// Hacks a ship, corrupting systems.
	/// </summary>
	public class Hacker
	{

		private const float s_hackStrength = 50f;
		public static readonly TimeSpan s_hackFrequency = new TimeSpan(0, 0, 11);
		public static readonly TimeSpan s_hackLength = new TimeSpan(0, 0, 17);
		//private static float allowedBreakForce = 1f;

		public static bool IsHacker(IMyCubeBlock block)
		{
			if (!(block is IMyLandingGear))
				return false;
			string descr = block.GetCubeBlockDefinition().DescriptionString;
			return descr != null && descr.ToLower().Contains("hacker");
		}

		private readonly IMyLandingGear m_hackBlock;

		private TimeSpan m_nextHack;
		private float m_strengthLeft;

		private Logable Log { get { return new Logable(m_hackBlock); } }

		public Hacker(IMyCubeBlock block)
		{
			m_hackBlock = block as IMyLandingGear;

			Log.DebugLog("created for: " + block.DisplayNameText);
			Log.DebugLog("Not a hacker", Logger.severity.FATAL, condition: !IsHacker(block));
		}

		public void Update10()
		{
			if (Globals.ElapsedTime < m_nextHack)
				return;
			if (!m_hackBlock.IsWorking)
			{
				m_strengthLeft = 0f;
				return;
			}
			IMyCubeGrid attached = m_hackBlock.GetAttachedEntity() as IMyCubeGrid;
			if (attached == null)
			{
				m_strengthLeft = 0f;
				return;
			}

			// break force might be removed from game entirely
			//if (m_hackBlock.BreakForce > allowedBreakForce)
			//{
			//	Log.DebugLog("break force too high: " + m_hackBlock.BreakForce);
			//	ITerminalProperty<float> prop = m_hackBlock.GetProperty("BreakForce") as ITerminalProperty<float>;
			//	if (prop == null)
			//	{
			//		Log.DebugLog("break force is disabled in SE", Logger.severity.INFO);
			//		allowedBreakForce = float.PositiveInfinity;
			//	}
			//	else
			//		prop.SetValue(m_hackBlock, allowedBreakForce);
			//}
			//if (allowedBreakForce == float.PositiveInfinity)

				// landing gear is unbreakable, disconnect / fail if not otherwise attached
				if (!AttachedGrid.IsGridAttached(m_hackBlock.CubeGrid as IMyCubeGrid, attached, AttachedGrid.AttachmentKind.Physics))
				{
					Log.DebugLog("no other connection to attached, hacker must disconnect", Logger.severity.DEBUG);
					ITerminalProperty<bool> autolock = m_hackBlock.GetProperty("Autolock") as ITerminalProperty<bool>;
					if (autolock.GetValue(m_hackBlock))
						autolock.SetValue(m_hackBlock, false);
					m_hackBlock.GetActionWithName("Unlock").Apply(m_hackBlock);
					return;
				}

			m_nextHack = Globals.ElapsedTime + s_hackFrequency;

			m_strengthLeft += s_hackStrength;

			foreach (int i in Enumerable.Range(0, 8).OrderBy(x => Globals.Random.Next()))
			{
				Disruption disrupt;
				switch (i)
				{
					case 0:
						disrupt = new AirVentDepressurize();
						break;
					case 1:
						disrupt = new DoorLock();
						break;
					case 2:
						disrupt = new GravityReverse();
						break;
					case 3:
						disrupt = new DisableTurret();
						break;
					case 4:
						disrupt = new TraitorTurret();
						break;
					case 5:
						disrupt = new CryoChamberMurder();
						break;
					case 6:
						disrupt = new JumpDriveDrain();
						break;
					case 7:
						disrupt = new MedicalRoom();
						break;
					default:
						Log.AlwaysLog("Case not implemented: " + i, Logger.severity.FATAL);
						continue;
				}
				foreach (IMyCubeGrid grid in AttachedGrid.AttachedGrids(attached, AttachedGrid.AttachmentKind.Terminal, true))
					disrupt.Start(grid, s_hackLength, ref m_strengthLeft, m_hackBlock.OwnerId);
			}
		}

	}
}
