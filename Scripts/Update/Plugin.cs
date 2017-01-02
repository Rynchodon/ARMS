using System.Reflection;
using Sandbox.Common.ObjectBuilders;
using Sandbox.Definitions;
using Sandbox.Game.World;
using VRage.ObjectBuilders;
using VRage.Plugins;

namespace Rynchodon.Update
{
	public class Plugin : IPlugin
	{
		public void Dispose() { }

		public void Init(object gameInstance)
		{
			MySession.OnLoading += CheckForArmsAndRegister;
		}

		public void Update() { }

		private static void CheckForArmsAndRegister()
		{
			if (MyDefinitionManager.Static.GetCubeBlockDefinition(new SerializableDefinitionId(typeof(MyObjectBuilder_Cockpit), "Autopilot-Block_Large")) == null)
				return;

			MySession.Static.RegisterComponentsFromAssembly(Assembly.GetExecutingAssembly(), true);
		}
	}
}
