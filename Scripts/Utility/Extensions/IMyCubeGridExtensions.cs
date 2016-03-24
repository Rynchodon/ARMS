using VRage.Game; // from VRage.Game.dll
using VRage.Game.ModAPI; // from VRage.Math.dll

namespace Rynchodon
{
	public static class IMyCubeGridExtensions
	{

		/// <summary>
		/// Gets the simple name for a grid, "Station", "Large Ship", or "Small Ship".
		/// </summary>
		public static string SimpleName(this IMyCubeGrid grid)
		{
			if (grid.GridSizeEnum == MyCubeSize.Large)
				if (grid.IsStatic)
					return "Station";
				else
					return "Large Ship";
			else
				return "Small Ship";
		}

	}
}
