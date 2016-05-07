using System; // partial
using System.Collections.Generic;
using Sandbox.Game.Entities;
using Sandbox.ModAPI;
using VRage;
using VRage.Collections;
using VRage.Game.ModAPI;
using VRage.Library.Utils;
using VRageMath;

namespace Rynchodon.Autopilot.Pathfinder
{
	public class PlanetChecker
	{

		[Flags]
		public enum State : byte { None = 0, Running = 1, Clear = 2, BlockedPath = 4, BlockedGravity = 8, Blocked = BlockedPath | BlockedGravity }

		private const float MinGravityAvoid = 0.25f / 9.81f;
		private static readonly long CutOffAfter = (long)(Globals.UpdateDuration / 100f * MyGameTimer.Frequency);

		private static LockedQueue<Action> DoTests = new LockedQueue<Action>();
		private static LineSegmentD Path = new LineSegmentD();
		private static MyGameTimer Timer = new MyGameTimer();
		private static Logger s_logger = new Logger("PlanetChecker");

		static PlanetChecker()
		{
			MyAPIGateway.Entities.OnCloseAll += Entities_OnCloseAll;
			Update.UpdateManager.Register(1, DoTest);
		}

		private static void Entities_OnCloseAll()
		{
			MyAPIGateway.Entities.OnCloseAll -= Entities_OnCloseAll;
			DoTests = null;
			Path = null;
			Timer = null;
			s_logger = null;
		}

		private static void DoTest()
		{
			int count = 0;
			long cutoff = Timer.ElapsedTicks + CutOffAfter;
			while (Timer.ElapsedTicks < cutoff)
			{
				count++;
				Action test;
				if (DoTests.TryDequeue(out test))
					test.Invoke();
				else
					return;
			}
			s_logger.debugLog("tests: " + count);
		}

		private readonly Logger m_logger;
		private readonly IMyCubeGrid m_grid;
		private readonly FastResourceLock m_lock = new FastResourceLock();

		private MyQueue<Vector3I> m_cells;
		private HashSet<Vector3I> m_cellsUnique;
		private Vector3 m_displacement;

		public MyPlanet ClosestPlanet { get; private set; }
		public State CurrentState { get; private set; }
		public Vector3D ObstructionPoint { get; private set; }

		public PlanetChecker(IMyCubeGrid grid)
		{
			this.m_logger = new Logger(GetType().Name, grid.getBestName, ClosestPlanet.getBestName, CurrentState.ToString);
			this.m_grid = grid;
			this.m_cells = new MyQueue<Vector3I>(8);
			this.m_cellsUnique = new HashSet<Vector3I>();
		}

		/// <summary>
		/// Starts checking path against the geometry of the closest planet.
		/// </summary>
		/// <param name="displacement">Destination - current postion</param>
		public void Start(Vector3 displacement)
		{
			Start(ref displacement);
		}

		/// <summary>
		/// Starts checking path against the geometry of the closest planet.
		/// </summary>
		/// <param name="displacement">Destination - current postion</param>
		private void Start(ref Vector3 displacement)
		{
			using (m_lock.AcquireExclusiveUsing())
			{
				if ((CurrentState & State.Running) != 0)
					return;
				CurrentState |= State.Running;

				m_displacement = displacement;

				m_cells.Clear();

				Vector3D gridCentre = m_grid.GetCentre();
				double distSq;
				ClosestPlanet = MyPlanetExtensions.GetClosestPlanet(gridCentre, out distSq);

				if (ClosestPlanet == null)
				{
					//m_logger.debugLog("No planets found", "Start()", Logger.severity.TRACE);
					CurrentState = State.Clear;
					return;
				}

				if (distSq > ClosestPlanet.MaximumRadius * ClosestPlanet.MaximumRadius)
				{
					//m_logger.debugLog("Outside maximum radius of closest planet", "Start()", Logger.severity.TRACE);

					// gravity test
					// TODO: it might be worthwhile to perform gravity test against multiple planets
					Path.From = gridCentre;
					Path.To = gridCentre + displacement;

					Vector3D closestPoint = Path.ClosestPoint(ClosestPlanet.WorldMatrix.Translation);
					if (closestPoint != Path.From && closestPoint != Path.To)
					{
						float gravityAtClose = ClosestPlanet.GetGravityMultiplier(closestPoint) - MinGravityAvoid;
						if (gravityAtClose > 0f && gravityAtClose > ClosestPlanet.GetGravityMultiplier(Path.From) && gravityAtClose > ClosestPlanet.GetGravityMultiplier(Path.To))
						{
							ObstructionPoint = closestPoint;
							CurrentState = State.BlockedGravity;
							return;
						}
					}

					CurrentState = State.Clear;
					return;
				}

				Vector3 direction;
				Vector3.Normalize(ref displacement, out direction);
				direction = Vector3.Transform(direction, m_grid.WorldMatrixNormalizedInv.GetOrientation());

				GridCellCache.GetCellCache(m_grid).ForEach(cell => {
					Vector3I rejected;
					Vector3.Reject(cell, direction).ApplyOperation(x => (int)Math.Round(x), out rejected);

					if (m_cellsUnique.Add(rejected))
						m_cells.Enqueue(rejected);
				});

				m_cellsUnique.Clear();

				DoTests.Enqueue(TestPath);
			}
		}

		/// <summary>
		/// Stop testing, set CurrentState to None.
		/// </summary>
		public void Stop()
		{
			using (m_lock.AcquireExclusiveUsing())
			{
				CurrentState = State.None;
				m_cells.Clear();
			}
		}

		private void TestPath()
		{
			using (m_lock.AcquireExclusiveUsing())
			{
				if (m_grid.MarkedForClose)
				{
					CurrentState = State.BlockedPath;
					m_cells.Clear();
					return;
				}

				if (CurrentState == State.None)
				{
					m_cells.Clear();
					return;
				}

				//MyGameTimer timer = new MyGameTimer();
				if (m_cells.Count != 0)
				{
					Vector3D worldPos = m_grid.GridIntegerToWorld(m_cells.Dequeue());
					LineD worldLine = new LineD(worldPos, worldPos + m_displacement);
					//var createLine = timer.Elapsed;
					//timer = new MyGameTimer();

					Vector3D? contact;
					if (RayCast.RayCastVoxel(ClosestPlanet, ref worldLine, out contact))
					{
						//var intersect = timer.Elapsed;
						m_logger.debugLog("Intersected line: " + worldLine.From + " to " + worldLine.To + ", at " + contact, Logger.severity.DEBUG);
						//m_logger.debugLog("Intersected line: " + worldLine.From + " to " + worldLine.To + ", at " + contact + ", createLine: " + createLine.ToPrettySeconds() + ", intersect: " + intersect.ToPrettySeconds(), "TestPath()", Logger.severity.DEBUG);
						ObstructionPoint = contact.Value;
						CurrentState = State.BlockedPath;
						m_cells.Clear();
						return;
					}
					//else
					//{
					//	var intersect = timer.Elapsed;
					//	m_logger.debugLog("no intersection with line: " + worldLine.From + " to " + worldLine.To + ", createLine: " + createLine.ToPrettySeconds() + ", intersect: " + intersect.ToPrettySeconds(), "TestPath()", Logger.severity.TRACE);
					//}

					DoTests.Enqueue(TestPath);
				}
				else
				{
					m_logger.debugLog("finished, clear", Logger.severity.DEBUG);
					CurrentState = State.Clear;
				}
			}
		}

	}
}
