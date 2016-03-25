using System;
using System.Collections.Generic;
using System.Linq;
using Rynchodon.Update;
using Sandbox.Game.Entities;
using Sandbox.ModAPI;
using VRage.Game;
using VRage.Game.ModAPI;
using VRage.ObjectBuilders;

namespace Rynchodon.Weapons.SystemDisruption
{
	public abstract class Disruption
	{

		private const uint UpdateFrequency = 10u;

		private static HashSet<IMyCubeBlock> m_allAffected = new HashSet<IMyCubeBlock>();

		static Disruption()
		{
			MyAPIGateway.Entities.OnCloseAll += Entities_OnCloseAll;
		}

		static void Entities_OnCloseAll()
		{
			MyAPIGateway.Entities.OnCloseAll -= Entities_OnCloseAll;
			m_allAffected = null;
		}

		protected Logger m_logger { get; private set; }

		private TimeSpan m_expire;
		private Dictionary<IMyCubeBlock, MyIDModule> m_affected = new Dictionary<IMyCubeBlock, MyIDModule>();

		/// <summary>When strength is less than this value, stop trying to start an effect.</summary>
		protected virtual int MinCost { get { return 1; } }

		/// <summary>Iff true, the owner of the disruption effect can access the blocks while disrupted.</summary>
		protected virtual bool EffectOwnerCanAccess { get { return false; } }

		/// <summary>Block types that are affected by the disruption</summary>
		protected abstract MyObjectBuilderType[] BlocksAffected { get; }

		/// <summary>
		/// Adds a disruption effect to a grid.
		/// </summary>
		/// <param name="grid">Grid that will be disrupted</param>
		/// <param name="duration">Duration of disruption</param>
		/// <param name="strength">Strength of disruption (in hackyness)</param>
		/// <param name="effectOwner">The owner of the disruption.</param>
		public void Start(IMyCubeGrid grid, TimeSpan duration, ref int strength, long effectOwner)
		{
			m_logger = new Logger(GetType().Name, () => grid.DisplayName);

			if (strength < MinCost)
			{
				m_logger.debugLog("strength: " + strength + ", below minimum: " + MinCost, "AddEffect()");
				return;
			}

			CubeGridCache cache = CubeGridCache.GetFor(grid);
			int applied = 0;
			if (!EffectOwnerCanAccess)
				effectOwner = long.MinValue;
			foreach (MyObjectBuilderType type in BlocksAffected)
			{
				var blockGroup = cache.GetBlocksOfType(type);
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
				m_expire = Globals.ElapsedTime.Add(duration);

				UpdateManager.Register(UpdateFrequency, UpdateEffect, grid);
			}
		}

		/// <summary>
		/// Checks for effects ending and removes them.
		/// </summary>
		protected void UpdateEffect()
		{
			if (Globals.ElapsedTime > m_expire)
			{
				m_logger.debugLog("Removing the effect", "UpdateEffect()", Logger.severity.DEBUG);
				UpdateManager.Unregister(UpdateFrequency, UpdateEffect);
				RemoveEffect();
			}
		}

		/// <summary>
		/// Removes an effect that has expired.
		/// </summary>
		private void RemoveEffect()
		{
			foreach (var pair in m_affected)
			{
				IMyCubeBlock block = pair.Key;
				EndEffect(block);
				
				// sound files are not linked properly
				try { block.SetDamageEffect(false); }
				catch (NullReferenceException nre)
				{ m_logger.alwaysLog("Exception on disabling damage effect:\n" + nre, "RemoveEffect()", Logger.severity.ERROR); }

				MyCubeBlock cubeBlock = block as MyCubeBlock;
				cubeBlock.ChangeOwner(pair.Value.Owner, pair.Value.ShareMode);
				m_allAffected.Remove(block);
			}

			m_affected = null;
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
		/// Additional condition that is checked before performing a disruption on a block.
		/// Disruption class already checks that the block is working, is not disrupted, and has a low enough cost.
		/// </summary>
		/// <param name="block">Block that may be disrupted</param>
		/// <returns>True iff the disruption can be started on the block.</returns>
		protected virtual bool CanDisrupt(IMyCubeBlock block)
		{
			return true;
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
