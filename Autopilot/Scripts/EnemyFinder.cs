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
	public class EnemyFinder
	{

		public enum Response : byte { None, Fight, Flee, Ram, Grind, Self_Destruct }

		private struct ResponseRange
		{
			public readonly Response Response;
			private float RangeSquared;

			public float Range
			{ set { RangeSquared = value * value; } }

			public ResponseRange(Response resp, float range)
			{
				this.Response = resp;
				this.RangeSquared = range * range;
			}

			public bool InRange(float distanceSquared)
			{
				if (RangeSquared < 1f)
					return true;
				return distanceSquared <= RangeSquared;
			}
		}

		private const ulong SearchInterval = 100ul;

		private readonly Logger m_logger;
		private readonly Mover m_mover;
		private readonly AllNavigationSettings m_navSet;
		private readonly ShipControllerBlock m_autopilot;
		private readonly ShipController m_controller;
		private readonly List<ResponseRange> m_allResponses = new List<ResponseRange>();
		private readonly Vector3D m_startPosition;

		private readonly List<LastSeen> m_enemies = new List<LastSeen>();
		private IEnemyResponse m_navResponse;
		private LastSeen m_enemy;
		private ResponseRange m_curResponse;
		private ulong m_nextSearch;
		private int m_responseIndex;

		public LastSeen Enemy
		{
			get { return m_enemy; }
			set
			{
				if (value == m_enemy)
					return;

				m_enemy = value;
				if (m_enemy == null)
				{
					m_logger.debugLog("no longer have an Enemy", "set_Enemy()", Logger.severity.DEBUG);
					m_navSet.OnTaskComplete_NavEngage();
					m_mover.MoveAndRotateStop();
				}
				else
				{
					m_logger.debugLog("Enemy is now: " + m_enemy.Entity.getBestName(), "set_Enemy()", Logger.severity.DEBUG);
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
					//case Response.Grind:
					//	m_navResponse = new Grinder(m_mover, m_navSet);
					//	break;
					case Response.Self_Destruct:
						AttachedGrid.RunOnAttached(m_autopilot.CubeGrid, AttachedGrid.AttachmentKind.Terminal, grid => {
							var warheads = CubeGridCache.GetFor(grid).GetBlocksOfType(typeof(MyObjectBuilder_Warhead));
							if (warheads != null)
								foreach (var war in warheads)
									if (m_autopilot.CubeBlock.canControlBlock(war))
									{
										m_logger.debugLog("Starting countdown for " + war.getBestName(), "set_CurrentResponse()", Logger.severity.DEBUG);
										war.ApplyAction("StartCountdown");
									}
							return false;
						}, true);
						NextResponse(); // you never know
						return;
					default:
						m_logger.alwaysLog("Response not implemented: " + m_curResponse.Response, "set_CurrentResponse()", Logger.severity.WARNING);
						NextResponse();
						return;
				}
				if (Enemy != null)
				{
					m_logger.debugLog("adding responder: " + m_navResponse, "set_CurrentResponse()", Logger.severity.DEBUG);
					SetEngage();
				}
			}
		}

		public EnemyFinder(Mover mover, AllNavigationSettings navSet)
		{
			this.m_logger = new Logger(GetType().Name, mover.Block.CubeBlock, () => CurrentResponse.Response.ToString());
			this.m_mover = mover;
			this.m_navSet = navSet;
			this.m_autopilot = mover.Block;
			this.m_startPosition = m_autopilot.CubeBlock.GetPosition();

			if (!Registrar.TryGetValue(m_autopilot.CubeBlock.EntityId, out this.m_controller))
				throw new NullReferenceException("ShipControllerBlock is not a ShipController");

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

			if (Enemy == null)
			{
				if (Globals.UpdateCount >= m_nextSearch)
					Search();
			}
			else
				if (Globals.UpdateCount >= m_nextSearch)
				{
					m_nextSearch = Globals.UpdateCount + SearchInterval;
					if (!CanTarget(Enemy)
						|| !CurrentResponse.InRange(Vector3.DistanceSquared(m_autopilot.CubeBlock.GetPosition(), Enemy.predictPosition())))
					{
						m_logger.debugLog("LastSeen no longer satisfies condition", "Update()", Logger.severity.DEBUG);
						Search();
					}
					else
					{
						// update last seen
						if (m_controller.tryGetLastSeen(Enemy.Entity.EntityId, out m_enemy)) // bypass Enemy because the underlying entity is not changing
							m_logger.debugLog("Enemy updated from Controller", "Update()");
					}
				}

			if (Enemy != null && !Enemy.IsValid)
				Enemy = null;

			m_navResponse.UpdateTarget(Enemy);
		}

		private void Search()
		{
			m_logger.debugLog("searching", "Search()");

			m_nextSearch = Globals.UpdateCount + SearchInterval;
			Vector3D position = m_autopilot.CubeBlock.GetPosition();

			m_enemies.Clear();
			m_controller.ForEachLastSeen(seen => {
				if (!seen.IsValid)
					return false;

				IMyCubeGrid asGrid = seen.Entity as IMyCubeGrid;
				if (asGrid == null || !m_autopilot.CubeBlock.canConsiderHostile(asGrid))
					return false;

				m_enemies.Add(seen);
				m_logger.debugLog("enemy: " + seen.Entity.getBestName(), "Search()");
				return false;
			});

			m_logger.debugLog("number of enemies: " + m_enemies.Count, "Search()");
			IOrderedEnumerable<LastSeen> enemiesByDistance = m_enemies.OrderBy(seen => Vector3D.DistanceSquared(position, seen.predictPosition()));
			foreach (LastSeen enemy in enemiesByDistance)
			{
				if (CanTarget(enemy))
				{
					Enemy = enemy;
					m_logger.debugLog("found target: " + enemy.Entity.getBestName(), "Search()");
					return;
				}
			}

			Enemy = null;
			m_logger.debugLog("nothing found", "Search()");
		}

		private void NextResponse()
		{
			m_responseIndex++;
			if (m_responseIndex == m_allResponses.Count)
				CurrentResponse = new ResponseRange();
			else
				CurrentResponse = m_allResponses[m_responseIndex];
		}

		private bool CanTarget(LastSeen enemy)
		{
			IMyCubeGrid grid = enemy.Entity as IMyCubeGrid;
			try
			{
				// if it is too far from start, cannot target
				if (!CurrentResponse.InRange(Vector3.DistanceSquared(m_startPosition, enemy.predictPosition())))
				{
					m_logger.debugLog("out of range of start position: " + grid.DisplayName, "CanTarget()");
					return false;
				}

				// if it is too fast, cannot target
				float speedTarget = m_navSet.Settings_Current.SpeedTarget;
				if (grid.GetLinearVelocity().LengthSquared() > speedTarget * speedTarget)
				{
					m_logger.debugLog("too fast to target: " + grid.DisplayName, "CanTarget()");
					return false;
				}

				return m_navResponse.CanTarget(grid);
			}
			catch (NullReferenceException nre)
			{
				m_logger.debugLog("Exception: " + nre, "CanTarget()");

				if (!grid.Closed)
					throw nre;
				m_logger.debugLog("Caught exception caused by grid closing, ignoring.", "CanTarget()");
				return false;
			}
		}

		/// This runs after m_navResponse is created and will override settings.
		private void SetEngage()
		{
			m_navSet.OnTaskComplete_NavEngage();
			m_navSet.Settings_Task_NavEngage.NavigatorMover = m_navResponse;
			m_navSet.Settings_Task_NavEngage.NavigatorRotator = m_navResponse;
			m_navSet.Settings_Task_NavEngage.IgnoreAsteroid = false;
			m_navSet.Settings_Task_NavEngage.PathfinderCanChangeCourse = true;
		}

	}
}
