using System;
using System.Collections.Generic;
using Rynchodon.Attached;
using Sandbox.Common.ObjectBuilders;
using Sandbox.Game.Entities;
using Sandbox.ModAPI;
using VRageMath;

namespace Rynchodon.Autopilot.Data
{
	public class GyroProfiler
	{

		private readonly Logger myLogger;
		private readonly IMyCubeGrid myGrid;

		private readonly List<MyGyro> Gyros = new List<MyGyro>();

		private ulong m_nextCheck_attachedGrids;
		private int m_attachedGrids;
		/// <summary>torqueAccelRatio becomes dirty any time a block is added, removed does not matter.</summary>
		private bool dirty_torqueAccelRatio;

		/// <summary>A measure of the affect torque has on velocity per autopilot update.</summary>
		public float torqueAccelRatio { get; private set; }

		public GyroProfiler(IMyCubeGrid grid)
		{
			this.myLogger = new Logger("GyroProfiler", () => grid.DisplayName);
			this.myGrid = grid;
			this.torqueAccelRatio = 0f;

			ReadOnlyList<IMyCubeBlock> blocks = CubeGridCache.GetFor(grid).GetBlocksOfType(typeof(MyObjectBuilder_Gyro));
			if (blocks != null)
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
					force += g.MaxGyroForce; // MaxGyroForce accounts for power ratio and modules

			return force;
		}

		public void Update_torqueAccelRatio(Vector3 command, Vector3 ratio)
		{
			if (Globals.UpdateCount >= m_nextCheck_attachedGrids)
				CheckAttachedGrids();

			for (int i = 0; i < 3; i++)
			{
				// where ratio is from damping, ignore it
				if (Math.Abs(command.GetDim(i)) < 0.01f)
					continue;

				float dim = ratio.GetDim(i);
				if (dim.IsValid() && (dirty_torqueAccelRatio || dim > torqueAccelRatio))
				{
					if (dim > 0f)
					{
						myLogger.debugLog("torqueAccelRatio changed from " + torqueAccelRatio + " to " + dim, "Update_torqueAccelRatio()", Logger.severity.DEBUG);
						torqueAccelRatio = dim;
					}
					else // caused by bumping into things
						myLogger.debugLog("dim <= 0 : " + dim, "Update_torqueAccelRatio()", Logger.severity.DEBUG);
				}
			}
		}

		private void grid_OnBlockAdded(IMySlimBlock obj)
		{
			dirty_torqueAccelRatio = true;
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

		private void CheckAttachedGrids()
		{
			m_nextCheck_attachedGrids = Globals.UpdateCount + 100ul;

			int count = 0;
			AttachedGrid.RunOnAttached(myGrid, AttachedGrid.AttachmentKind.Physics, grid => {
				count++;
				return false;
			});

			if (count > m_attachedGrids)
				dirty_torqueAccelRatio = true;
			m_attachedGrids = count;
		}

	}
}
