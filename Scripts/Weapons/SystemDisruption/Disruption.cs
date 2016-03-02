using System;
using System.Collections.Generic;
using System.Linq;
using Sandbox.Game.Entities;
using Sandbox.ModAPI;
using VRage.Game;
using VRage.ObjectBuilders;

namespace Rynchodon.Weapons.SystemDisruption
{
	public abstract class Disruption
	{

		private static HashSet<IMyCubeBlock> m_allAffected = new HashSet<IMyCubeBlock>();

		protected readonly Logger m_logger;

		private CubeGridCache m_cache;
		private MyObjectBuilderType[] m_affects;
		private SortedDictionary<DateTime, int> m_effects = new SortedDictionary<DateTime, int>();
		private Dictionary<IMyCubeBlock, MyIDModule> m_affected = new Dictionary<IMyCubeBlock, MyIDModule>();
		private List<IMyCubeBlock> m_affectedRemovals = new List<IMyCubeBlock>();
		private DateTime m_nextExpire;
		private int m_toRemove;

		/// <summary>When strength is less than this value, stop trying to start an effect.</summary>
		protected virtual int MinCost { get { return 1; } }

		protected Disruption(IMyCubeGrid grid, MyObjectBuilderType[] affects)
		{
			m_logger = new Logger(GetType().Name, () => grid.DisplayName);
			m_cache = CubeGridCache.GetFor(grid);
			m_affects = affects;
		}

		/// <summary>
		/// Adds an effect to the grid.
		/// </summary>
		/// <param name="duration">The length of time the effect will last for.</param>
		/// <param name="strength">The amount of effect available.</param>
		/// <param name="effectOwner">The player that will be allowed to control the block. By default, no one can access.</param>
		/// <returns>Strength remaining after effect is applied</returns>
		protected int AddEffect(TimeSpan duration, int strength, long effectOwner = long.MinValue)
		{
			if (strength < MinCost)
			{
				m_logger.debugLog("strength: " + strength + ", below minimum: " + MinCost, "AddEffect()");
				return strength;
			}

			int applied = 0;
			foreach (MyObjectBuilderType type in m_affects)
			{
				var blockGroup = m_cache.GetBlocksOfType(type);
				if (blockGroup != null)
				{
					foreach (IMyCubeBlock block in blockGroup.OrderBy(OrderBy))
					{
						if (!block.IsWorking || m_allAffected.Contains(block))
						{
							m_logger.debugLog("cannot disrupt: " + block, "AddEffect()");
							continue;
						}
						int cost = BlockCost(block);
						if (cost > strength)
						{
							m_logger.debugLog("cannot disrupt block: " + block + ", cost: " + cost + " is greater than strength available: " + strength, "AddEffect()");
							continue;
						}

						StartEffect(block);
						m_logger.debugLog("disrupting: " + block + ", cost: " + cost + ", remaining strength: " + strength, "AddEffect()");
						strength -= cost;
						applied += cost;
						MyCubeBlock cubeBlock = block as MyCubeBlock;
						MyIDModule idMod = new MyIDModule() { Owner = cubeBlock.IDModule.Owner, ShareMode = cubeBlock.IDModule.ShareMode };
						m_affected.Add(block, idMod);
						m_allAffected.Add(block);

						block.SetDamageEffect(true);
						cubeBlock.ChangeOwner(effectOwner, MyOwnershipShareModeEnum.Faction);

						if (strength < MinCost)
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

		/// <summary>
		/// Checks for effects ending and removes them.
		/// </summary>
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

		/// <summary>
		/// Removes an effect that has expired.
		/// </summary>
		private void RemoveEffect()
		{
			m_logger.debugLog(m_affectedRemovals.Count != 0, "m_affectedRemovals has not been cleared", "AddEffect()", Logger.severity.FATAL);

			foreach (var pair in m_affected)
			{
				IMyCubeBlock block = pair.Key;
				int cost = BlockCost(block);
				if (cost > m_toRemove)
				{
					m_logger.debugLog("cannot remove disruption from: " + block + ", cost: " + cost + " is greater than m_toRemove: " + m_toRemove, "AddEffect()");
					continue;
				}

				EndEffect(block);
				m_toRemove -= cost;
				m_affectedRemovals.Add(block);

				block.SetDamageEffect(false);
				MyCubeBlock cubeBlock = block as MyCubeBlock;
				cubeBlock.ChangeOwner(pair.Value.Owner, pair.Value.ShareMode);
			}

			foreach (IMyCubeBlock block in m_affectedRemovals)
			{
				m_affected.Remove(block);
				m_allAffected.Remove(block);
			}
			m_affectedRemovals.Clear();
		}

		/// <summary>
		/// Finds when the next effect expiration will occur.
		/// </summary>
		private void SetNextExpire()
		{
			m_logger.debugLog(m_effects.Count == 0, "No effects remain", "SetNextExpire()", Logger.severity.FATAL);

			var enumer = m_effects.GetEnumerator();
			enumer.MoveNext();
			m_nextExpire = enumer.Current.Key;
			m_logger.debugLog("Next effect will expire in " + (m_nextExpire - DateTime.UtcNow), "SetNextExpire()");
		}

		/// <summary>
		/// Blocks will be affected in ascending order of the values returned by this function.
		/// </summary>
		/// <param name="block">Block to order.</param>
		/// <returns>An integer that affects the order blocks are affected in.</returns>
		protected virtual int OrderBy(IMyCubeBlock block)
		{
			return Globals.Random.Next();
		}

		/// <summary>
		/// The cost of starting an effect on this block.
		/// </summary>
		/// <param name="block">The block the disruption may be started on,.</param>
		/// <returns>The cost of starting an effect on this block.</returns>
		protected virtual int BlockCost(IMyCubeBlock block)
		{
			return MinCost;
		}

		/// <summary>
		/// Starts the effect on a block, the ownership will be changed and visual effect will be added by Disruption class.
		/// </summary>
		/// <param name="block">The block to try the disruption on.</param>
		protected virtual void StartEffect(IMyCubeBlock block) { }

		/// <summary>
		/// Ends a disruption on a block, the ownership will be changed and visual effect will be added by Disruption class.
		/// </summary>
		/// <param name="block">The block to try to end the disruption on.</param>
		protected virtual void EndEffect(IMyCubeBlock block) { }

	}
}
