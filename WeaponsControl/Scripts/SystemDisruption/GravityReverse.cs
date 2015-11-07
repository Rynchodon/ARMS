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

		public static int ReverseGravity(IMyCubeGrid grid, int strength, TimeSpan duration, long effectOwner)
		{
			GravityReverse gg;
			if (!Registrar.TryGetValue(grid, out gg))
				gg = new GravityReverse(grid);
			return gg.AddEffect(duration, strength, effectOwner);
		}

		private readonly Logger m_logger;

		private GravityReverse(IMyCubeGrid grid)
			: base(grid, s_affects)
		{
			m_logger = new Logger(GetType().Name, () => grid.DisplayName);
			Registrar.Add(grid, this);
		}

		protected override int StartEffect(IMyCubeBlock block, int strength)
		{
			return ReverseGravity(block, strength);
		}

		protected override int EndEffect(IMyCubeBlock block, int strength)
		{
			return ReverseGravity(block, strength);
		}

		private int ReverseGravity(IMyCubeBlock block, int strength)
		{
			m_logger.debugLog("Reversing gravity of " + block.DisplayNameText + ", remaining strength: " + (strength - 1), "ReverseGravity()");
			Ingame.IMyGravityGenerator rect = block as Ingame.IMyGravityGenerator;
			if (rect != null)
			{
				rect.GetProperty("Gravity").AsFloat().SetValue(rect, -rect.Gravity * Gee);
				return 1;
			}
			Ingame.IMyGravityGeneratorSphere sphere = block as Ingame.IMyGravityGeneratorSphere;
			if (sphere != null)
			{
				sphere.GetProperty("Gravity").AsFloat().SetValue(sphere, -sphere.Gravity * Gee);
				return 1;
			}

			m_logger.alwaysLog("Exotic gravity generator: " + block.DefinitionDisplayNameText + "/" + block.DisplayNameText, "ReverseGravity()", Logger.severity.WARNING);
			return 0;
		}

	}
}
