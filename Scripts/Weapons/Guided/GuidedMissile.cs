using System;
using System.Collections.Generic;
using Rynchodon.AntennaRelay;
using Rynchodon.Threading;
using Rynchodon.Weapons.SystemDisruption;
using Sandbox.Game.Entities;
using Sandbox.ModAPI;
using VRage;
using VRage.Collections;
using VRage.Game;
using VRage.ModAPI;
using VRage.ObjectBuilders;
using VRageMath;

namespace Rynchodon.Weapons.Guided
{
	/*
	 * TODO:
	 * Lockon notification for ready-to-launch and to warn of incoming (only some tracking types)
	 * SA*
	 */
	public class GuidedMissile : TargetingBase
	{

		private class RailData
		{
			public readonly Vector3D RailStart;
			public readonly LineSegmentD Rail;
			public readonly DateTime Created;

			public RailData(Vector3D start)
			{
				RailStart = start;
				Rail = new LineSegmentD();
				Created = DateTime.UtcNow;
			}
		}

		private class GravityData
		{
			public readonly MyPlanet Planet;
			public readonly Vector3 GravNormAtTarget;
			public Vector3 Normal;
			public Vector3 AccelPerUpdate;

			public GravityData(MyPlanet planet, Vector3 gravityNormAtTarget)
			{
				this.Planet = planet;
				this.GravNormAtTarget = gravityNormAtTarget;
			}
		}

		private enum Stage : byte { Rail, Boost, MidCourse, SemiActive, Guided, Ballistic, Terminated }

		private const float Angle_AccelerateWhen = 0.02f;
		private const float Angle_Detonate = 0.1f;
		private const float Angle_Cluster = 0.05f;

		private static Logger staticLogger = new Logger("GuidedMissile");
		private static ThreadManager Thread = new ThreadManager();
		private static CachingList<GuidedMissile> AllGuidedMissiles = new CachingList<GuidedMissile>();
		private static FastResourceLock lock_AllGuidedMissiles = new FastResourceLock();
		private static Dictionary<long, long> s_missileOwners = new Dictionary<long, long>();

		static GuidedMissile()
		{
			MyAPIGateway.Entities.OnCloseAll += Entities_OnCloseAll;
		}

		private static void Entities_OnCloseAll()
		{
			MyAPIGateway.Entities.OnCloseAll -= Entities_OnCloseAll;
			staticLogger = null;
			Thread = null;
			AllGuidedMissiles = null;
			lock_AllGuidedMissiles = null;
			s_missileOwners = null;
		}

		public static void Update1()
		{
			if (lock_AllGuidedMissiles.TryAcquireExclusive())
			{
				AllGuidedMissiles.ApplyChanges();
				lock_AllGuidedMissiles.ReleaseExclusive();
			}

			using (lock_AllGuidedMissiles.AcquireSharedUsing())
				foreach (GuidedMissile missile in AllGuidedMissiles)
				{
					Thread.EnqueueAction(() => {
						if (missile.Stopped)
							return;
						if (missile.myCluster != null)
							missile.UpdateCluster();
						if (missile.myTarget.TType == TargetType.None)
							return;
						if (missile.m_stage >= Stage.MidCourse && missile.m_stage <= Stage.Guided)
							missile.SetFiringDirection();
						missile.Update();
					});
					if (missile.m_rail != null)
						missile.UpdateRail();
					if (missile.m_stage == Stage.Boost)
						missile.ApplyGravity();
				}
		}

		public static void Update10()
		{
			using (lock_AllGuidedMissiles.AcquireSharedUsing())
				foreach (GuidedMissile missile in AllGuidedMissiles)
					Thread.EnqueueAction(() => {
						if (missile.Stopped)
							return;
						if (missile.m_stage == Stage.SemiActive)
							missile.TargetSemiActive();
						else
						{
							if (missile.m_stage == Stage.Guided && missile.myDescr.TargetRange > 1f)
								missile.UpdateTarget();
							if ((missile.CurrentTarget.TType == TargetType.None || missile.CurrentTarget is LastSeenTarget) && missile.myAntenna != null)
								missile.GetLastSeenTarget(missile.myAntenna.Storage, missile.myAmmo.MissileDefinition.MaxTrajectory);
						}
						missile.CheckGuidance();
					});
		}

