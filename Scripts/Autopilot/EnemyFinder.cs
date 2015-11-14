using System;
using System.Collections.Generic;
using System.Linq;
using Rynchodon.AntennaRelay;
using Rynchodon.Attached;
using Rynchodon.Autopilot.Data;
using Rynchodon.Autopilot.Movement;
using Rynchodon.Autopilot.Navigator;
using Sandbox.Common.ObjectBuilders;
using Sandbox.ModAPI;
using VRageMath;

namespace Rynchodon.Autopilot
{
	public class EnemyFinder : GridFinder
	{

		public enum Response : byte { None, Fight, Flee, Ram, Self_Destruct }

		private struct ResponseRange
		{
			public readonly Response Response;
			public float Range;

			public ResponseRange(Response resp, float range)
			{
				this.Response = resp;
				this.Range = range;
			}
		}

		private const ulong SearchInterval = 100ul;

		private readonly Logger m_logger;
		private readonly Mover m_mover;
		private readonly AllNavigationSettings m_navSet;
		private readonly List<ResponseRange> m_allResponses = new List<ResponseRange>();

		private IEnemyResponse m_navResponse;
		private ResponseRange m_curResponse;
		private int m_responseIndex;

		private LastSeen value_grid;

		public override LastSeen Grid
		{
			get { return value_grid; }
			protected set
			{
				if (value != null && value_grid != null && value.Entity == value_grid.Entity)
				{
					value_grid = value;
					return;
				}

				value_grid = value;
				if (value_grid == null)
				{
					m_logger.debugLog("no longer have an Enemy", "set_Enemy()", Logger.severity.DEBUG);
					m_navSet.OnTaskComplete_NavEngage();
					m_mover.MoveAndRotateStop();
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
						m_navSet.OnTaskComplete_NavEngage();
						m_mover.MoveAndRotateStop();
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
				MaximumRange = m_curResponse.Range;
				GridCondition = m_navResponse.CanTarget;
				if (Grid != null)
				{
					m_logger.debugLog("adding responder: " + m_navResponse, "set_CurrentResponse()", Logger.severity.DEBUG);
					SetEngage();
				}
			}
		}

		public EnemyFinder(Mover mover, AllNavigationSettings navSet)
			: base(navSet, mover.Block)
		{
			this.m_logger = new Logger(GetType().Name, mover.Block.CubeBlock, () => CurrentResponse.Response.ToString());
			this.m_mover = mover;
			this.m_navSet = navSet;

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
						rr.Range = range;
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
		}

	}
}
