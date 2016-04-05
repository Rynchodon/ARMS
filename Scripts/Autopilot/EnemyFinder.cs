using System.Collections.Generic;
using Rynchodon.AntennaRelay;
using Rynchodon.Autopilot.Data;
using Rynchodon.Autopilot.Movement;
using Rynchodon.Autopilot.Navigator;
using VRage.ModAPI;
using VRageMath;

namespace Rynchodon.Autopilot
{

	public class EnemyFinder : GridFinder
	{

		public enum Response : byte { None, Fight, Flee, Ram, Self_Destruct }

		private struct ResponseRange
		{
			public readonly Response Response;
			public float SearchRange;

			public ResponseRange(Response resp, float searchRange)
			{
				this.Response = resp;
				this.SearchRange = searchRange;
			}
		}

		private readonly Logger m_logger;
		private readonly Mover m_mover;
		private readonly AllNavigationSettings m_navSet;
		private readonly List<ResponseRange> m_allResponses = new List<ResponseRange>();

		private IEnemyResponse m_navResponse;
		private ResponseRange m_curResponse;
		private int m_responseIndex;

		private Vector3D m_originalPosition = Vector3D.PositiveInfinity;
		private IMyEntity m_originalDestEntity;
		private bool m_engageSet;

		private LastSeen value_grid;

		public override LastSeen Grid
		{
			get { return value_grid; }
			protected set
			{
				if (value == value_grid)
					return;

				if (value != null && value_grid != null && value.Entity == value_grid.Entity)
				{
					value_grid = value;
					return;
				}

				value_grid = value;
				if (value_grid == null)
				{
					m_logger.debugLog("no longer have an Enemy", "set_Enemy()", Logger.severity.DEBUG);
					EndEngage();
				}
				else
				{
					m_logger.debugLog("Enemy is now: " + value_grid.Entity.getBestName(), "set_Enemy()", Logger.severity.DEBUG);
					SetEngage();
				}
			}
		}

		private ResponseRange CurrentResponse
		{
			get { return m_curResponse; }
			set
			{
				m_logger.debugLog("Changing response from " + m_curResponse.Response + " to " + value.Response, "set_CurrentResponse()", Logger.severity.DEBUG);
				m_navResponse = null;
				m_curResponse = value;
				switch (m_curResponse.Response)
				{
					case Response.None:
						m_logger.debugLog("No responses left", "set_Enemy()", Logger.severity.DEBUG);
						EndEngage();
						return;
					case Response.Fight:
						m_navResponse = new Fighter(m_mover, m_navSet);
						break;
					case Response.Flee:
						m_navResponse = new Coward(m_mover, m_navSet);
						break;
					case Response.Ram:
						m_navResponse = new Kamikaze(m_mover, m_navSet);
						break;
					case Response.Self_Destruct:
						m_navResponse = new Self_Destruct(m_mover.Block.CubeBlock);
						break;
					default:
						m_logger.alwaysLog("Response not implemented: " + m_curResponse.Response, "set_CurrentResponse()", Logger.severity.WARNING);
						NextResponse();
						return;
				}
				MaximumRange = m_curResponse.SearchRange;
				GridCondition = m_navResponse.CanTarget;
				if (Grid != null)
				{
					m_logger.debugLog("adding responder: " + m_navResponse, "set_CurrentResponse()", Logger.severity.DEBUG);
					SetEngage();
				}
			}
		}

		public EnemyFinder(Mover mover, AllNavigationSettings navSet, long entityId)
			: base(navSet, mover.Block)
		{
			this.m_logger = new Logger(GetType().Name, mover.Block.CubeBlock, () => CurrentResponse.Response.ToString());
			this.m_mover = mover;
			this.m_navSet = navSet;
			this.m_targetEntityId = entityId;

			m_logger.debugLog("Initialized", "EnemyFinder()");
		}

		public void AddResponses(float range, List<Response> responses)
		{
			foreach (Response r in responses)
			{
				bool found = false;
				for (int i = 0; i < m_allResponses.Count; i++)
				{
					ResponseRange rr = m_allResponses[i];
					if (r == rr.Response)
					{
						m_logger.debugLog("ammending old response: " + r + ", range: " + range, "AddResponses()");
						found = true;
						rr.SearchRange = range;
					}
				}
				if (!found)
				{
					m_logger.debugLog("adding new response: " + r + ", range: " + range, "AddResponses()");
					m_allResponses.Add(new ResponseRange(r, range));
				}
			}
			if (CurrentResponse.Response == Response.None)
				CurrentResponse = m_allResponses[0];
		}