		public static void Update100()
		{
			using (lock_AllGuidedMissiles.AcquireSharedUsing())
				foreach (GuidedMissile missile in AllGuidedMissiles)
				{
					Thread.EnqueueAction(() => {
						if (!missile.Stopped)
						{
							missile.ClearBlacklist();
							if (missile.m_gravData != null)
								missile.UpdateGravity();
							if (missile.m_radar != null && missile.CurrentTarget.TType == TargetType.None)
								missile.m_radar.Update100();
						}
					});
					if (!missile.Stopped)
						missile.UpdateNetwork();
				}
		}

		[Obsolete("Use TryGetOwnerId")]
		public static long GetOwnerId(long missileId)
		{
			long owner;
			if (s_missileOwners.TryGetValue(missileId, out owner))
				return owner;
			return 0L;
		}

		public static bool TryGetOwnerId(long missileId, out long OwnerId)
		{
			return s_missileOwners.TryGetValue(missileId, out OwnerId);
		}

		public static bool IsGuidedMissile(long missileId)
		{
			return s_missileOwners.ContainsKey(missileId);
		}

		private static void AddMissileOwner(IMyEntity missile, long owner)
		{
			s_missileOwners.Add(missile.EntityId, owner);
			missile.OnClose += missile_OnClose;
		}

		private static void missile_OnClose(IMyEntity obj)
		{
			if (s_missileOwners == null)
				return;
			s_missileOwners.Remove(obj.EntityId);
		}

		private readonly Logger myLogger;
		private readonly Ammo myAmmo;
		private readonly NetworkNode myAntenna;
		private readonly GuidedMissileLauncher m_launcher;

		private Cluster myCluster;
		private IMyEntity myRock;
		private DateTime myGuidanceEnds;
		private float addSpeedPerUpdate, acceleration;
		private Stage m_stage;
		private RailData m_rail;
		private GravityData m_gravData;
		private RadarEquipment m_radar;

		private bool Stopped
		{ get { return MyEntity.Closed || m_stage == Stage.Terminated; } }

		private Ammo.AmmoDescription myDescr
		{ get { return myAmmo.Description; } }

		private NetworkClient m_launcherClient
		{ get { return m_launcher.m_netClient; } }

		/// <summary>
		/// Creates a missile with homing and target finding capabilities.
		/// </summary>
		public GuidedMissile(IMyEntity missile, GuidedMissileLauncher launcher, LastSeen initialTarget)
			: base(missile, launcher.CubeBlock)
		{
			myLogger = new Logger("GuidedMissile", () => missile.getBestName(), () => m_stage.ToString());
			m_launcher = launcher;
			myAmmo = launcher.loadedAmmo;
			if (myAmmo.Description.HasAntenna)
				myAntenna = new NetworkNode(missile, launcher.CubeBlock, ComponentRadio.CreateRadio(missile, 0f));
			TryHard = true;
			SEAD = myAmmo.Description.SEAD;

			AllGuidedMissiles.Add(this);
			AddMissileOwner(MyEntity, CubeBlock.OwnerId);
			MyEntity.OnClose += MyEntity_OnClose;

			acceleration = myDescr.Acceleration + myAmmo.MissileDefinition.MissileAcceleration;
			addSpeedPerUpdate = myDescr.Acceleration * Globals.UpdateDuration;
			if (!(launcher.CubeBlock is Sandbox.ModAPI.Ingame.IMyLargeTurretBase))
				m_rail = new RailData(Vector3D.Transform(MyEntity.GetPosition(), CubeBlock.WorldMatrixNormalizedInv));

			Options = m_launcher.m_weaponTarget.Options.Clone();
			Options.TargetingRange = myAmmo.Description.TargetRange;
			if (initialTarget != null)
			{
				IMyCubeBlock initialBlock;
				if (ChooseBlock(initialTarget, out initialBlock))
				{
					myLogger.debugLog("initial target: " + initialTarget.Entity.getBestName() + ", block: " + initialBlock.getBestName(), "GuidedMissile()", Logger.severity.DEBUG);
					myTarget = new LastSeenTarget(initialTarget, initialBlock);
					CurrentTarget = myTarget;
				}
			}

			if (myAmmo.RadarDefinition != null)
			{
				myLogger.debugLog("Has a radar definiton", "GuidedMissile()");
				m_radar = new RadarEquipment(missile, myAmmo.RadarDefinition, launcher.CubeBlock);
				if (myAntenna == null)
				{
					myLogger.debugLog("Creating node for radar", "GuidedMissile()");
					myAntenna = new NetworkNode(missile, launcher.CubeBlock, null);
				}
			}

			myLogger.debugLog("Options: " + Options + ", initial target: " + (initialTarget == null ? "null" : initialTarget.Entity.getBestName()), "GuidedMissile()");
			//myLogger.debugLog("AmmoDescription: \n" + MyAPIGateway.Utilities.SerializeToXML<Ammo.AmmoDescription>(myDescr), "GuidedMissile()");
		}

