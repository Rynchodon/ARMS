using System.Collections.Generic;
using Rynchodon.AntennaRelay;
using Rynchodon.Autopilot.Data;
using Rynchodon.Autopilot.Movement;
using Rynchodon.Autopilot.Navigator;
using Rynchodon.Autopilot.Navigator.Response;
using Rynchodon.Autopilot.Pathfinding;
using Rynchodon.Utility;
using VRage.ModAPI;
using VRageMath;

namespace Rynchodon.Autopilot
{

	public class EnemyFinder : GridFinder
	{

		public enum Response : byte { None, Fight, Flee, Ram, Self_Destruct, Land }

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

		private readonly Pathfinder m_pathfinder;
		private readonly AllNavigationSettings m_navSet;
		private readonly List<ResponseRange> m_allResponses = new List<ResponseRange>();
		/// <summary>The landing block at the time enemy finder is created, it will be vetted by EnemyLander</summary>
		private readonly PseudoBlock m_landingGear;

		private IEnemyResponse m_navResponse;
		private ResponseRange m_curResponse;
		private int m_responseIndex;

		public Vector3D m_originalPosition = Vector3D.PositiveInfinity;
		public IMyEntity m_originalDestEntity;
		private bool m_engageSet;

		private LastSeen value_grid;

		private Logable Log
		{
			get { return new Logable(m_pathfinder.Mover.Block.CubeBlock, CurrentResponse.Response.ToString()); }
		}

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
					Log.DebugLog("no longer have an Enemy", Logger.severity.DEBUG);
					EndEngage();
				}
				else
				{
					Log.DebugLog("Enemy is now: " + value_grid.Entity.getBestName(), Logger.severity.DEBUG);
					SetEngage();
				}
			}
		}

		private ResponseRange CurrentResponse
		{
			get { return m_curResponse; }
			set
			{
				Log.DebugLog("Changing response from " + m_curResponse.Response + " to " + value.Response, Logger.severity.DEBUG);
				m_navResponse = null;
				m_curResponse = value;
				switch (m_curResponse.Response)
				{
					case Response.None:
						Log.DebugLog("No responses left", Logger.severity.DEBUG);
						EndEngage();
						return;
					case Response.Fight:
						m_navResponse = new Fighter(m_pathfinder, m_navSet);
						break;
					case Response.Flee:
						m_navResponse = new Coward(m_pathfinder, m_navSet);
						break;
					case Response.Ram:
						m_navResponse = new Kamikaze(m_pathfinder, m_navSet);
						break;
					case Response.Self_Destruct:
						m_navResponse = new Self_Destruct(m_pathfinder.Mover.Block.CubeBlock);
						break;
					case Response.Land:
						m_navResponse = new EnemyLander(m_pathfinder, m_landingGear);
						break;
					default:
						Log.AlwaysLog("Response not implemented: " + m_curResponse.Response, Logger.severity.WARNING);
						NextResponse();
						return;
				}
				MaximumRange = m_curResponse.SearchRange;
				GridCondition = m_navResponse.CanTarget;
				if (Grid != null)
				{
					Log.DebugLog("adding responder: " + m_navResponse, Logger.severity.DEBUG);
					SetEngage();
				}
			}
		}

		public EnemyFinder(Pathfinder pathfinder, AllNavigationSettings navSet, long entityId)
			: base(navSet, pathfinder.Mover.Block)
		{
			this.m_pathfinder = pathfinder;
			this.m_navSet = navSet;
			this.m_targetEntityId = entityId;
			this.m_landingGear = m_navSet.Settings_Current.LandingBlock;

			Log.DebugLog("Initialized");
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
						Log.DebugLog("ammending old response: " + r + ", range: " + range);
						found = true;
						rr.SearchRange = range;
					}
				}
				if (!found)
				{
					Log.DebugLog("adding new response: " + r + ", range: " + range);
					m_allResponses.Add(new ResponseRange(r, range));
				}
			}
			if (CurrentResponse.Response == Response.None)
				CurrentResponse = m_allResponses[0];
		}

		public new void Update()
		{
			if (CurrentResponse.Response == Response.None)
				return;

			if (!m_navResponse.CanRespond())
			{
				Log.DebugLog("cannot respond");
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
				float distance = (float)Vector3D.Distance(m_pathfinder.Mover.Block.CubeBlock.GetPosition(), originalPosition);
				MaximumRange = CurrentResponse.SearchRange - 0.5f * distance;
				//Log.DebugLog("SearchRange: " + CurrentResponse.SearchRange + ", distance: " + distance + ", MaximumRange: " + MaximumRange, "Update()");
				if (!m_engageSet && distance < m_navSet.Settings_Current.DestinationRadius)
				{
					Log.DebugLog("returned to original position");
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
			Log.DebugLog("entered", Logger.severity.DEBUG);
			m_navSet.OnTaskComplete_NavEngage();
			m_navSet.Settings_Task_NavEngage.NavigatorMover = m_navResponse;
			m_navSet.Settings_Task_NavEngage.NavigatorRotator = m_navResponse;
			m_navSet.Settings_Task_NavEngage.IgnoreAsteroid = false;
			m_navSet.Settings_Task_NavEngage.PathfinderCanChangeCourse = true;
			if (!(m_navResponse is EnemyLander))
				m_navSet.Settings_Task_NavEngage.DestinationEntity = m_pathfinder.Mover.Block.CubeBlock;

			if (!m_originalPosition.IsValid() && MaximumRange > 1f)
			{
				m_originalDestEntity = m_navSet.Settings_Task_NavMove.DestinationEntity;
				if (m_originalDestEntity == null)
				{
					m_originalPosition = m_pathfinder.Mover.Block.CubeBlock.GetPosition();
					Log.DebugLog("original entity null", Logger.severity.DEBUG);
				}
				else if (m_originalDestEntity == m_pathfinder.Mover.Block.CubeBlock || m_originalDestEntity == m_pathfinder.Mover.Block.CubeGrid)
				{
					m_originalDestEntity = null;
					m_originalPosition = m_pathfinder.Mover.Block.CubeBlock.GetPosition();
					Log.DebugLog("original entity is me", Logger.severity.DEBUG);
				}
				else
				{
					Log.DebugLog("original entity OK", Logger.severity.DEBUG);
					m_originalPosition = m_pathfinder.Mover.Block.CubeBlock.GetPosition() - m_originalDestEntity.GetPosition();
				}

				Log.DebugLog("original dest entity: " + m_originalDestEntity.getBestName() + ", original position: " + m_originalPosition, Logger.severity.DEBUG);
			}

			m_engageSet = true;
		}

		private void EndEngage()
		{
			m_navSet.OnTaskComplete_NavEngage();
			m_pathfinder.Mover.MoveAndRotateStop();

			if (m_originalPosition.IsValid())
			{
				if (m_originalDestEntity != null)
				{
					Log.DebugLog("return to original entity: " + m_originalDestEntity.getBestName() + ", offset: " + m_originalPosition, Logger.severity.DEBUG);
					new Waypoint(m_pathfinder, AllNavigationSettings.SettingsLevelName.NavEngage, m_originalDestEntity, m_originalPosition);
				}
				else
				{
					Log.DebugLog("return to original position: " + m_originalPosition, Logger.severity.DEBUG);
					new GOLIS(m_pathfinder, m_originalPosition, AllNavigationSettings.SettingsLevelName.NavEngage);
				}
			}
			else
				Log.DebugLog("original postion is invalid, no return possible");

			m_engageSet = false;
		}

	}
}
