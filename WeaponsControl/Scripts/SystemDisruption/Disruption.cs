using System;
using System.Collections.Generic;
using Sandbox.ModAPI;
using VRage.Collections;
using VRage.ObjectBuilders;

namespace Rynchodon.Weapons.SystemDisruption
{
	public abstract class Disruption
	{

		private readonly Logger m_logger;

		private CubeGridCache m_cache;
		private MyObjectBuilderType[] m_affects;
		private SortedDictionary<DateTime, int> m_effects = new SortedDictionary<DateTime, int>();
		private MyUniqueList<IMyFunctionalBlock> m_affected = new MyUniqueList<IMyFunctionalBlock>();
		private List<IMyFunctionalBlock> m_affectedRemovals = new List<IMyFunctionalBlock>();
		private DateTime m_nextExpire;
		private int m_toRemove;

		protected Disruption(IMyCubeGrid grid, MyObjectBuilderType[] affects)
		{
			m_logger = new Logger(GetType().Name, () => grid.DisplayName);
			m_cache = CubeGridCache.GetFor(grid);
			m_affects = affects;
		}

		protected int AddEffect(TimeSpan duration, int strength)
		{
			int applied = 0;
			foreach (MyObjectBuilderType type in m_affects)
			{
				var blockGroup = m_cache.GetBlocksOfType(type);
				if (blockGroup != null)
					foreach (IMyFunctionalBlock block in blockGroup)
					{
						if (!block.IsWorking || m_affected.Contains(block))
						{
							m_logger.debugLog("cannot disrupting: " + block, "AddEffect()");
							continue;
						}
						m_logger.debugLog("disrupting: " + block, "AddEffect()");
						int change = StartEffect(block, strength);
						if (change != 0)
						{
							(block as IMyCubeBlock).SetDamageEffect(true);
							strength -= change;
							applied += change;
							m_affected.Add(block);
							if (strength < 1)
								goto FinishedBlocks;
						}
					}
				else
					m_logger.debugLog("no blocks of type: " + type, "AddEffect()");
			}
FinishedBlocks:
			if (applied != 0)
			{
				m_logger.debugLog("Added new effect, strength: " + applied, "AddEffect()");
				m_effects.Add(DateTime.UtcNow.Add(duration), applied);
				SetNextExpire();
			}
			return strength;
		}

		protected void UpdateEffect()
		{
			if (m_effects.Count == 0)
				return;

			foreach (IMyFunctionalBlock block in m_affected)
				UpdateEffect(block);

			if (DateTime.UtcNow > m_nextExpire)
			{
				if (m_effects.Count == 1)
				{
					m_logger.debugLog("Removing the last effect", "UpdateEffect()");
					m_toRemove = int.MaxValue;
					RemoveEffect();
					m_effects.Remove(m_nextExpire);
					m_toRemove = 0;
				}
				else
				{
					m_logger.debugLog("An effect has expired, strength: " + m_effects[m_nextExpire], "UpdateEffect()");
					m_toRemove += m_effects[m_nextExpire];
					RemoveEffect();
					m_effects.Remove(m_nextExpire);
					SetNextExpire();
				}
			}
		}

		private void RemoveEffect()
		{
			m_logger.debugLog(m_affectedRemovals.Count != 0, "m_affectedRemovals has not been cleared", "AddEffect()", Logger.severity.FATAL);

			foreach (IMyFunctionalBlock block in m_affected)
			{
				int change = EndEffect(block, m_toRemove);
				if (change != 0)
				{
					(block as IMyCubeBlock).SetDamageEffect(false);
					m_toRemove -= change;
					m_affectedRemovals.Add(block);
				}
			}

			foreach (IMyFunctionalBlock block in m_affectedRemovals)
				m_affected.Remove(block);
			m_affectedRemovals.Clear();
		}

		private void SetNextExpire()
		{
			m_logger.debugLog(m_effects.Count == 0, "No effects remain", "SetNextExpire()", Logger.severity.FATAL);

			var enumer = m_effects.GetEnumerator();
			enumer.MoveNext();
			m_nextExpire = enumer.Current.Key;
			m_logger.debugLog("Next effect will expire in " + (m_nextExpire - DateTime.UtcNow), "SetNextExpire()");
		}

		protected abstract int StartEffect(IMyFunctionalBlock block, int strength);
		protected abstract void UpdateEffect(IMyFunctionalBlock block);
		protected abstract int EndEffect(IMyFunctionalBlock block, int strength);
	}
}
