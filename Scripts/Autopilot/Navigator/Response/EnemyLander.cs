using System.Text;
using Rynchodon.Autopilot.Data;
using Rynchodon.Autopilot.Pathfinding;
using SpaceEngineers.Game.ModAPI;
using VRage.Game.ModAPI;

namespace Rynchodon.Autopilot.Navigator.Response
{
	public class EnemyLander : IEnemyResponse
	{
		private readonly Pathfinder m_pathfinder;
		private readonly FlyToGrid m_flyToGrid;
		private readonly bool m_hasLandingGear;

		private AllNavigationSettings m_navSet { get { return m_pathfinder.NavSet; } }

		public EnemyLander(Pathfinder pathfinder, PseudoBlock landingGear)
		{
			this.m_pathfinder = pathfinder;

			if (landingGear == null)
			{
				Logger.DebugLog("landingGear param is null, not going to land");
				return;
			}
			this.m_hasLandingGear = landingGear.Block is IMyLandingGear;
			if (!this.m_hasLandingGear)
			{
				Logger.DebugLog("landingGear param is not landing gear: " + landingGear.Block.getBestName() + ", not going to land");
				return;
			}
			this.m_flyToGrid = new FlyToGrid(pathfinder, finder: m_navSet.Settings_Current.EnemyFinder, landingBlock: landingGear);
		}

		public bool CanRespond()
		{
			return m_hasLandingGear && m_pathfinder.Mover.Thrust.CanMoveAnyDirection();
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
