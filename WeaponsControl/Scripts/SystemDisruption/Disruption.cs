using System;
using System.Collections.Generic;
using Sandbox.Common.ObjectBuilders;
using Sandbox.Game.Entities;
using Sandbox.ModAPI;
using VRage.ObjectBuilders;

namespace Rynchodon.Weapons.SystemDisruption
{
	public abstract class Disruption
	{

		private readonly Logger m_logger;

		private CubeGridCache m_cache;
		private MyObjectBuilderType[] m_affects;
		private SortedDictionary<DateTime, int> m_effects = new SortedDictionary<DateTime, int>();
		private Dictionary<IMyCubeBlock, MyIDModule> m_affected = new Dictionary<IMyCubeBlock, MyIDModule>();
		private List<IMyCubeBlock> m_affectedRemovals = new List<IMyCubeBlock>();
		private DateTime m_nextExpire;
		private int m_toRemove;

		protected Disruption(IMyCubeGrid grid, MyObjectBuilderType[] affects)
		{
			m_logger = new Logger(GetType().Name, () => grid.DisplayName);
			m_cache = CubeGridCache.GetFor(grid);
			m_affects = affects;
		}

		protected int AddEffect(TimeSpan duration, int strength, long effectOwner)
		{
			int applied = 0;
			foreach (MyObjectBuilderType type in m_affects)
			{
				var blockGroup = m_cache.GetBlocksOfType(type);
				if (blockGroup != null)
					foreach (IMyCubeBlock block in blockGroup)
					{
						if (!block.IsWorking || m_affected.ContainsKey(block))
						{
							m_logger.debugLog("cannot disrupt: " + block, "AddEffect()");
							continue;
						}
						m_logger.debugLog("disrupting: " + block, "AddEffect()");
						int change = StartEffect(block, strength);
						if (change != 0)
						{
							strength -= change;
							applied += change;
							MyCubeBlock cubeBlock = block as MyCubeBlock;
							MyIDModule idMod = new MyIDModule() { Owner = cubeBlock.IDModule.Owner, ShareMode = cubeBlock.IDModule.ShareMode };
							m_affected.Add(block, idMod);

							block.SetDamageEffect(true);
							cubeBlock.ChangeOwner(effectOwner, MyOwnershipShareModeEnum.Faction);

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

			foreach (var pair in m_affected)
			{
				IMyCubeBlock block = pair.Key;
				int change = EndEffect(block, m_toRemove);
				if (change != 0)
				{
					m_toRemove -= change;
					m_affectedRemovals.Add(block);

					block.SetDamageEffect(false);
					MyCubeBlock cubeBlock = block as MyCubeBlock;
					cubeBlock.ChangeOwner(pair.Value.Owner, pair.Value.ShareMode);
				}
			}

			foreach (IMyCubeBlock block in m_affectedRemovals)
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

		protected abstract int StartEffect(IMyCubeBlock block, int strength);
		protected abstract int EndEffect(IMyCubeBlock block, int strength);
	}
}
