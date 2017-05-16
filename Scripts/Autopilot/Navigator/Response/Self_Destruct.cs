using System.Text;
using Rynchodon.Attached;
using Rynchodon.Utility;
using Sandbox.Common.ObjectBuilders;
using VRage.Game.ModAPI;

namespace Rynchodon.Autopilot.Navigator
{
	public class Self_Destruct : IEnemyResponse
	{
		private readonly IMyCubeBlock m_block;
		private bool m_countingDown;

		private Logable Log { get { return new Logable(m_block); } }

		public Self_Destruct(IMyCubeBlock block)
		{
			this.m_block = block;
		}

		public bool CanRespond()
		{
			return !m_countingDown;
		}

		public bool CanTarget(IMyCubeGrid grid)
		{
			return true;
		}

		public void UpdateTarget(AntennaRelay.LastSeen enemy)
		{
			if (enemy == null)
				return;

			foreach (IMyCubeGrid grid in AttachedGrid.AttachedGrids(m_block.CubeGrid, AttachedGrid.AttachmentKind.Terminal, true))
			{
				CubeGridCache cache = CubeGridCache.GetFor(grid);
				if (cache == null)
					continue;
				foreach (IMyCubeBlock warhead in cache.BlocksOfType(typeof(MyObjectBuilder_Warhead)))
					if (m_block.canControlBlock(warhead))
					{
						Log.DebugLog("Starting countdown for " + warhead.getBestName(), Logger.severity.DEBUG);
						warhead.ApplyAction("StartCountdown");
					}
			}
			m_countingDown = true;
		}

		public void Move() { }

		public void AppendCustomInfo(StringBuilder customInfo)
		{
			customInfo.AppendLine("Self destruct sequence");
		}

		public void Rotate() { }

	}
}
