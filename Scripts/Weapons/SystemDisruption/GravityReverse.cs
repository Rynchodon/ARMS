
using Sandbox.Common.ObjectBuilders;
using Sandbox.ModAPI.Interfaces;
using SpaceEngineers.Game.ModAPI;
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
			IMyGravityGeneratorBase gravityBase = block as IMyGravityGeneratorBase;
			if (gravityBase != null)
			{
				gravityBase.GravityAcceleration = -gravityBase.GravityAcceleration;
				return;
			}

			Logger.AlwaysLog("Exotic gravity generator: " + block.DefinitionDisplayNameText + "/" + block.DisplayNameText, Rynchodon.Logger.severity.WARNING);
		}

	}
}