		public GuidedMissile(Cluster missiles, GuidedMissileLauncher launcher, LastSeen initialTarget)
			: this(missiles.Master, launcher, initialTarget)
		{
			myCluster = missiles;
		}

		private void MyEntity_OnClose(IMyEntity obj)
		{
			if (AllGuidedMissiles != null)
			{
				AllGuidedMissiles.Remove(this);
				RemoveRock();

				myLogger.debugLog("EMP_Seconds: " + myDescr.EMP_Seconds + ", EMP_Strength: " + myDescr.EMP_Strength, "MyEntity_OnClose()");
				if (myDescr.EMP_Seconds > 0f && myDescr.EMP_Strength > 0)
				{
					myLogger.debugLog("Creating EMP effect", "MyEntity_OnClose()", Logger.severity.DEBUG);
					BoundingSphereD empSphere = new BoundingSphereD(ProjectilePosition(), myAmmo.MissileDefinition.MissileExplosionRadius);
					EMP.ApplyEMP(empSphere, myDescr.EMP_Strength, TimeSpan.FromSeconds(myDescr.EMP_Seconds));
				}
			}
		}

		protected override bool CanRotateTo(Vector3D targetPos)
		{
			return true;
		}

		protected override bool Obstructed(Vector3D targetPos)
		{
			return false;
		}

		protected override float ProjectileSpeed(Vector3D targetPos)
		{
			return acceleration;
		}

		private void TargetSemiActive()
		{
			SemiActiveTarget sat = myTarget as SemiActiveTarget;
			if (sat == null)
			{
				myLogger.debugLog("creating semi-active target", "TargetSemiActive()", Logger.severity.DEBUG);
				sat = new SemiActiveTarget(m_launcher.CubeBlock);
				myTarget = sat;
			}

			sat.Update(MyEntity);
		}

