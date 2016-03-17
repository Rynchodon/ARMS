
using Sandbox.Common.ObjectBuilders;
using Sandbox.ModAPI.Interfaces;
using VRage.Game.ModAPI;
using VRage.ObjectBuilders;
using Ingame = SpaceEngineers.Game.ModAPI.Ingame;

namespace Rynchodon.Weapons.SystemDisruption
{
	public class GravityReverse : Disruption
	{

		private const float Gee = 9.81f;

		protected override MyObjectBuilderType[] BlocksAffected
		{
			get { return new MyObjectBuilderType[] { typeof(MyObjectBuilder_GravityGenerator), typeof(MyObjectBuilder_GravityGeneratorSphere) }; }
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
				return;
			}
			Ingame.IMyGravityGeneratorSphere sphere = block as Ingame.IMyGravityGeneratorSphere;
			if (sphere != null)
			{
				sphere.GetProperty("Gravity").AsFloat().SetValue(sphere, -sphere.Gravity * Gee);
				return;
			}

			m_logger.alwaysLog("Exotic gravity generator: " + block.DefinitionDisplayNameText + "/" + block.DisplayNameText, "ReverseGravity()", Logger.severity.WARNING);
		}

	}
}
