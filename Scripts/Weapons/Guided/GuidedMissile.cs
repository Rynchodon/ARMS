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
	 * ARH, SA*
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

		private enum Stage : byte { Rail, Boost, MidCourse, Guided, Ballistic, Terminated }

		private const float Angle_AccelerateWhen = 0.02f;
		private const float Angle_Detonate = 0.1f;
		private const float Angle_Cluster = 0.05f;
		private static readonly TimeSpan checkLastSeen = new TimeSpan(0, 0, 10);

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
						if (missile.CurrentTarget.TType == TargetType.None)
							return;
						if (missile.m_stage == Stage.MidCourse || missile.m_stage == Stage.Guided)
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
						if (missile.m_stage == Stage.Guided && missile.myDescr.TargetRange > 1f)
							missile.UpdateTarget();
						if (missile.CurrentTarget.TType == TargetType.None || missile.CurrentTarget is LastSeenTarget)
							missile.TargetLastSeen();
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
		private readonly Ammo.AmmoDescription myDescr;
		private readonly NetworkNode myAntenna;
		private readonly NetworkClient m_launcherClient;

		private LastSeen myTargetSeen;
		private Cluster myCluster;
		private IMyEntity myRock;
		private DateTime failed_lastSeenTarget;
		private DateTime myGuidanceEnds;
		private float addSpeedPerUpdate, acceleration;
		private Stage m_stage;
		private RailData m_rail;
		private GravityData m_gravData;

		private bool Stopped
		{ get { return MyEntity.Closed || m_stage == Stage.Terminated; } }

		/// <summary>
		/// Creates a missile with homing and target finding capabilities.
		/// </summary>
		public GuidedMissile(IMyEntity missile, IMyCubeBlock firedBy, TargetingOptions opt, Ammo ammo, LastSeen initialTarget, NetworkClient launcherClient)
			: base(missile, firedBy)
		{
			myLogger = new Logger("GuidedMissile", () => missile.getBestName(), () => m_stage.ToString());
			myAmmo = ammo;
			myDescr = ammo.Description;
			if (ammo.Description.HasAntenna)
				myAntenna = new NetworkNode(missile, firedBy, ComponentRadio.CreateRadio(missile, 0f));
			m_launcherClient = launcherClient;
			TryHard = true;

			AllGuidedMissiles.Add(this);
			AddMissileOwner(MyEntity, CubeBlock.OwnerId);
			MyEntity.OnClose += MyEntity_OnClose;

			acceleration = myDescr.Acceleration + myAmmo.MissileDefinition.MissileAcceleration;
			addSpeedPerUpdate = myDescr.Acceleration * Globals.UpdateDuration;
			if (!(firedBy is Sandbox.ModAPI.Ingame.IMyLargeTurretBase))
				m_rail = new RailData(Vector3D.Transform(MyEntity.GetPosition(), CubeBlock.WorldMatrixNormalizedInv));

			Options = opt;
			Options.TargetingRange = ammo.Description.TargetRange;
			myTargetSeen = initialTarget;

			myLogger.debugLog("Options: " + Options + ", initial target: " + (initialTarget == null ? "null" : initialTarget.Entity.getBestName()), "GuidedMissile()");
			//myLogger.debugLog("AmmoDescription: \n" + MyAPIGateway.Utilities.SerializeToXML<Ammo.AmmoDescription>(myDescr), "GuidedMissile()");
		}

		public GuidedMissile(Cluster missiles, IMyCubeBlock firedBy, TargetingOptions opt, Ammo ammo, LastSeen initialTarget, NetworkClient launcherClient)
			: this(missiles.Master, firedBy, opt, ammo, initialTarget, launcherClient)
		{
			myCluster = missiles;
			m_rail = null;
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

		/// <remarks>
		/// Runs on separate thread.
		/// </remarks>
		private void TargetLastSeen()
		{
			NetworkStorage store = myAntenna == null ? null : myAntenna.Storage;

			if (store == null || store.LastSeenCount == 0)
			{
				if (myTargetSeen != null && myTarget.TType == TargetType.None)
				{
					myLogger.debugLog("Retargeting last: " + myTargetSeen.Entity.getBestName() + " at " + myTargetSeen.GetPosition(), "TargetLastSeen()");
					myTarget = new LastSeenTarget(myTargetSeen);
					SetFiringDirection();
				}
				return;
			}

			if (DateTime.UtcNow - failed_lastSeenTarget < checkLastSeen)
				return;

			LastSeen fetched;
			if (myTargetSeen != null && store.TryGetLastSeen(myTargetSeen.Entity.EntityId, out fetched) && fetched.isRecent())
			{
				myLogger.debugLog("using previous last seen: " + fetched.Entity.getBestName() + " at " + fetched.GetPosition(), "TargetLastSeen()");
				myTarget = new LastSeenTarget(fetched);
				SetFiringDirection();
				return;
			}

			if (Options.TargetEntityId.HasValue)
			{
				if (store.TryGetLastSeen(Options.TargetEntityId.Value, out fetched))
				{
					myLogger.debugLog("using last seen from entity id: " + fetched.Entity.getBestName() + " at " + fetched.GetPosition(), "TargetLastSeen()");
					myTarget = new LastSeenTarget(fetched);
					SetFiringDirection();
				}
				else
					myLogger.debugLog("failed to get last seen from entity id", "TargetLastSeen()");
				return;
			}

			Vector3D myPos = MyEntity.GetPosition();
			LastSeen closest = null;
			double closestDist = double.MaxValue;

			myLogger.debugLog("last seen count: " + store.LastSeenCount, "TargetLastSeen()");
			store.ForEachLastSeen(seen => {
				myLogger.debugLog("checking: " + seen.Entity.getBestName(), "TargetLastSeen()");
				if (seen.isRecent() && CubeBlock.canConsiderHostile(seen.Entity) && Options.CanTargetType(seen.Entity))
				{
					double dist = Vector3D.DistanceSquared(myPos, seen.LastKnownPosition);
					if (dist < closestDist)
					{
						closestDist = dist;
						closest = seen;
					}
				}
			});

			if (closest == null)
			{
				myLogger.debugLog("failed to get a target from last seen", "TargetLastSeen()");
				failed_lastSeenTarget = DateTime.UtcNow;
				myTargetSeen = null;
			}
			else
			{
				myLogger.debugLog("got a target from last seen: " + closest.Entity.getBestName() + " at " + closest.GetPosition(), "TargetLastSeen()");
				myTarget = new LastSeenTarget(closest);
				SetFiringDirection();
				myTargetSeen = closest;
			}
		}

		/// <remarks>
		/// Runs on separate thread.
		/// </remarks>
		private void Update()
		{
			if (m_stage == Stage.Rail)
				return;

			Target cached = CurrentTarget;
			if (!cached.FiringDirection.HasValue)
				return;

			//myLogger.debugLog("target: " + cached.Entity.getBestName() + ", ContactPoint: " + cached.ContactPoint, "Update()");

			Vector3 forward = MyEntity.WorldMatrix.Forward;

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
				case Stage.Guided:
				default:
					targetDirection = cached.FiringDirection.Value;
					break;
			}

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
			//myLogger.debugLog("updating cluster", "UpdateCluster()");

			MatrixD[] partWorldMatrix = new MatrixD[myCluster.Slaves.Count];
			float moveBy = MyEntity.Physics.LinearVelocity.Length() * 2f * Globals.UpdateDuration;
			float moveBySq = moveBy * moveBy;
			if (myTarget.Entity != null)
				myCluster.AdjustMulti(myTarget.Entity.LocalAABB.GetShortestDim() * 0.5f);

			for (int i = 0; i < partWorldMatrix.Length; i++)
			{
				partWorldMatrix[i] = MyEntity.WorldMatrix;
				Vector3D slavePos = myCluster.Slaves[i].GetPosition();
				myLogger.debugLog("cluster offset: " + myCluster.SlaveOffsets[i] + ", scaled: " + myCluster.SlaveOffsets[i] * myCluster.OffsetMulti, "UpdateCluster()");
				myLogger.debugLog("world matrix: " + MyEntity.WorldMatrix, "UpdateCluster()");
				Vector3D destination = Vector3.Transform(myCluster.SlaveOffsets[i] * myCluster.OffsetMulti, MyEntity.WorldMatrix);
				double distSquared = Vector3D.DistanceSquared(slavePos, destination);
				myLogger.debugLog("slave pos: " + slavePos + ", destination: " + destination + ", dist squared: " + distSquared + ", move by squared: " + moveBySq, "UpdateCluster()");
				if (distSquared > moveBySq)
				{
					myLogger.debugLog("Slave: " + i + ", far from position, distance: " + Math.Sqrt(distSquared) + ", moving: " + moveBy, "UpdateCluster()");
					Vector3D direction = (destination - slavePos) / (float)Math.Sqrt(distSquared);
					partWorldMatrix[i].Translation = slavePos + direction * moveBy;
				}
				else
				{
					myLogger.debugLog("Slave: " + i + ", at destination", "UpdateCluster()");
					partWorldMatrix[i].Translation = destination;
				}
			}

			MyAPIGateway.Utilities.TryInvokeOnGameThread(() => {
				if (Stopped || myCluster == null)
					return;

				int index = 0;
				foreach (IMyEntity missile in myCluster.Slaves)
				{
					if (missile.Closed || index >= partWorldMatrix.Length)
						return;
					missile.WorldMatrix = partWorldMatrix[index++];
					missile.Physics.LinearVelocity = MyEntity.Physics.LinearVelocity;
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
							myLogger.debugLog("past arming range, starting guidance. Guidance until" + myGuidanceEnds, "CheckGuidance()", Logger.severity.INFO);
							m_stage = Stage.Guided;
						}
						m_rail = null;
					}
					break;
				case Stage.Boost:
					if (Vector3D.DistanceSquared(CubeBlock.GetPosition(), MyEntity.GetPosition()) >= myAmmo.Description.BoostDistance * myAmmo.Description.BoostDistance)
					{
						myLogger.debugLog("completed boost stage, starting mid course stage", "CheckGuidance()", Logger.severity.INFO);
						m_stage = Stage.MidCourse;
					}
					break;
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
					break;
				case Stage.Guided:
					if (DateTime.UtcNow >= myGuidanceEnds)
					{
						myLogger.debugLog("finished guidance", "CheckGuidance()", Logger.severity.INFO);
						m_stage = Stage.Ballistic;
					}
					break;
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