		/// <remarks>
		/// Runs on separate thread.
		/// </remarks>
		private void Update()
		{
			Target cached = CurrentTarget;
			if (!cached.FiringDirection.HasValue)
				return;

			//myLogger.debugLog("target: " + cached.Entity.getBestName() + ", ContactPoint: " + cached.ContactPoint, "Update()");

			Vector3 targetDirection;

			switch (m_stage)
			{
				case Stage.Boost:
					myLogger.debugLog(m_gravData == null, "m_gravData == null", "Update()", Logger.severity.FATAL);
					targetDirection = -m_gravData.Normal;
					break;
				case Stage.MidCourse:
					Vector3 toTarget = cached.GetPosition() - MyEntity.GetPosition();
					targetDirection = Vector3.Normalize(Vector3.Reject(toTarget, m_gravData.Normal));
					break;
				case Stage.SemiActive:
				case Stage.Guided:
					targetDirection = cached.FiringDirection.Value;
					break;
				default:
					return;
			}

			Vector3 forward = MyEntity.WorldMatrix.Forward;
			float angle = forward.AngleBetween(targetDirection);

			if (m_stage <= Stage.Guided && angle > 0.001f) // if the angle is too small, the matrix will be invalid
			{ // rotate missile
				float rotate = Math.Min(angle, myDescr.RotationPerUpdate);
				Vector3 axis = forward.Cross(targetDirection);
				axis.Normalize();
				Quaternion rotation = Quaternion.CreateFromAxisAngle(axis, rotate);

				MyAPIGateway.Utilities.TryInvokeOnGameThread(() => {
					if (!Stopped)
					{
						MatrixD WorldMatrix = MyEntity.WorldMatrix;
						MatrixD newMatrix = WorldMatrix.GetOrientation();
						newMatrix = MatrixD.Transform(newMatrix, rotation);
						newMatrix.Translation = WorldMatrix.Translation;

						MyEntity.WorldMatrix = newMatrix;
					}
				}, myLogger);
			}

			//myLogger.debugLog("targetDirection: " + targetDirection + ", forward: " + forward, "Update()");

			{ // accelerate if facing target
				if (angle < Angle_AccelerateWhen && addSpeedPerUpdate > 0f && MyEntity.GetLinearVelocity().LengthSquared() < myAmmo.AmmoDefinition.DesiredSpeed * myAmmo.AmmoDefinition.DesiredSpeed)
				{
					//myLogger.debugLog("accelerate. angle: " + angle, "Update()");
					MyAPIGateway.Utilities.TryInvokeOnGameThread(() => {
						if (!Stopped)
							MyEntity.Physics.LinearVelocity += MyEntity.WorldMatrix.Forward * addSpeedPerUpdate;
					}, myLogger);
				}
			}

			{ // check for proxmity det
				if (angle >= Angle_Detonate && myDescr.DetonateRange > 0f)
				{
					float distSquared = Vector3.DistanceSquared(MyEntity.GetPosition(), cached.GetPosition() + cached.Entity.GetLinearVelocity());
					//myLogger.debugLog("distSquared: " + distSquared, "Update()");
					if (distSquared <= myDescr.DetonateRange * myDescr.DetonateRange)
					{
						Explode();
						return;
					}
				}
			}
		}

		/// <remarks>
		/// Runs on separate thread.
		/// </remarks>
		private void UpdateCluster()
		{
			Vector3D[] slavePosition = new Vector3D[myCluster.Slaves.Count];
			float moveBy = Globals.UpdateDuration;
			float moveBySq = moveBy * moveBy;
			if (myTarget.Entity != null)
				myCluster.AdjustMulti(myTarget.Entity.LocalAABB.GetLongestDim() * 0.5f);

			for (int i = 0; i < slavePosition.Length; i++)
			{
				Vector3D slavePos = myCluster.Slaves[i].GetPosition() - MyEntity.GetPosition();
				Vector3D destination = myCluster.SlaveOffsets[i] * myCluster.OffsetMulti;
				double distSquared = Vector3D.DistanceSquared(slavePos, destination);

				if (distSquared > moveBySq)
				{
					Vector3D direction = (destination - slavePos) / (float)Math.Sqrt(distSquared);
					slavePosition[i] = slavePos + direction * moveBy;
				}
				else
				{
					slavePosition[i] = destination;
				}
			}

			MyAPIGateway.Utilities.TryInvokeOnGameThread(() => {
				if (Stopped)
					return;

				myLogger.debugLog(myCluster == null, "myCluster == null", "UpdateCluster()", Logger.severity.FATAL);

				for (int i = 0; i < myCluster.Slaves.Count; i++)
				{
					if (myCluster.Slaves[i].Closed || i >= slavePosition.Length)
						continue;
					MatrixD worldMatrix = MyEntity.WorldMatrix;
					worldMatrix.Translation += slavePosition[i];
					myCluster.Slaves[i].WorldMatrix = worldMatrix;
					myCluster.Slaves[i].Physics.LinearVelocity = MyEntity.Physics.LinearVelocity;
				}

			}, myLogger);
		}

