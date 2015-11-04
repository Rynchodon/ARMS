using System;
using System.Collections.Generic;

using Sandbox.Common.ObjectBuilders;
using Sandbox.Definitions;
using Sandbox.Game.Entities;
using Sandbox.ModAPI;
using VRage.Collections;
using VRage.ModAPI;
using VRage.ObjectBuilders;
using VRageMath;

namespace Rynchodon.Weapons
{
	public class EMP_Disruption
	{

		private static List<MyEntity> s_entitiesEMP = new List<MyEntity>();

		public static void Update1()
		{
			Registrar.ForEach((EMP_Disruption disrupt) => disrupt.Update());
		}

		public static void ApplyEMP(ref BoundingSphereD location, float strength, TimeSpan duration)
		{
			MyGamePruningStructure.GetAllTopMostEntitiesInSphere(ref location, s_entitiesEMP);
			foreach (IMyEntity entity in s_entitiesEMP)
			{
				IMyCubeGrid grid = entity as IMyCubeGrid;
				if (grid != null)
					GetFor(grid).AddEMP(duration, ref strength);
			}
			s_entitiesEMP.Clear();
		}

		private static EMP_Disruption GetFor(IMyCubeGrid grid)
		{
			EMP_Disruption value;
			if (!Registrar.TryGetValue(grid, out value))
				value = new EMP_Disruption(grid);
			return value;
		}

		private readonly Logger m_logger;
		private readonly IMyCubeGrid m_grid;
		private readonly CubeGridCache m_gridCache;

		private readonly SortedDictionary<DateTime, float> m_EMP_effects = new SortedDictionary<DateTime, float>();
		private readonly CachingList<IMyFunctionalBlock> m_EMP_disabled = new CachingList<IMyFunctionalBlock>();

		private DateTime m_nextEffect;
		private float m_EMP_toRemove;

		private EMP_Disruption(IMyCubeGrid grid)
		{
			m_logger = new Logger(GetType().Name, () => grid.DisplayName);
			m_grid = grid;
			m_gridCache = CubeGridCache.GetFor(grid);
			Registrar.Add(grid, this);
		}

		public void AddEMP(TimeSpan duration, ref float empStrength)
		{
			float emp_applied = 0L;
			AddEMP(ref empStrength, ref emp_applied, typeof(MyObjectBuilder_Reactor));
			AddEMP(ref empStrength, ref emp_applied, typeof(MyObjectBuilder_BatteryBlock));
			if (emp_applied > 0L)
			{
				m_logger.debugLog("Total EMP applied: " + emp_applied, "AddEMP()", Logger.severity.INFO);
				m_EMP_effects.Add(DateTime.UtcNow.Add(duration), emp_applied);
				m_EMP_disabled.ApplyAdditions();
				SetNextEffect();
			}
		}

		private void Update()
		{
			foreach (IMyFunctionalBlock block in m_EMP_disabled)
				if (block.Enabled)
					block.RequestEnable(false);

			if (m_EMP_effects.Count != 0 && m_nextEffect < DateTime.UtcNow)
			{
				if (m_EMP_effects.Count == 1)
				{
					m_logger.debugLog("the last EMP effect has expired", "Update()", Logger.severity.INFO);
					m_EMP_toRemove = float.MaxValue;
					RemoveEMP();
					m_EMP_effects.Remove(m_nextEffect);
					m_EMP_toRemove = 0f;
				}
				else
				{
					m_logger.debugLog("an EMP effect has expired, strength: " + m_EMP_effects[m_nextEffect], "Update()", Logger.severity.INFO);
					m_EMP_toRemove += m_EMP_effects[m_nextEffect] + 0.001f;
					RemoveEMP();
					m_EMP_effects.Remove(m_nextEffect);
					SetNextEffect();
				}
			}
		}

		private void AddEMP(ref float empStrength, ref float emp_applied, MyObjectBuilderType type)
		{
			var blockList = m_gridCache.GetBlocksOfType(type);
			if (blockList != null)
				foreach (IMyFunctionalBlock block in blockList)
					if (block.IsWorking)
					{
						var definition = DefinitionCache.GetCubeBlockDefinition(block) as MyPowerProducerDefinition;
						m_logger.debugLog("comparing strength: " + empStrength + ", to power: " + definition.MaxPowerOutput, "AddEMP()");
						if (empStrength >= definition.MaxPowerOutput)
						{
							m_EMP_disabled.Add(block);
							empStrength -= definition.MaxPowerOutput;
							emp_applied += definition.MaxPowerOutput;
							m_logger.debugLog("block is now disabled: " + block.DisplayNameText + ", remaining emp: " + empStrength, "AddEMP()");
							block.RequestEnable(false);
							(block as IMyCubeBlock).SetDamageEffect(true);
						}
					}
		}

		private void RemoveEMP()
		{
			foreach (IMyFunctionalBlock block in m_EMP_disabled)
			{
				var definition = DefinitionCache.GetCubeBlockDefinition(block) as MyPowerProducerDefinition;
				if (m_EMP_toRemove >= definition.MaxPowerOutput)
				{
					m_EMP_disabled.Remove(block);
					m_EMP_toRemove -= definition.MaxPowerOutput;
					m_logger.debugLog("block is now enabled: " + block.DisplayNameText + ", remaining emp: " + m_EMP_toRemove, "RemoveEMP()");
					block.RequestEnable(true);
					(block as IMyCubeBlock).SetDamageEffect(false);
				}
			}
			m_EMP_disabled.ApplyRemovals();
		}

		private void SetNextEffect()
		{
			if (m_EMP_effects.Count == 0)
			{
				m_logger.debugLog("no effects remain", "SetNextEffect()");
				return;
			}

			var effectsEnumer = m_EMP_effects.GetEnumerator();
			effectsEnumer.MoveNext();
			m_nextEffect = effectsEnumer.Current.Key;
			m_logger.debugLog("Next effect will expire at " + m_nextEffect, "SetNextEffect()", Logger.severity.DEBUG);
		}

	}
}
