using System;
using System.Collections.Generic;
using Rynchodon.AntennaRelay;
using Sandbox.ModAPI;
using VRage.ModAPI;

namespace Rynchodon.Weapons
{
	/// <summary>
	/// Tracks the entities targeting something.
	/// </summary>
	public class TargeterTracker
	{

		private Dictionary<long, List<long>> m_data = new Dictionary<long, List<long>>();

		public TargeterTracker()
		{
			MyAPIGateway.Entities.OnCloseAll += Entities_OnCloseAll;
		}

		private void Entities_OnCloseAll()
		{
			MyAPIGateway.Entities.OnCloseAll -= Entities_OnCloseAll;
			m_data.Clear();
		}

		public void AddTarget(long target, long entityTargeting)
		{
			Logger.DebugLog(entityTargeting + " now targeting " + target);
			List<long> targeters;
			if (!m_data.TryGetValue(target, out targeters))
			{
				targeters = new List<long>();
				m_data.Add(target, targeters);
			}
			if (targeters.Contains(entityTargeting))
				throw new Exception(entityTargeting + " is already targeting " + target);
			targeters.Add(entityTargeting);
			Logger.DebugLog("count is now " + targeters.Count);
		}

		public void ChangeTarget(IMyEntity oldTarget, IMyEntity newTarget, long entityTargeting)
		{
			if (oldTarget != null && newTarget != null && oldTarget.EntityId == newTarget.EntityId)
				return;

			if (oldTarget != null)
				RemoveTarget(oldTarget.EntityId, entityTargeting);
			if (newTarget != null)
				AddTarget(newTarget.EntityId, entityTargeting);
		}

		public void ChangeTarget(LastSeen oldTarget, LastSeen newTarget, long entityTargeting)
		{
			if (oldTarget != null && newTarget != null && oldTarget.Entity.EntityId == newTarget.Entity.EntityId)
				return;

			if (oldTarget != null)
			{
				Logger.DebugLog("old target is not null, remove");
				RemoveTarget(oldTarget.Entity.EntityId, entityTargeting);
			}
			if (newTarget != null)
			{
				Logger.DebugLog("new target is not null, add");
				AddTarget(newTarget.Entity.EntityId, entityTargeting);
			}
		}

		public int GetCount(long target)
		{
			List<long> targeters;
			if (m_data.TryGetValue(target, out targeters))
				return targeters.Count;
			return 0;
		}

		public void RemoveTarget(long target, long entityTargeting)
		{
			Logger.DebugLog(entityTargeting + " no longer targeting " + target);
			List<long> targeters;
			if (!m_data.TryGetValue(target, out targeters))
				throw new Exception("Target is not present: " + target);
			if (!targeters.Remove(entityTargeting))
				throw new Exception(entityTargeting + " is not targeting " + target);
			if (targeters.Count == 0)
				m_data.Remove(target);
		}

	}
}
