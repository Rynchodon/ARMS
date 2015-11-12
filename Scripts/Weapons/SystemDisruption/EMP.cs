using System;
using System.Collections.Generic;
using Sandbox.Common.ObjectBuilders;
using Sandbox.Definitions;
using Sandbox.Game.Entities;
using Sandbox.ModAPI;
using VRage.ModAPI;
using VRage.ObjectBuilders;
using VRageMath;

namespace Rynchodon.Weapons.SystemDisruption
{
	public class EMP : Disruption
	{

		private static readonly Logger s_logger = new Logger("EMP");
		private static readonly MyObjectBuilderType[] s_affects = new MyObjectBuilderType[] { typeof(MyObjectBuilder_BatteryBlock), typeof(MyObjectBuilder_Reactor) };
		private static List<MyEntity> s_entitiesEMP = new List<MyEntity>();

		public static void Update()
		{
			Registrar.ForEach((EMP e) => e.UpdateEffect());
		}

		public static void ApplyEMP(BoundingSphereD location, int strength, TimeSpan duration, long effectOwner)
		{
			MyAPIGateway.Utilities.TryInvokeOnGameThread(() => {
				MyGamePruningStructure.GetAllTopMostEntitiesInSphere(ref location, s_entitiesEMP);
				foreach (IMyEntity entity in s_entitiesEMP)
				{
					IMyCubeGrid grid = entity as IMyCubeGrid;
					if (grid != null)
					{
						EMP e;
						if (!Registrar.TryGetValue(grid, out e))
							e = new EMP(grid);
						e.AddEffect(duration, strength, effectOwner);
					}
				}
				s_entitiesEMP.Clear();
			}, s_logger);
		}

		private readonly Logger m_logger;

		private EMP(IMyCubeGrid grid)
			: base(grid, s_affects)
		{
			m_logger = new Logger(GetType().Name, () => grid.DisplayName);
			Registrar.Add(grid, this);
		}

		protected override int StartEffect(IMyCubeBlock block, int strength)
		{
			int maxPower = MaxPowerOutput(block);
			if (strength >= maxPower)
			{
				m_logger.debugLog("EMP disabled: " + block.DisplayNameText + ", remaining strength: " + (strength - maxPower), "StartEffect()");
				(block as IMyFunctionalBlock).RequestEnable(false);
				return maxPower;
			}
			else
				m_logger.debugLog("EMP not strong enough to disable: " + block.DisplayNameText + ", strength: " + strength + ", required: " + maxPower, "StartEffect()");
			return 0;
		}

		protected override int EndEffect(IMyCubeBlock block, int strength)
		{
			int maxPower = MaxPowerOutput(block);
			if (strength >= maxPower)
			{
				m_logger.debugLog("EMP expired on " + block.DisplayNameText + ", remaining strength: " + (strength - maxPower), "EndEffect()");
				(block as IMyFunctionalBlock).RequestEnable(true);
				return maxPower;
			}
			return 0;
		}

		private int MaxPowerOutput(IMyCubeBlock block)
		{
			var definition = block.GetCubeBlockDefinition() as MyPowerProducerDefinition;
			return (int)(definition.MaxPowerOutput * 1000f);
		}

	}
}