		public void Update()
		{
			if (CurrentResponse.Response == Response.None)
				return;

			if (!m_navResponse.CanRespond())
			{
				m_logger.debugLog("cannot respond", "Update()");
				NextResponse();
				return;
			}

			if (m_originalPosition.IsValid())
			{
				Vector3D originalPosition;
				if (m_originalDestEntity != null)
					originalPosition = m_originalDestEntity.GetPosition() + m_originalPosition;
				else
					originalPosition = m_originalPosition;
				float distance = (float)Vector3D.Distance(m_mover.Block.CubeBlock.GetPosition(), originalPosition);
				MaximumRange = CurrentResponse.SearchRange - 0.5f * distance;
				//m_logger.debugLog("SearchRange: " + CurrentResponse.SearchRange + ", distance: " + distance + ", MaximumRange: " + MaximumRange, "Update()");
				if (!m_engageSet && distance < m_navSet.Settings_Current.DestinationRadius)
				{
					m_logger.debugLog("returned to original position", "Update()");
					m_originalPosition = Vector3D.PositiveInfinity;
				}
			}
			else
				MaximumRange = CurrentResponse.SearchRange;

			base.Update();
			m_navResponse.UpdateTarget(Grid);
		}

		private void NextResponse()
		{
			m_responseIndex++;
			if (m_responseIndex == m_allResponses.Count)
				CurrentResponse = new ResponseRange();
			else
				CurrentResponse = m_allResponses[m_responseIndex];
		}

		/// This runs after m_navResponse is created and will override settings.
		private void SetEngage()
		{
			m_logger.debugLog("entered", "SetEngage()", Logger.severity.DEBUG);
			m_navSet.OnTaskComplete_NavEngage();
			m_navSet.Settings_Task_NavEngage.NavigatorMover = m_navResponse;
			m_navSet.Settings_Task_NavEngage.NavigatorRotator = m_navResponse;
			m_navSet.Settings_Task_NavEngage.IgnoreAsteroid = false;
			m_navSet.Settings_Task_NavEngage.PathfinderCanChangeCourse = true;
			m_navSet.Settings_Task_NavEngage.DestinationEntity = m_mover.Block.CubeBlock;

			if (!m_originalPosition.IsValid() && MaximumRange > 1f)
			{
				m_originalDestEntity = m_navSet.Settings_Task_NavMove.DestinationEntity;
				if (m_originalDestEntity == null)
				{
					m_originalPosition = m_mover.Block.CubeBlock.GetPosition();
					m_logger.debugLog("original entity null", "SetEngage()", Logger.severity.DEBUG);
				}
				else if (m_originalDestEntity == m_mover.Block.CubeBlock || m_originalDestEntity == m_mover.Block.CubeGrid)
				{
					m_originalDestEntity = null;
					m_originalPosition = m_mover.Block.CubeBlock.GetPosition();
					m_logger.debugLog("original entity is me", "SetEngage()", Logger.severity.DEBUG);
				}
				else
				{
					m_logger.debugLog("original entity OK", "SetEngage()", Logger.severity.DEBUG);
					m_originalPosition = m_mover.Block.CubeBlock.GetPosition() - m_originalDestEntity.GetPosition();
				}

				m_logger.debugLog("original dest entity: " + m_originalDestEntity.getBestName() + ", original position: " + m_originalPosition, "SetEngage()", Logger.severity.DEBUG);
			}

			m_engageSet = true;
		}

		private void EndEngage()
		{
			m_navSet.OnTaskComplete_NavEngage();
			m_mover.MoveAndRotateStop();

			if (m_originalPosition.IsValid())
			{
				if (m_originalDestEntity != null)
				{
					m_logger.debugLog("return to original entity: " + m_originalDestEntity.getBestName() + ", offset: " + m_originalPosition, "EndEngage())", Logger.severity.DEBUG);
					new Waypoint(m_mover, m_navSet, AllNavigationSettings.SettingsLevelName.NavEngage, m_originalDestEntity, m_originalPosition);
				}
				else
				{
					m_logger.debugLog("return to original position: " + m_originalPosition, "EndEngage())", Logger.severity.DEBUG);
					new GOLIS(m_mover, m_navSet, m_originalPosition, AllNavigationSettings.SettingsLevelName.NavEngage);
				}
			}
			else
				m_logger.debugLog("original postion is invalid, no return possible", "EndEngage()");

			m_engageSet = false;
		}

	}
}