		/// <summary>
		/// Spawns a rock to explode the missile.
		/// </summary>
		/// <remarks>
		/// Runs on separate thread. (sort-of)
		/// </remarks>
		private void Explode()
		{
			MyAPIGateway.Utilities.TryInvokeOnGameThread(() => {
				if (MyEntity.Closed)
					return;
				m_stage = Stage.Terminated;

				MyEntity.Physics.LinearVelocity = Vector3.Zero;

				RemoveRock();

				MyObjectBuilder_InventoryItem item = new MyObjectBuilder_InventoryItem() { Amount = 100, Content = new MyObjectBuilder_Ore() { SubtypeName = "Stone" } };

				MyObjectBuilder_FloatingObject rockBuilder = new MyObjectBuilder_FloatingObject();
				rockBuilder.Item = item;
				rockBuilder.PersistentFlags = MyPersistentEntityFlags2.InScene;
				rockBuilder.PositionAndOrientation = new MyPositionAndOrientation()
				{
					Position = MyEntity.GetPosition(),
					Forward = (Vector3)MyEntity.WorldMatrix.Forward,
					Up = (Vector3)MyEntity.WorldMatrix.Up
				};

				myRock = MyAPIGateway.Entities.CreateFromObjectBuilderAndAdd(rockBuilder);
				myLogger.debugLog("created rock at " + MyEntity.GetPosition() + ", " + myRock.getBestName(), "Explode()");
			}, myLogger);
		}

		/// <summary>
		/// Only call from game thread! Remove the rock created by Explode().
		/// </summary>
		private void RemoveRock()
		{
			if (myRock == null || myRock.Closed)
				return;

			myLogger.debugLog("removing rock", "RemoveRock()");
			myRock.Delete();
		}

		/// <summary>
		/// Updates m_stage if guidance starts or stops.
		/// </summary>
		private void CheckGuidance()
		{
			switch (m_stage)
			{
				case Stage.Rail:
					double minDist = (MyEntity.WorldAABB.Max - MyEntity.WorldAABB.Min).AbsMax();
					minDist *= 2;

					if (CubeBlock.WorldAABB.DistanceSquared(MyEntity.GetPosition()) >= minDist * minDist)
					{
						m_rail = null;
						if (myDescr.SemiActiveLaser)
						{
							myGuidanceEnds = DateTime.UtcNow.AddSeconds(myDescr.GuidanceSeconds);
							myLogger.debugLog("past arming range, semi-active until " + myGuidanceEnds.ToLongTimeString(), "CheckGuidance()", Logger.severity.INFO);
							m_stage = Stage.SemiActive;
							return;
						}

						if (myAmmo.Description.BoostDistance > 1f)
						{
							myLogger.debugLog("past arming range, starting boost stage", "CheckGuidance()", Logger.severity.INFO);
							StartGravity();
							m_stage = Stage.Boost;
							if (m_gravData == null)
							{
								myLogger.debugLog("no gravity, terminating", "CheckGuidance()", Logger.severity.WARNING);
								m_stage = Stage.Terminated;
							}
						}
						else
						{
							myGuidanceEnds = DateTime.UtcNow.AddSeconds(myDescr.GuidanceSeconds);
							myLogger.debugLog("past arming range, starting guidance. Guidance until " + myGuidanceEnds.ToLongTimeString(), "CheckGuidance()", Logger.severity.INFO);
							m_stage = Stage.Guided;
						}
					}
					return;
				case Stage.Boost:
					if (Vector3D.DistanceSquared(CubeBlock.GetPosition(), MyEntity.GetPosition()) >= myAmmo.Description.BoostDistance * myAmmo.Description.BoostDistance)
					{
						myLogger.debugLog("completed boost stage, starting mid course stage", "CheckGuidance()", Logger.severity.INFO);
						m_stage = Stage.MidCourse;
					}
					return;
				case Stage.MidCourse:
					Target t = CurrentTarget;
					if (t.Entity == null)
						return;

					double toTarget = Vector3D.Distance(MyEntity.GetPosition(), t.GetPosition());
					double toLaunch = Vector3D.Distance(MyEntity.GetPosition(), CubeBlock.GetPosition());

					if (toTarget < toLaunch)
					{
						myLogger.debugLog("closer to target(" + toTarget + ") than to launch(" + toLaunch + "), starting guidance", "CheckGuidance()", Logger.severity.INFO);
						m_stage = Stage.Guided;
						myGuidanceEnds = DateTime.UtcNow.AddSeconds(myDescr.GuidanceSeconds);
						m_gravData = null;
					}
					return;
				case Stage.SemiActive:
				case Stage.Guided:
					if (DateTime.UtcNow >= myGuidanceEnds)
					{
						myLogger.debugLog("finished guidance", "CheckGuidance()", Logger.severity.INFO);
						m_stage = Stage.Ballistic;
					}
					return;
			}
		}

