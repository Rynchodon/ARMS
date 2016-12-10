#if DEBUG
// if this is enabled, stop AeroProfiler from disposing of m_data
//#define DEBUG_DRAW
#define DEBUG_IN_SPACE
#endif

using System;
using System.Text;
using Rynchodon.Update;
using Sandbox.Common.ObjectBuilders;
using Sandbox.Game.Entities;
using Sandbox.ModAPI;
using VRage.Game.Components;
using VRage.Game.ModAPI;
using VRageMath;

namespace Rynchodon.Autopilot.Movement
{
	class AeroEffects
	{

		private const ulong ProfileWait = 200uL;

		private readonly Logger m_logger;
		private readonly IMyCubeGrid m_grid;

		private ulong m_profileAt;
		private float m_airDensity;
		private bool m_cockpitComplain;
#if DEBUG_DRAW
		private bool m_profilerDebugDraw;
		private int m_maxSteps = int.MaxValue;
#endif

		private AeroProfiler value_profiler;
		private AeroProfiler m_profiler
		{
			get { return value_profiler; }
			set
			{
#if DEBUG_DRAW
				DebugDraw(false);
#endif
				value_profiler = value;
			}
		}

		public Vector3[] DragCoefficient { get; private set; }

		public AeroEffects(IMyCubeGrid grid)
		{
			this.m_logger = new Logger(grid);
			this.m_grid = grid;
			this.m_profileAt = Globals.UpdateCount + ProfileWait;

			m_grid.OnBlockAdded += OnBlockChange;
			m_grid.OnBlockRemoved += OnBlockChange;
#if DEBUG_DRAW
			MyAPIGateway.Utilities.MessageEntered += Utilities_MessageEntered;
			m_grid.OnClose += OnGridClose;
#endif
		}

#if DEBUG_DRAW
		private void Utilities_MessageEntered(string messageText, ref bool sendToOthers)
		{
			int steps;
			if (!int.TryParse(messageText, out steps) || steps == m_maxSteps)
				return;
			m_maxSteps = steps;
			m_logger.debugLog("Running profiler", Logger.severity.DEBUG);
			m_profileAt = ulong.MaxValue;
			m_profiler = new AeroProfiler(m_grid, m_maxSteps);
		}
#endif

		public void Update1()
		{
			if (DragCoefficient == null || m_airDensity == 0f)
				return;

			Vector3 worldVelocity = m_grid.Physics.LinearVelocity;

			if (worldVelocity.LengthSquared() < 1f)
				return;

			MatrixD worldInv = m_grid.WorldMatrixNormalizedInv.GetOrientation();
			Vector3D localVelocityD; Vector3D.Transform(ref worldVelocity, ref worldInv, out localVelocityD);
			Vector3 localVelocity = localVelocityD;

			Vector3 localDrag = Vector3.Zero;
			for (int i = 0; i < 6; ++i)
			{
				Base6Directions.Direction direction = (Base6Directions.Direction)i;
				Vector3 vectorDirection; Base6Directions.GetVector(direction, out vectorDirection);
				float dot; Vector3.Dot(ref localVelocity, ref vectorDirection, out dot);
				// intentionally keeping negative values
				Vector3 scaled; Vector3.Multiply(ref DragCoefficient[i], Math.Sign(dot) * dot * dot, out scaled);
				Vector3.Add(ref localDrag, ref scaled, out localDrag);

				m_logger.debugLog("direction: " + direction + ", dot: " + dot + ", scaled: " + scaled);
			}

			MatrixD world = m_grid.WorldMatrix.GetOrientation();
			Vector3D worldDrag; Vector3D.Transform(ref localDrag, ref world, out worldDrag);

			m_logger.debugLog("world velocity: " + worldVelocity + ", local velocity: " + localVelocity + ", local drag: " + localDrag + ", world drag: " + worldDrag);

			m_grid.Physics.AddForce(MyPhysicsForceType.APPLY_WORLD_FORCE, worldDrag, null, null);
		}

		public void Update100()
		{
			FillAirDensity();
			if (m_profileAt <= Globals.UpdateCount && !m_grid.IsStatic && m_airDensity != 0f)
			{
				m_logger.debugLog("Running profiler", Logger.severity.DEBUG);
				m_profileAt = ulong.MaxValue;
				SetCockpitComplain(true);
#if DEBUG_DRAW
				m_profiler = new AeroProfiler(m_grid, m_maxSteps);
#else
				m_profiler = new AeroProfiler(m_grid);
#endif
				return;
			}
#if DEBUG_DRAW
			if (m_profiler != null && !m_profiler.Running && m_profiler.Success)
				DebugDraw(true);
#endif
			if (m_profiler != null && !m_profiler.Running)
			{
				if (m_profiler.Success)
				{
					DragCoefficient = m_profiler.DragCoefficient;
					SetCockpitComplain(false);
				}
				m_profiler = null;
			}
		}

		private void FillAirDensity()
		{
#if DEBUG_IN_SPACE
			m_airDensity = 1f;
#else
			Vector3D gridCentre = m_grid.GetCentre();
			MyPlanet closestPlanet = MyPlanetExtensions.GetClosestPlanet(gridCentre);
			if (closestPlanet == null)
				m_airDensity = 0f;
			else
				m_airDensity = closestPlanet.GetAirDensity(gridCentre);
#endif
		}

		private void OnBlockChange(IMySlimBlock obj)
		{
			m_profileAt = Globals.UpdateCount + ProfileWait;
		}

		private void SetCockpitComplain(bool enabled)
		{
			if (m_cockpitComplain == enabled)
				return;
			m_cockpitComplain = enabled;

			CubeGridCache cache = CubeGridCache.GetFor(m_grid);
			if (cache == null)
				return;
			foreach (MyCockpit cockpit in cache.BlocksOfType(typeof(MyObjectBuilder_Cockpit)))
			{
				if (m_cockpitComplain)
					cockpit.AppendingCustomInfo += CockpitComplain;
				else
					cockpit.AppendingCustomInfo -= CockpitComplain;
				cockpit.RefreshCustomInfo();
			}
		}

		private void CockpitComplain(Sandbox.Game.Entities.Cube.MyTerminalBlock arg1, StringBuilder arg2)
		{
			arg2.AppendLine("ARMS is calculating drag");
		}

#if DEBUG_DRAW
		private void OnGridClose(VRage.ModAPI.IMyEntity obj)
		{
			m_profiler = null;
			MyAPIGateway.Utilities.MessageEntered -= Utilities_MessageEntered;
		}

		private void DebugDraw(bool enable)
		{
			if (enable == m_profilerDebugDraw)
				return;

			if (enable)
			{
				m_profilerDebugDraw = true;
				UpdateManager.Register(1, m_profiler.DebugDraw_Velocity);
				Logger.DebugNotify("Debug drawing velocity");
			}
			else
			{
				m_profilerDebugDraw = false;
				UpdateManager.Unregister(1, m_profiler.DebugDraw_Velocity);
			}
		}
#endif

	}
}
