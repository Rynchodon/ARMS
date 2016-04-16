using System.Text;
using Rynchodon.Autopilot.Data;
using Rynchodon.Autopilot.Movement;
using SpaceEngineers.Game.ModAPI;

using VRage.Game.ModAPI;

namespace Rynchodon.Autopilot.Navigator.Response
{
	public class EnemyLander : IEnemyResponse
	{

		private readonly Logger m_logger;
		private readonly AllNavigationSettings m_navSet;
		private readonly FlyToGrid m_flyToGrid;
		private readonly bool m_hasLandingGear;

		public EnemyLander(Mover mover, AllNavigationSettings navSet, PseudoBlock landingGear)
		{
			this.m_logger = new Logger(GetType().Name);
			this.m_navSet = navSet;

			if (landingGear == null)
			{
				m_logger.debugLog("landingGear param is null, not going to land", "EnemyLander()");
				return;
			}
			this.m_hasLandingGear = landingGear.Block is IMyLandingGear;
			if (!this.m_hasLandingGear)
			{
				m_logger.debugLog("landingGear param is not landing geat: " + landingGear.Block.getBestName() + ", not going to land", "EnemyLander()");
				return;
			}
			this.m_flyToGrid = new FlyToGrid(mover, navSet, finder: m_navSet.Settings_Current.EnemyFinder, landingBlock: landingGear);
		}

		public bool CanRespond()
		{
			return m_hasLandingGear;
		}

		public bool CanTarget(IMyCubeGrid grid)
		{
			return true;
		}

		public void UpdateTarget(AntennaRelay.LastSeen enemy) { }

		public void Move()
		{
			m_flyToGrid.Move();
		}

		public void Rotate()
		{
			m_flyToGrid.Rotate();
		}

		public void AppendCustomInfo(StringBuilder customInfo)
		{
			customInfo.Append("Flying to ");
			customInfo.AppendLine(m_navSet.Settings_Current.EnemyFinder.Grid.HostileName());
			customInfo.Append("Landing: ");
			customInfo.AppendLine(m_flyToGrid.m_landingState.ToString());
		}

	}
}
