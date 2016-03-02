using System;

using Sandbox.Common.ObjectBuilders;
using Sandbox.ModAPI;
using Sandbox.ModAPI.Interfaces;
using VRage.ObjectBuilders;
using Ingame = Sandbox.ModAPI.Ingame;

namespace Rynchodon.Weapons.SystemDisruption
{
	public class GravityReverse : Disruption
	{

		private const float Gee = 9.81f;

		private static readonly MyObjectBuilderType[] s_affects = new MyObjectBuilderType[] { typeof(MyObjectBuilder_GravityGenerator), typeof(MyObjectBuilder_GravityGeneratorSphere) };

		public static void Update()
		{
			Registrar.ForEach((GravityReverse gg) => gg.UpdateEffect());
		}

		public static int ReverseGravity(IMyCubeGrid grid, int strength, TimeSpan duration)
		{
			GravityReverse gg;
			if (!Registrar.TryGetValue(grid, out gg))
				gg = new GravityReverse(grid);
			return gg.AddEffect(duration, strength);
		}

		private readonly Logger m_logger;

		private GravityReverse(IMyCubeGrid grid)
			: base(grid, s_affects)
		{
			m_logger = new Logger(GetType().Name, () => grid.DisplayName);
			Registrar.Add(grid, this);
		}

		protected override void StartEffect(IMyCubeBlock block)
		{
			ReverseGravity(block);
		}

		protected override void EndEffect(IMyCubeBlock block)
		{
			ReverseGravity(block);
		}

		private void ReverseGravity(IMyCubeBlock block)
		{
			Ingame.IMyGravityGenerator rect = block as Ingame.IMyGravityGenerator;
			if (rect != null)
			{
				rect.GetProperty("Gravity").AsFloat().SetValue(rect, -rect.Gravity * Gee);
			}
			Ingame.IMyGravityGeneratorSphere sphere = block as Ingame.IMyGravityGeneratorSphere;
			if (sphere != null)
			{
				sphere.GetProperty("Gravity").AsFloat().SetValue(sphere, -sphere.Gravity * Gee);
			}

			m_logger.alwaysLog("Exotic gravity generator: " + block.DefinitionDisplayNameText + "/" + block.DisplayNameText, "ReverseGravity()", Logger.severity.WARNING);
		}

	}
}
