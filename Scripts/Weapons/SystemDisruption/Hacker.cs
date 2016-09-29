using System;
using System.Collections.Generic;
using System.Linq;
using Rynchodon.Attached;
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
		private static readonly TimeSpan s_hackFrequency = new TimeSpan(0, 0, 11);
		private static readonly TimeSpan s_hackLength = new TimeSpan(0, 0, 17);
		private static float allowedBreakForce = 1f;

		public static bool IsHacker(IMyCubeBlock block)
		{
			if (!(block is IMyLandingGear))
				return false;
			string descr = block.GetCubeBlockDefinition().DescriptionString;
			return descr != null && descr.ToLower().Contains("hacker");
		}

		private readonly Logger m_logger;
		private readonly IMyLandingGear m_hackBlock;

		private TimeSpan m_nextHack;
		private float m_strengthLeft;

		public Hacker(IMyCubeBlock block)
		{
			m_logger = new Logger(block);
			m_hackBlock = block as IMyLandingGear;

			m_logger.debugLog("created for: " + block.DisplayNameText);
			m_logger.debugLog("Not a hacker", Logger.severity.FATAL, condition: !IsHacker(block));
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
			if (m_hackBlock.BreakForce > allowedBreakForce)
			{
				m_logger.debugLog("break force too high: " + m_hackBlock.BreakForce);
				ITerminalProperty<float> prop = m_hackBlock.GetProperty("BreakForce") as ITerminalProperty<float>;
				if (prop == null)
				{
					m_logger.debugLog("break force is disabled in SE", Logger.severity.INFO);
					allowedBreakForce = float.PositiveInfinity;
				}
				else
					prop.SetValue(m_hackBlock, allowedBreakForce);
			}
			if (allowedBreakForce == float.PositiveInfinity)
				// landing gear is unbreakable, disconnect / fail if not otherwise attached
				if (!AttachedGrid.IsGridAttached(m_hackBlock.CubeGrid as IMyCubeGrid, attached, AttachedGrid.AttachmentKind.Physics))
				{
					m_logger.debugLog("no other connection to attached, hacker must disconnect", Logger.severity.DEBUG);
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
						// vanilla turret controls are not respecting the change in ownership
						continue;
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
						m_logger.alwaysLog("Case not implemented: " + i, Logger.severity.FATAL);
						continue;
				}
				AttachedGrid.RunOnAttached(attached, AttachedGrid.AttachmentKind.Terminal, grid => {
					disrupt.Start(grid, s_hackLength, ref m_strengthLeft, m_hackBlock.OwnerId);
					return false;
				}, true);
			}
		}

	}
}
