using System; // (partial) from mscorlib.dll
using System.Text; // from mscorlib.dll
using Rynchodon.AntennaRelay;
using Rynchodon.Autopilot.Data;
using Rynchodon.Autopilot.Movement;
using Sandbox.ModAPI; // from Sandbox.Common.dll
using VRage.Game.ModAPI;

namespace Rynchodon.Autopilot.Navigator
{
	public class FlyToCharacter : NavigatorMover, INavigatorRotator
	{

		private enum Stage : byte { Searching, Flying, Idle }

		private static readonly TimeSpan timeout = new TimeSpan(0, 1, 0);

		private readonly Logger m_logger;
		private readonly string m_charName;

		private ulong m_nextLastSeen;
		private LastSeen m_character;
		private TimeSpan m_timeoutAt;

		public FlyToCharacter(Mover mover, string charName)
			: base(mover)
		{
			this.m_logger = new Logger(m_controlBlock.CubeBlock, () => charName);
			this.m_charName = charName.LowerRemoveWhitespace();
			this.m_timeoutAt = Globals.ElapsedTime + timeout;

			m_navSet.Settings_Task_NavMove.NavigatorMover = this;
		}

		public override void Move()
		{
			if (Globals.UpdateCount >= m_nextLastSeen)
			{
				m_nextLastSeen = Globals.UpdateCount + 100ul;
				UpdateLastSeen();
			}

			if (m_character == null)
			{
				m_mover.StopMove();
				if (Globals.ElapsedTime > m_timeoutAt)
				{
					m_logger.debugLog("terminating search");
					m_navSet.OnTaskComplete_NavMove();
				}
				return;
			}

			if (m_navSet.DistanceLessThanDestRadius())
			{
				m_logger.debugLog("Reached player", Logger.severity.INFO);
				m_navSet.OnTaskComplete_NavMove();
			}
			else
				m_mover.CalcMove(m_navSet.Settings_Current.NavigationBlock, m_character.Entity.GetPosition(), m_character.Entity.Physics.LinearVelocity, true);
		}

		public void Rotate()
		{
			if (m_character != null)
				m_mover.CalcRotate();
			else
				m_mover.StopRotate();
		}

		public override void AppendCustomInfo(StringBuilder customInfo)
		{
			if (m_character != null)
				customInfo.Append("Flying to player: ");
			else
				customInfo.Append("Searching for player: ");
			customInfo.AppendLine(m_charName);
		}

		private void UpdateLastSeen()
		{
			RelayStorage store = m_controlBlock.NetworkStorage;
			if (store == null)
			{
				m_logger.debugLog("failed to get storage", Logger.severity.INFO);
				m_character = null;
				return;
			}

			if (m_character != null)
				if (store.TryGetLastSeen(m_character.Entity.EntityId, out m_character))
				{
					m_logger.debugLog("got updated last seen");
					return;
				}
				else
				{
					m_logger.debugLog("failed to update last seen", Logger.severity.WARNING);
					m_character = null;
					m_timeoutAt = Globals.ElapsedTime + timeout;
				}

			store.SearchLastSeen((LastSeen seen) => {
				m_logger.debugLog("seen: " + seen.Entity.getBestName());
				if (seen.Entity is IMyCharacter && seen.Entity.DisplayName.LowerRemoveWhitespace().Contains(m_charName))
				{
					m_logger.debugLog("found a last seen for character");
					m_character = seen;
					return true;
				}
				return false;
			});

			m_logger.debugLog("failed to find a character from last seen", condition: m_character == null);
		}

	}
}
