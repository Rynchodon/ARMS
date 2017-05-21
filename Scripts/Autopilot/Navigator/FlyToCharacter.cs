using System;
using System.Text;
using Rynchodon.AntennaRelay;
using Rynchodon.Autopilot.Pathfinding;
using Rynchodon.Utility;
using VRage.Game.ModAPI;
using VRageMath;

namespace Rynchodon.Autopilot.Navigator
{
	public class FlyToCharacter : NavigatorMover, INavigatorRotator
	{

		private enum Stage : byte { Searching, Flying, Idle }

		private static readonly TimeSpan timeout = new TimeSpan(0, 1, 0);

		private readonly string m_charName;
		private readonly string m_charName_orig;

		private ulong m_nextLastSeen;
		private LastSeen m_character;
		private TimeSpan m_timeoutAt;

		private Logable Log
		{
			get { return new Logable(m_controlBlock.CubeBlock, m_charName_orig); }
		}

		public FlyToCharacter(Pathfinder mover, string charName)
			: base(mover)
		{
			this.m_charName_orig = charName;
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
					Log.DebugLog("terminating search");
					m_navSet.OnTaskComplete_NavMove();
				}
				return;
			}

			if (m_navSet.DistanceLessThanDestRadius())
			{
				Log.DebugLog("Reached player", Logger.severity.INFO);
				m_navSet.OnTaskComplete_NavMove();
			}
			else
			{
				Destination destination = new Destination(m_character.Entity, Vector3D.Zero);
				m_pathfinder.MoveTo(destinations: destination);
			}
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
				Log.DebugLog("failed to get storage", Logger.severity.INFO);
				m_character = null;
				return;
			}

			if (m_character != null)
				if (store.TryGetLastSeen(m_character.Entity.EntityId, out m_character))
				{
					Log.DebugLog("got updated last seen");
					return;
				}
				else
				{
					Log.DebugLog("failed to update last seen", Logger.severity.WARNING);
					m_character = null;
					m_timeoutAt = Globals.ElapsedTime + timeout;
				}

			store.SearchLastSeen((LastSeen seen) => {
				Log.DebugLog("seen: " + seen.Entity.getBestName());
				if (seen.Entity is IMyCharacter && seen.Entity.DisplayName.LowerRemoveWhitespace().Contains(m_charName))
				{
					Log.DebugLog("found a last seen for character");
					m_character = seen;
					return true;
				}
				return false;
			});

			Log.DebugLog("failed to find a character from last seen", condition: m_character == null);
		}

	}
}
