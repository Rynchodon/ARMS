#if DEBUG
//#define DEBUG_IN_SPACE
#endif

using System;
using Rynchodon.Utility;
using Rynchodon.Utility.Network;
using Rynchodon.Utility.Vectors;
using Sandbox.Game.Entities;
using Sandbox.Game.World;
using Sandbox.ModAPI;
using VRage.Game.Components;
using VRage.Game.ModAPI;
using VRageMath;

namespace Rynchodon.Autopilot.Aerodynamics
{
	/// <summary>
	/// Applies the air effects to the grid. Runs AeroProfiler when needed.
	/// </summary>
	class AeroEffects
	{

		private class ProfileTask : RemoteTask
		{
			private AeroProfiler Profiler;
			[Argument]
			private long EntityId;
			[Result]
			public Vector3[] DragCoefficient;

			public ProfileTask() { }

			public ProfileTask(long EntityId)
			{
				this.EntityId = EntityId;
			}

			protected override void Start()
			{
				IMyCubeGrid grid = (IMyCubeGrid)MyAPIGateway.Entities.GetEntityById(EntityId);
				Profiler = new AeroProfiler(grid);
				Update.UpdateManager.Register(100, CheckProfiler);
			}

			private void CheckProfiler()
			{
				if (Profiler.Running)
					return;

				Update.UpdateManager.Unregister(100, CheckProfiler);
				if (Profiler.Success)
				{
					DragCoefficient = Profiler.DragCoefficient;
					Completed(Status.Success);
				}
				else
					Completed(Status.Exception);
			}
		}

		private const ulong ProfileWait = 200uL;

		private readonly IMyCubeGrid m_grid;
		private readonly CubeGridCache m_cache;

		private ulong m_profileAt;
		private ProfileTask m_profileTask;

		private float m_airDensity;
		private Vector3 m_worldDrag;

		// public because Autopilot is going to need it later
		public Vector3[] DragCoefficient;

		private Logable Log
		{ get { return new Logable(m_grid); } }

		public AeroEffects(IMyCubeGrid grid)
		{
			this.m_grid = grid;
			this.m_cache = CubeGridCache.GetFor(m_grid);
			this.m_profileAt = Globals.UpdateCount + ProfileWait;

			m_grid.OnBlockAdded += OnBlockChange;
			m_grid.OnBlockRemoved += OnBlockChange;

			Registrar.Add(grid, this);
		}

		public void Update1()
		{
			if (MyAPIGateway.Multiplayer.IsServer)
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

				Log.TraceLog("direction: " + direction + ", dot: " + dot + ", scaled: " + scaled);
			}

			MatrixD world = m_grid.WorldMatrix.GetOrientation();
			Vector3D worldDrag; Vector3D.Transform(ref localDrag, ref world, out worldDrag);
			m_worldDrag = worldDrag;

			Log.TraceLog("world velocity: " + worldVelocity + ", local velocity: " + localVelocity + ", local drag: " + localDrag + ", world drag: " + m_worldDrag);

			m_grid.Physics.AddForce(MyPhysicsForceType.APPLY_WORLD_FORCE, m_worldDrag, null, null);

			// I am assuming it is not worth the trouble to properly calculate angular resistance. This is similar to gyro damping.
			Vector3 angularVelocity = ((DirectionWorld)m_grid.Physics.AngularVelocity).ToGrid(m_grid);
			Vector3 impulse = angularVelocity * m_grid.Physics.Mass * -10f;
			m_grid.Physics.AddForce(MyPhysicsForceType.ADD_BODY_FORCE_AND_BODY_TORQUE, null, null, impulse);
		}

		public void Update100()
		{
			FillAirDensity();
			if (m_grid.IsStatic || m_airDensity == 0f)
				return;

			if (m_profileAt <= Globals.UpdateCount)
			{
				Log.DebugLog("Starting profile task", Logger.severity.DEBUG);
				m_profileAt = ulong.MaxValue;
				m_profileTask = new ProfileTask(m_grid.EntityId);
				RemoteTask.StartTask(m_profileTask, ((MyCubeGrid)m_grid).CubeBlocks.Count < 100);
				return;
			}
			if (m_profileTask != null && m_profileTask.CurrentStatus > RemoteTask.Status.Started)
			{
				if (m_profileTask.CurrentStatus == RemoteTask.Status.Success)
				{
					DragCoefficient = m_profileTask.DragCoefficient;
					for (int i = 0; i < 6; ++i)
						Log.DebugLog("Direction: " + (Base6Directions.Direction)i + ", DragCoefficient: " + DragCoefficient[i], Logger.severity.DEBUG);
				}
				m_profileTask = null;
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

			Vector3 drag = Vector3.Zero;
			foreach (IMyCubeGrid grid in Attached.AttachedGrid.AttachedGrids(m_grid, Attached.AttachedGrid.AttachmentKind.Physics, true))
			{
				AeroEffects aero;
				if (Registrar.TryGetValue(grid.EntityId, out aero))
				{
					Vector3.Add(ref drag, ref aero.m_worldDrag, out drag);
					Log.TraceLog("Drag: " + ((DirectionWorld)aero.m_worldDrag).ToGrid(m_grid) + " Grid: " + grid.nameWithId());
				}
			}

			//AeroDrawIndicators.DrawDrag(block, ref drag, m_airDensity);
		}

	}
}
