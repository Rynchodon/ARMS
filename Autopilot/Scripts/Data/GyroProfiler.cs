using System.Collections.Generic;

using Sandbox.Common.ObjectBuilders;
using Sandbox.Game.Entities;
using Sandbox.ModAPI;

namespace Rynchodon.Autopilot.Data
{
	public class GyroProfiler
	{

		private readonly Logger myLogger;
		private readonly IMyCubeGrid myGrid;

		private readonly List<MyGyro> Gyros = new List<MyGyro>();

		public GyroProfiler(IMyCubeGrid grid)
		{
			this.myLogger = new Logger("GyroProfiler", () => grid.DisplayName);
			this.myGrid = grid;

			ReadOnlyList<IMyCubeBlock> blocks = CubeGridCache.GetFor(grid).GetBlocksOfType(typeof(MyObjectBuilder_Gyro));
			foreach (MyGyro g in blocks)
				Gyros.Add(g);

			grid.OnBlockAdded += grid_OnBlockAdded;
			grid.OnBlockRemoved += grid_OnBlockRemoved;
		}

		public float TotalGyroForce()
		{
			float force = 0f;
			foreach (MyGyro g in Gyros)
				if (g.IsWorking)
					force += g.MaxGyroForce;

			return force;
		}

		private void grid_OnBlockAdded(IMySlimBlock obj)
		{
			MyGyro g = obj.FatBlock as MyGyro;
			if (g == null)
				return;

			Gyros.Add(g);
		}

		private void grid_OnBlockRemoved(IMySlimBlock obj)
		{
			MyGyro g = obj.FatBlock as MyGyro;
			if (g == null)
				return;

			Gyros.Remove(g);
		}

	}
}
