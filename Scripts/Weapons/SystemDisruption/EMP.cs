using System;
using System.Collections.Generic;
using Sandbox.Common.ObjectBuilders;
using Sandbox.Definitions;
using Sandbox.Game.Entities;
using Sandbox.Game.Entities.Cube;
using Sandbox.ModAPI;
using VRage.Game.Entity;
using VRage.Game.ModAPI;
using VRage.ModAPI;
using VRage.ObjectBuilders;
using VRageMath;

namespace Rynchodon.Weapons.SystemDisruption
{
	public class EMP : Disruption
	{

		private static List<MyEntity> s_entitiesEMP = new List<MyEntity>();

		public static void ApplyEMP(BoundingSphereD location, float strength, TimeSpan duration)
		{
			MyAPIGateway.Utilities.TryInvokeOnGameThread(() => {
				MyGamePruningStructure.GetAllTopMostEntitiesInSphere(ref location, s_entitiesEMP);
				foreach (IMyEntity entity in s_entitiesEMP)
				{
					IMyCubeGrid grid = entity as IMyCubeGrid;
					if (grid != null)
					{
						EMP e = new EMP();
						e.Start(grid, duration, ref strength, long.MinValue);
					}
				}
				s_entitiesEMP.Clear();
			});
		}

		protected override float MinCost { get { return 500f; } }

		protected override MyObjectBuilderType[] BlocksAffected
		{
			get { return  new MyObjectBuilderType[] { typeof(MyObjectBuilder_BatteryBlock), typeof(MyObjectBuilder_Reactor) }; }
		}

		protected override float OrderBy(IMyCubeBlock block)
		{
			return -BlockCost(block);
		}

		protected override float BlockCost(IMyCubeBlock block)
		{
			return ((block.GetCubeBlockDefinition() as MyPowerProducerDefinition).MaxPowerOutput * 1000f);
		}

		protected override void StartEffect(IMyCubeBlock block)
		{
			((MyFunctionalBlock)block).Enabled = false;
		}

		protected override void EndEffect(IMyCubeBlock block)
		{
			((MyFunctionalBlock)block).Enabled = true;
		}

	}
}
