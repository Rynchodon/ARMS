using System;
using System.Collections.Generic;
using System.Linq;
using Sandbox.ModAPI;

namespace Rynchodon.Weapons.SystemDisruption
{
	/// <summary>
	/// Hacks a ship, corrupting systems.
	/// </summary>
	public class Hacker
	{

		private const int s_hackStrength = 100;
		private static readonly TimeSpan s_hackFrequency = new TimeSpan(0, 0, 17);
		private static readonly TimeSpan s_hackLength = new TimeSpan(0, 0, 11);

		public static bool IsHacker(IMyCubeBlock block)
		{
			if (!(block is IMyLandingGear))
				return false;
			string descr = block.GetCubeBlockDefinition().DescriptionString;
			return descr != null && descr.ToLower().Contains("hacker");
		}

		private readonly Logger m_logger;
		private readonly IMyLandingGear m_hackBlock;

		private DateTime m_nextHack;
		private int m_strengthLeft;

		public Hacker(IMyCubeBlock block)
		{
			m_logger = new Logger(GetType().Name, block);
			m_hackBlock = block as IMyLandingGear;

			m_logger.debugLog("created for: " + block.DisplayNameText, "Hacker()");
			m_logger.debugLog(!IsHacker(block), "Not a hacker", "Hacker()", Logger.severity.FATAL);
		}

		public void Update10()
		{
			if (DateTime.UtcNow < m_nextHack )
				return;
			if (!m_hackBlock.IsWorking)
			{
				m_strengthLeft = 0;
				return;
			}
			IMyCubeGrid attached = m_hackBlock.GetAttachedEntity() as IMyCubeGrid;
			if (attached == null)
			{
				m_strengthLeft = 0;
				return;
			}

			m_nextHack = DateTime.UtcNow + s_hackFrequency;

			m_strengthLeft += s_hackStrength;
			List<long> bigOwners = (m_hackBlock.CubeGrid as IMyCubeGrid).BigOwners;
			long effectOwner = bigOwners == null || bigOwners.Count == 0 ? 0L : bigOwners[0];

			foreach (int i in Enumerable.Range(0, 5).OrderBy(x => Globals.Random.Next()))
				switch (i)
				{
					case 0:
						m_strengthLeft = AirVentDepressurize.Depressurize(attached, m_strengthLeft, s_hackLength, effectOwner);
						break;
					case 1:
						m_strengthLeft = DoorLock.LockDoors(attached, m_strengthLeft, s_hackLength, effectOwner);
						break;
					case 2:
						m_strengthLeft = GravityReverse.ReverseGravity(attached, m_strengthLeft, s_hackLength);
						break;
					case 3:
						m_strengthLeft = DisableTurret.DisableTurrets(attached, m_strengthLeft, s_hackLength);
						break;
					case 4:
						m_strengthLeft = TraitorTurret.TurnTurrets(attached, m_strengthLeft, s_hackLength, effectOwner);
						break;
					default:
						m_logger.alwaysLog("Case not implemented: " + i, "Update10()", Logger.severity.WARNING);
						break;
				}
		}

	}
}
