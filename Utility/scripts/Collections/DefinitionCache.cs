using System.Collections.Generic;
using Sandbox.Definitions;
using Sandbox.ModAPI;
using VRage.ObjectBuilders;
using Ingame = Sandbox.ModAPI.Ingame;

namespace Rynchodon
{
	public static class DefinitionCache
	{
		private static Dictionary<SerializableDefinitionId, MyCubeBlockDefinition> KnownDefinitions_CubeBlock = new Dictionary<SerializableDefinitionId, MyCubeBlockDefinition>();

		static DefinitionCache()
		{
			MyAPIGateway.Entities.OnCloseAll += Entities_OnCloseAll;
		}

		private static void Entities_OnCloseAll()
		{
			MyAPIGateway.Entities.OnCloseAll -= Entities_OnCloseAll;
			KnownDefinitions_CubeBlock = null;
		}

		public static MyCubeBlockDefinition GetCubeBlockDefinition(Ingame.IMyCubeBlock block)
		{ return GetCubeBlockDefinition(block as IMyCubeBlock); }

		public static MyCubeBlockDefinition GetCubeBlockDefinition(IMyCubeBlock block)
		{
			MyCubeBlockDefinition result;
			SerializableDefinitionId ID = block.BlockDefinition;

			if (!KnownDefinitions_CubeBlock.TryGetValue(ID, out result))
			{
				result = MyDefinitionManager.Static.GetCubeBlockDefinition(block.GetObjectBuilder_Safe());
				KnownDefinitions_CubeBlock.Add(ID, result);
			}

			return result;
		}
	}
}