		/// <summary>
		/// Updates myAntenna and sends LastSeen of this missile to launcher. Runs on game thread.
		/// </summary>
		private void UpdateNetwork()
		{
			if (myAntenna != null)
				myAntenna.Update100();

			if (m_launcherClient == null)
			{
				myLogger.debugLog("No launcher client", "UpdateNetwork()", Logger.severity.WARNING);
				return;
			}

			NetworkStorage store = m_launcherClient.GetStorage();
			if (store == null)
			{
				myLogger.debugLog("Net client does not have a storage", "UpdateNetwork()", Logger.severity.WARNING);
				return;
			}

			//myLogger.debugLog("Updating launcher with location of this missile", "UpdateNetwork()");
			store.Receive(new LastSeen(MyEntity, LastSeen.UpdateTime.None));
		}

		private void UpdateRail()
		{
			MatrixD matrix = CubeBlock.WorldMatrix;
			m_rail.Rail.From = Vector3D.Transform(m_rail.RailStart, matrix);
			m_rail.Rail.To = m_rail.Rail.From + matrix.Forward * 100d;

			Vector3D closest = m_rail.Rail.ClosestPoint(MyEntity.GetPosition());
			//myLogger.debugLog("my position: " + MyEntity.GetPosition() + ", closest point: " + closest + ", distance: " + Vector3D.Distance(MyEntity.GetPosition(), closest), "UpdateRail()");
			//myLogger.debugLog("my forward: " + MyEntity.WorldMatrix.Forward + ", block forward: " + matrix.Forward + ", angle: " + MyEntity.WorldMatrix.Forward.AngleBetween(matrix.Forward), "UpdateRail()");

			matrix.Translation = closest;
			MyEntity.WorldMatrix = matrix;

			float speed = myAmmo.MissileDefinition.MissileInitialSpeed + (float)(DateTime.UtcNow - m_rail.Created).TotalSeconds * myAmmo.MissileDefinition.MissileAcceleration;
			MyEntity.Physics.LinearVelocity = CubeBlock.CubeGrid.Physics.LinearVelocity + matrix.Forward * myAmmo.MissileDefinition.MissileInitialSpeed;
		}

		private void StartGravity()
		{
			Vector3D position = MyEntity.GetPosition();
			List<IMyVoxelBase> allPlanets = ResourcePool<List<IMyVoxelBase>>.Pool.Get();
			MyAPIGateway.Session.VoxelMaps.GetInstances_Safe(allPlanets, voxel => voxel is MyPlanet);

			foreach (MyPlanet planet in allPlanets)
				if (planet.IsPositionInGravityWell(position))
				{
					Vector3D targetPosition = CurrentTarget.GetPosition();
					if (!planet.IsPositionInGravityWell(targetPosition))
					{
						myLogger.debugLog("Target is not in gravity well, target position: " + targetPosition + ", planet: " + planet.getBestName(), "UpdateGravity()", Logger.severity.WARNING);
						return;
					}
					Vector3 gravAtTarget = planet.GetWorldGravityNormalized(ref targetPosition);
					m_gravData = new GravityData(planet, gravAtTarget);
					break;
				}

			allPlanets.Clear();
			ResourcePool<List<IMyVoxelBase>>.Pool.Return(allPlanets);

			if (m_gravData != null)
				UpdateGravity();
		}

		/// <summary>
		/// Updates stored gravity values. Runs on missile thread.
		/// </summary>
		private void UpdateGravity()
		{
			Vector3D position = MyEntity.GetPosition();
			m_gravData.Normal = m_gravData.Planet.GetWorldGravityNormalized(ref position);
			if (m_stage == Stage.Boost)
			{
				float grav = m_gravData.Planet.GetGravityMultiplier(position) * 9.81f;
				m_gravData.AccelPerUpdate = m_gravData.Normal * grav * Globals.UpdateDuration;
			}

			myLogger.debugLog("updated gravity, norm: " + m_gravData.Normal, "UpdateGravity()");
		}

		/// <summary>
		/// Applies gravitational acceleration to the missile. Runs on game thread.
		/// </summary>
		private void ApplyGravity()
		{
			MyEntity.Physics.LinearVelocity += m_gravData.AccelPerUpdate;
		}

	}
}
