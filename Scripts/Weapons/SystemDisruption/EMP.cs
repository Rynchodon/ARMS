using System;
using System.Collections.Generic;
using Sandbox.Common.ObjectBuilders;
using Sandbox.Definitions;
using Sandbox.Game.Entities;
using Sandbox.ModAPI;
using VRage.Game.Entity;
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

		public static void ApplyEMP(BoundingSphereD location, int strength, TimeSpan duration)
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
						e.AddEffect(duration, strength);
					}
				}
				s_entitiesEMP.Clear();
			}, s_logger);
		}

		private readonly Logger m_logger;

		protected override int MinCost { get { return 1000; } }

		private EMP(IMyCubeGrid grid)
			: base(grid, s_affects)
		{
			m_logger = new Logger(GetType().Name, () => grid.DisplayName);
			Registrar.Add(grid, this);
		}

		protected override int OrderBy(IMyCubeBlock block)
		{
			return -BlockCost(block);
		}

		protected override int BlockCost(IMyCubeBlock block)
		{
			return (int)((block.GetCubeBlockDefinition() as MyPowerProducerDefinition).MaxPowerOutput * 1000f);
		}

		protected override void StartEffect(IMyCubeBlock block)
		{
			(block as IMyFunctionalBlock).RequestEnable(false);
		}

		protected override void EndEffect(IMyCubeBlock block)
		{
			(block as IMyFunctionalBlock).RequestEnable(true);
		}

	}
}
