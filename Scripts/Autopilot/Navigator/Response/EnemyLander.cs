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
		private readonly Mover m_mover;
		private readonly FlyToGrid m_flyToGrid;
		private readonly bool m_hasLandingGear;

		private AllNavigationSettings m_navSet { get { return m_mover.NavSet; } }

		public EnemyLander(Mover mover, PseudoBlock landingGear)
		{
			this.m_logger = new Logger();
			this.m_mover = mover;

			if (landingGear == null)
			{
				m_logger.debugLog("landingGear param is null, not going to land");
				return;
			}
			this.m_hasLandingGear = landingGear.Block is IMyLandingGear;
			if (!this.m_hasLandingGear)
			{
				m_logger.debugLog("landingGear param is not landing gear: " + landingGear.Block.getBestName() + ", not going to land");
				return;
			}
			this.m_flyToGrid = new FlyToGrid(mover, finder: m_navSet.Settings_Current.EnemyFinder, landingBlock: landingGear);
		}

		public bool CanRespond()
		{
			return m_hasLandingGear && m_mover.Thrust.CanMoveAnyDirection();
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
