using System;
using System.Collections.Generic;
using System.Linq;
using System.Xml.Serialization;
using Rynchodon.Update;
using Sandbox.Game.Entities;
using Sandbox.ModAPI;
using VRage;
using VRage.Game;
using VRage.Game.ModAPI;
using VRage.ModAPI;
using VRage.ObjectBuilders;

namespace Rynchodon.Weapons.SystemDisruption
{
	public abstract class Disruption
	{

		[Serializable]
		public class Builder_Disruption
		{
			[XmlAttribute]
			public string Type;
			[XmlAttribute]
			public long EffectOwner;
			public SerializableGameTime Expires;
			public long[] Affected_Blocks;
			public MyIDModule[] Affected_Owner;
		}

		private const uint UpdateFrequency = 10u;

		public static HashSet<Disruption> AllDisruptions = new HashSet<Disruption>();
		private static HashSet<IMyCubeBlock> m_allAffected = new HashSet<IMyCubeBlock>();

		static Disruption()
		{
			MyAPIGateway.Entities.OnCloseAll += Entities_OnCloseAll;
		}

		static void Entities_OnCloseAll()
		{
			MyAPIGateway.Entities.OnCloseAll -= Entities_OnCloseAll;
			AllDisruptions = null;
			m_allAffected = null;
		}

		protected Logger m_logger { get; private set; }

		private long m_effectOwner;

		private TimeSpan m_expire;
		private Dictionary<IMyCubeBlock, MyIDModule> m_affected = new Dictionary<IMyCubeBlock, MyIDModule>();

		/// <summary>When strength is less than this value, stop trying to start an effect.</summary>
		protected virtual float MinCost { get { return 1f; } }

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
		public void Start(IMyCubeGrid grid, TimeSpan duration, ref float strength, long effectOwner)
		{
			this.m_logger = new Logger(() => grid.DisplayName);

			if (strength < MinCost)
			{
				m_logger.debugLog("strength: " + strength + ", below minimum: " + MinCost);
				return;
			}

			CubeGridCache cache = CubeGridCache.GetFor(grid);
			float applied = 0;
			if (!EffectOwnerCanAccess)
				effectOwner = long.MinValue;
			m_effectOwner = effectOwner;
			foreach (MyObjectBuilderType type in BlocksAffected)
			{
				var blockGroup = cache.GetBlocksOfType(type);
				if (blockGroup != null && blockGroup.Count != 0)
				{
					foreach (IMyCubeBlock block in blockGroup.OrderBy(OrderBy))
					{
						if (!block.IsWorking || m_allAffected.Contains(block))
						{
							m_logger.debugLog("cannot disrupt: " + block);
							continue;
						}
						float cost = BlockCost(block);
						if (cost > strength)
						{
							m_logger.debugLog("cannot disrupt block: " + block + ", cost: " + cost + " is greater than strength available: " + strength);
							continue;
						}

						StartEffect(block);
						m_logger.debugLog("disrupting: " + block + ", cost: " + cost + ", remaining strength: " + strength);
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
					m_logger.debugLog("no blocks of type: " + type);
			}
FinishedBlocks:
			if (m_affected.Count != 0)
			{
				m_logger.debugLog("Added new effect, strength: " + applied);
				m_expire = Globals.ElapsedTime.Add(duration);

				UpdateManager.Register(UpdateFrequency, UpdateEffect); // don't unregister on grid close, blocks can still be valid
				AllDisruptions.Add(this);
			}
		}

		public void Start(Builder_Disruption builder)
		{
			this.m_logger = new Logger();
			this.m_effectOwner = builder.EffectOwner;

			for (int index = 0; index < builder.Affected_Blocks.Length; index++)
			{
				IMyEntity entity;
				if (!MyAPIGateway.Entities.TryGetEntityById(builder.Affected_Blocks[index], out entity) || !(entity is IMyCubeBlock))
				{
					m_logger.debugLog("Block is not in world: " + builder.Affected_Blocks[index], Logger.severity.WARNING);
					continue;
				}
				IMyCubeBlock block  = (IMyCubeBlock)entity;
				StartEffect(block);
				// save/load will change ownership from long.MinValue to 0L
				((MyCubeBlock)block).ChangeOwner(m_effectOwner, MyOwnershipShareModeEnum.Faction);
				m_affected.Add(block, builder.Affected_Owner[index]);

				block.SetDamageEffect(true);
			}

			if (m_affected.Count != 0)
			{
				m_logger.debugLog("Added old effect from builder");
				m_expire = builder.Expires.ToTimeSpan();
				UpdateManager.Register(UpdateFrequency, UpdateEffect);
				AllDisruptions.Add(this);
			}
		}

		/// <summary>
		/// Checks for effects ending and removes them.
		/// </summary>
		protected void UpdateEffect()
		{
			if (Globals.ElapsedTime > m_expire)
			{
				m_logger.debugLog("Removing the effect", Logger.severity.DEBUG);
				UpdateManager.Unregister(UpdateFrequency, UpdateEffect);
				AllDisruptions.Remove(this);
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
				{ m_logger.alwaysLog("Exception on disabling damage effect:\n" + nre, Logger.severity.ERROR); }

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
		/// <returns>An float that reflects the order blocks are affected in.</returns>
		protected virtual float OrderBy(IMyCubeBlock block)
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
		protected virtual float BlockCost(IMyCubeBlock block)
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

		public Builder_Disruption GetBuilder()
		{
			Builder_Disruption result = new Builder_Disruption()
			{
				Type = GetType().Name,
				EffectOwner = m_effectOwner,
				Expires = new SerializableGameTime(m_expire)
			};

			result.Affected_Blocks = new long[m_affected.Count];
			result.Affected_Owner = new MyIDModule[m_affected.Count];

			int index = 0;
			foreach (var pair in m_affected)
			{
				result.Affected_Blocks[index] = pair.Key.EntityId;
				result.Affected_Owner[index++] = pair.Value;
			}

			return result;
		}

	}
}
