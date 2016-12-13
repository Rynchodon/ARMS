#if DEBUG
//#define DEBUG_IN_SPACE
#endif

using System;
using Sandbox.Game.Entities;
using Sandbox.Game.World;
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
		private Vector3 m_worldDrag;

		private AeroProfiler m_profiler;

		public Vector3[] DragCoefficient { get; private set; }

		public AeroEffects(IMyCubeGrid grid)
		{
			this.m_logger = new Logger(grid);
			this.m_grid = grid;
			this.m_profileAt = Globals.UpdateCount + ProfileWait;

			m_grid.OnBlockAdded += OnBlockChange;
			m_grid.OnBlockRemoved += OnBlockChange;
		}

		public void Update1()
		{
			ApplyDrag();
			DrawAirAndDrag();
		}

		private void ApplyDrag()
		{
			if (DragCoefficient == null || m_airDensity == 0f)
			{
				m_worldDrag = Vector3.Zero;
				return;
			}

			Vector3 worldVelocity = m_grid.Physics.LinearVelocity;

			if (worldVelocity.LengthSquared() < 1f)
			{
				m_worldDrag = Vector3.Zero;
				return;
			}

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
				Vector3 scaled; Vector3.Multiply(ref DragCoefficient[i], Math.Sign(dot) * dot * dot * m_airDensity, out scaled);
				Vector3.Add(ref localDrag, ref scaled, out localDrag);

				m_logger.traceLog("direction: " + direction + ", dot: " + dot + ", scaled: " + scaled);
			}

			MatrixD world = m_grid.WorldMatrix.GetOrientation();
			Vector3D worldDrag; Vector3D.Transform(ref localDrag, ref world, out worldDrag);
			m_worldDrag = worldDrag;

			m_logger.traceLog("world velocity: " + worldVelocity + ", local velocity: " + localVelocity + ", local drag: " + localDrag + ", world drag: " + m_worldDrag);

			m_grid.Physics.AddForce(MyPhysicsForceType.APPLY_WORLD_FORCE, m_worldDrag, null, null);
		}

		public void Update100()
		{
			FillAirDensity();
			if (m_profileAt <= Globals.UpdateCount && !m_grid.IsStatic && m_airDensity != 0f)
			{
				m_logger.debugLog("Running profiler", Logger.severity.DEBUG);
				m_profileAt = ulong.MaxValue;
				m_profiler = new AeroProfiler(m_grid);
				return;
			}
			if (m_profiler != null && !m_profiler.Running)
			{
				if (m_profiler.Success)
					DragCoefficient = m_profiler.DragCoefficient;
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

		private void DrawAirAndDrag()
		{
			MyPlayer player = MySession.Static.LocalHumanPlayer;
			if (player == null)
				return;

			MyCubeBlock block = player.Controller.ControlledEntity as MyCubeBlock;
			if (block == null || block.CubeGrid != m_grid)
				return;

			if (m_worldDrag == Vector3D.Zero)
				return;

			AeroDrawIndicators.DrawDrag(block, ref m_worldDrag, m_airDensity);
		}

	}
}
