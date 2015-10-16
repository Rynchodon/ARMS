using System.Collections.Generic;
using Sandbox.Definitions;
using Sandbox.ModAPI;
using Ingame = Sandbox.ModAPI.Ingame;

namespace Rynchodon
{
	public static class DefinitionCache
	{
		private static Dictionary<string, MyCubeBlockDefinition> KnownDefinitions_CubeBlock = new Dictionary<string, MyCubeBlockDefinition>();

		public static MyCubeBlockDefinition GetCubeBlockDefinition(Ingame.IMyCubeBlock block)
		{ return GetCubeBlockDefinition(block as IMyCubeBlock); }

		public static MyCubeBlockDefinition GetCubeBlockDefinition(IMyCubeBlock block)
		{
			MyCubeBlockDefinition result;
			string ID = block.BlockDefinition.ToString();

			if (!KnownDefinitions_CubeBlock.TryGetValue(ID, out result))
			{
				result = MyDefinitionManager.Static.GetCubeBlockDefinition(block.GetObjectBuilder_Safe());
				KnownDefinitions_CubeBlock.Add(ID, result);
			}

			return result;
		}
	}
}
