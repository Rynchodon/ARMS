using System;
using System.Collections.Generic;
using Rynchodon.Autopilot.Pathfinding;

namespace Rynchodon.Autopilot.Navigator
{
	/// <summary>
	/// Common methods for landing and unlanding.
	/// </summary>
	abstract class ALand : NavigatorMover, INavigatorRotator, IDisposable
	{
		private static HashSet<long> ReservedTargets = new HashSet<long>();

		private long m_reservedTarget = 0L;

		public ALand(Pathfinder pathfinder) : base(pathfinder) { }

		~ALand()
		{
			Dispose();
		}

		public abstract void Rotate();

		public void Dispose()
		{
			UnreserveTarget();
		}

		protected void UnreserveTarget()
		{
			if (m_reservedTarget != 0L)
			{
				Logger.DebugLog("unreserve: " + m_reservedTarget);
				ReservedTargets.Remove(m_reservedTarget);
				m_reservedTarget = 0L;
			}
		}

		protected bool CanReserveTarget(long target)
		{
			return target == m_reservedTarget || !ReservedTargets.Contains(target);
		}

		protected bool ReserveTarget(long target)
		{
			if (target == m_reservedTarget)
				return true;

			UnreserveTarget();

			if (ReservedTargets.Add(target))
			{
				Logger.DebugLog("reserve: " + target);
				m_reservedTarget = target;
				return true;
			}
			Logger.DebugLog("cannot reserve: " + target);
			return false;
		}

	}
}
