using System;
using System.Collections.Generic;
using System.Xml.Serialization;
using Rynchodon.AntennaRelay;
using Rynchodon.Threading;
using Rynchodon.Update;
using Rynchodon.Weapons.SystemDisruption;
using Sandbox.Game.Entities;
using Sandbox.Game.Gui;
using Sandbox.ModAPI;
using VRage;
using VRage.Collections;
using VRage.Game;
using VRage.Game.Entity;
using VRage.ModAPI;
using VRage.ObjectBuilders;
using VRageMath;

namespace Rynchodon.Weapons.Guided
{
	// TODO: periodically sync position & velocity of missile
	public class GuidedMissile : TargetingBase
	{

		[Serializable]
		public class Builder_GuidedMissile
		{
			[XmlAttribute]
			public long Missile;
			public SerializableDefinitionId Ammo;
			public long Launcher, Owner;
			public SerializableGameTime GuidanceEnds;
			public Stage CurrentStage;
			public Cluster.Builder_Cluster Cluster;
		}

		private class RailData
		{
			public readonly Vector3D RailStart;
			public readonly LineSegmentD Rail;
			public readonly TimeSpan Created;

			public RailData(Vector3D start)
			{
				RailStart = start;
				Rail = new LineSegmentD();
				Created = Globals.ElapsedTime;
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

		public enum Stage : byte { Rail, Boost, MidCourse, SemiActive, Guided, Ballistic, Terminated, Exploded }

		private static readonly float Angle_AccelerateWhen = 0.02f;
		private static readonly float Cos_Angle_Detonate = (float)Math.Cos(0.3f);

		private static Logger staticLogger = new Logger("GuidedMissile");
		private static ThreadManager Thread = new ThreadManager();
		private static CachingList<GuidedMissile> AllGuidedMissiles = new CachingList<GuidedMissile>();
		private static FastResourceLock lock_AllGuidedMissiles = new FastResourceLock();

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
		}

		//public static void CreateFromBuilder(Builder_GuidedMissile builder)
		//{
		//	Logger m_logger = new Logger("GuidedMissile");

		//	IMyEntity missile;
		//	if (!MyAPIGateway.Entities.TryGetEntityById(builder.Missile, out missile))
		//	{
		//		m_logger.alwaysLog("Missile is not in world: " + builder.Missile, "CreateFromBuilder()", Logger.severity.WARNING);
		//		return;
		//	}
		//	GuidedMissileLauncher launcher;
		//	if (!Registrar.TryGetValue(builder.Launcher, out launcher))
		//	{
		//		m_logger.alwaysLog("Launcher is not in world: " + builder.Launcher, "CreateFromBuilder()", Logger.severity.WARNING);
		//		return;
		//	}

		//	new GuidedMissile(missile, launcher, builder);
		//}

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

		public static void ForEach(Action<GuidedMissile> invoke)
		{
			using (lock_AllGuidedMissiles.AcquireSharedUsing())
				foreach (GuidedMissile missile in AllGuidedMissiles)
					invoke(missile);
		}

		public static bool TryGetOwnerId(long missileId, out long OwnerId)
		{
			GuidedMissile missile;
			if (!Registrar.TryGetValue(missileId, out missile))
			{
				OwnerId = default(long);
				return false;
			}

			OwnerId = missile.m_owner;
			return true;
		}

		public static bool IsGuidedMissile(long missileId)
		{
			return Registrar.Contains<GuidedMissile>(missileId);
		}

		private readonly Logger myLogger;
		private readonly Ammo myAmmo;
		private readonly RelayNode myAntenna;
		private readonly GuidedMissileLauncher m_launcher;
		private readonly long m_owner;

		private Cluster myCluster;
		private MyEntity myRock;
		private TimeSpan myGuidanceEnds;
		private float addSpeedPerUpdate, acceleration;
		private Stage m_stage;
		private RailData m_rail;
		private GravityData m_gravData;
		private RadarEquipment m_radar;
		private bool m_destroyedNearbyMissiles;

		public bool Stopped
		{ get { return MyEntity.Closed || m_stage >= Stage.Terminated; } }

		public float RadarReflectivity
		{ get { return myDescr.RadarReflectivity; } }

		private Ammo.AmmoDescription myDescr
		{ get { return myAmmo.Description; } }

		private IRelayPart m_launcherRelay
		{ get { return m_launcher.m_relayPart; } }

		/// <summary>
		/// Creates a missile with homing and target finding capabilities.
		/// </summary>
		public GuidedMissile(IMyEntity missile, GuidedMissileLauncher launcher, ref Target initialTarget)
			: base(missile, launcher.CubeBlock)
		{
			myLogger = new Logger("GuidedMissile", () => myAmmo.AmmoDefinition.DisplayNameText, () => missile.getBestName(), () => m_stage.ToString());
			m_launcher = launcher;
			myAmmo = launcher.loadedAmmo;
			m_owner = launcher.CubeBlock.OwnerId;
			if (myAmmo.Description.HasAntenna)
				myAntenna = new RelayNode(missile, () => m_owner, ComponentRadio.CreateRadio(missile, 0f));
			TryHard = true;
			SEAD = myAmmo.Description.SEAD;

			AllGuidedMissiles.Add(this);
			MyEntity.OnClose += MyEntity_OnClose;

			acceleration = myDescr.Acceleration + myAmmo.MissileDefinition.MissileAcceleration;
			addSpeedPerUpdate = myDescr.Acceleration * Globals.UpdateDuration;
			if (!(launcher.CubeBlock is Sandbox.ModAPI.Ingame.IMyLargeTurretBase))
				m_rail = new RailData(Vector3D.Transform(MyEntity.GetPosition(), CubeBlock.WorldMatrixNormalizedInv));

			Options = m_launcher.m_weaponTarget.Options.Clone();
			Options.TargetingRange = myAmmo.Description.TargetRange;

			if (initialTarget != null && initialTarget.Entity != null)
			{
				myLogger.debugLog("using target from launcher: " + initialTarget.Entity.nameWithId());
				CurrentTarget = initialTarget;
			}
			else
			{
				RelayStorage storage = launcher.m_relayPart.GetStorage();
				if (storage == null)
				{
					myLogger.debugLog("failed to get storage for launcher", Logger.severity.WARNING);
				}
				else
				{
					myLogger.debugLog("getting initial target from launcher", Logger.severity.DEBUG);
					GetLastSeenTarget(storage, myAmmo.MissileDefinition.MaxTrajectory);
				}
				initialTarget = CurrentTarget;
			}

			if (myAmmo.RadarDefinition != null)
			{
				myLogger.debugLog("Has a radar definiton");
				m_radar = new RadarEquipment(missile, myAmmo.RadarDefinition, launcher.CubeBlock);
				if (myAntenna == null)
				{
					myLogger.debugLog("Creating node for radar");
					myAntenna = new RelayNode(missile, () => m_owner, null);
				}
			}

			Registrar.Add(missile, this);

			myLogger.debugLog("Options: " + Options + ", initial target: " + (myTarget == null ? "null" : myTarget.Entity.getBestName()));
			//myLogger.debugLog("AmmoDescription: \n" + MyAPIGateway.Utilities.SerializeToXML<Ammo.AmmoDescription>(myDescr), "GuidedMissile()");
		}

		public GuidedMissile(Cluster missiles, GuidedMissileLauncher launcher, ref Target initialTarget)
			: this(missiles.Master, launcher, ref initialTarget)
		{
			myCluster = missiles;
		}

		//private GuidedMissile(IMyEntity missile, GuidedMissileLauncher launcher, Builder_GuidedMissile builder)
		//	: base(missile, launcher.CubeBlock)
		//{
		//	myLogger = new Logger("GuidedMissile", () => missile.getBestName(), () => m_stage.ToString());
		//	m_launcher = launcher;
		//	myAmmo = Ammo.GetAmmo(builder.Ammo);
		//	m_owner = builder.Owner;
		//	myGuidanceEnds = builder.GuidanceEnds.ToTimeSpan();
		//	m_stage = builder.CurrentStage;

		//	if (builder.Cluster != null)
		//		myCluster = new Cluster(missile, builder.Cluster);

		//	if (myAmmo.Description.HasAntenna)
		//		myAntenna = new NetworkNode(missile, () => m_owner, ComponentRadio.CreateRadio(missile, 0f));
		//	TryHard = true;
		//	SEAD = myAmmo.Description.SEAD;

		//	AllGuidedMissiles.Add(this);
		//	MyEntity.OnClose += MyEntity_OnClose;

		//	acceleration = myDescr.Acceleration + myAmmo.MissileDefinition.MissileAcceleration;
		//	addSpeedPerUpdate = myDescr.Acceleration * Globals.UpdateDuration;

		//	Options = m_launcher.m_weaponTarget.Options.Clone();
		//	Options.TargetingRange = myAmmo.Description.TargetRange;

		//	NetworkStorage storage = launcher.m_netClient.GetStorage();
		//	if (storage == null)
		//	{
		//		myLogger.debugLog("failed to get storage for launcher", "GuidedMissile()", Logger.severity.WARNING);
		//	}
		//	else
		//	{
		//		myLogger.debugLog("getting initial target from launcher", "GuidedMissile()", Logger.severity.DEBUG);
		//		GetLastSeenTarget(storage, myAmmo.MissileDefinition.MaxTrajectory);
		//	}

		//	if (myAmmo.RadarDefinition != null)
		//	{
		//		myLogger.debugLog("Has a radar definiton", "GuidedMissile()");
		//		m_radar = new RadarEquipment(missile, myAmmo.RadarDefinition, launcher.CubeBlock);
		//		if (myAntenna == null)
		//		{
		//			myLogger.debugLog("Creating node for radar", "GuidedMissile()");
		//			myAntenna = new NetworkNode(missile, ()=> m_owner, null);
		//		}
		//	}

		//	switch (m_stage)
		//	{
		//		case Stage.Rail:
		//			if (!(launcher.CubeBlock is Sandbox.ModAPI.Ingame.IMyLargeTurretBase))
		//				m_rail = new RailData(Vector3D.Transform(MyEntity.GetPosition(), CubeBlock.WorldMatrixNormalizedInv));
		//			break;
		//		case Stage.Boost:
		//		case Stage.MidCourse:
		//			StartGravity();
		//			break;
		//	}

		//	Registrar.Add(missile, this);
		//	myLogger.debugLog("Created from builder", "GuidedMissile()");
		//	myLogger.debugLog("Options: " + Options + ", initial target: " + (myTarget == null ? "null" : myTarget.Entity.getBestName()), "GuidedMissile()");
		//}

		private void MyEntity_OnClose(IMyEntity obj)
		{
			m_stage = Stage.Exploded;

			if (AllGuidedMissiles == null)
				return;

			myLogger.debugLog("on close");

			AllGuidedMissiles.Remove(this);
			if (!MyAPIGateway.Multiplayer.IsServer)
				return;

			RemoveRock();

			DestroyAllNearbyMissiles();

			if (myDescr.EMP_Seconds > 0f && myDescr.EMP_Strength > 0f)
			{
				myLogger.debugLog("Creating EMP effect. EMP_Seconds: " + myDescr.EMP_Seconds + ", EMP_Strength: " + myDescr.EMP_Strength);
				BoundingSphereD empSphere = new BoundingSphereD(ProjectilePosition(), myAmmo.MissileDefinition.MissileExplosionRadius);
				EMP.ApplyEMP(empSphere, myDescr.EMP_Strength, TimeSpan.FromSeconds(myDescr.EMP_Seconds));
			}
		}

		protected override bool CanRotateTo(Vector3D targetPos)
		{
			if (myDescr.AcquisitionAngle == MathHelper.Pi)
				return true;

			Vector3 displacement = targetPos - MyEntity.GetPosition();
			displacement.Normalize();
			//myLogger.debugLog("forwardness: " + Vector3.Dot(displacement, MyEntity.WorldMatrix.Forward) + ", CosCanRotateArc: " + myDescr.CosCanRotateArc);
			return Vector3.Dot(displacement, MyEntity.WorldMatrix.Forward) > myDescr.CosAcquisitionAngle;
		}

		protected override bool myTarget_CanRotateTo(Vector3D targetPos)
		{
			return true;
		}

		protected override bool Obstructed(Vector3D targetPos, IMyEntity target)
		{
			return false;
		}

		protected override float ProjectileSpeed(ref Vector3D targetPos)
		{
			return acceleration;
		}

		private void TargetSemiActive()
		{
			SemiActiveTarget sat = myTarget as SemiActiveTarget;
			if (sat == null)
			{
				myLogger.debugLog("creating semi-active target", Logger.severity.DEBUG);
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

			//myLogger.debugLog("target: " + cached.Entity.getBestName() + ", ContactPoint: " + cached.ContactPoint);

			myLogger.debugLog(!cached.FiringDirection.IsValid(), "FiringDirection invalid: " + cached.FiringDirection, Logger.severity.FATAL);
			myLogger.debugLog(!cached.ContactPoint.IsValid(), "ContactPoint invalid: " + cached.ContactPoint, Logger.severity.FATAL);

			Vector3 targetDirection;

			switch (m_stage)
			{
				case Stage.Boost:
					myLogger.debugLog(m_gravData == null, "m_gravData == null", Logger.severity.FATAL);
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
			float angle = (float)Math.Acos(Vector3.Dot(forward, targetDirection));

			//myLogger.debugLog("forward: " + forward + ", targetDirection: " + targetDirection + ", angle: " + angle);

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
				});
			}

			//myLogger.debugLog("targetDirection: " + targetDirection + ", forward: " + forward);

			{ // accelerate if facing target
				if (angle < Angle_AccelerateWhen && addSpeedPerUpdate > 0f && MyEntity.GetLinearVelocity().LengthSquared() < myAmmo.AmmoDefinition.DesiredSpeed * myAmmo.AmmoDefinition.DesiredSpeed)
				{
					//myLogger.debugLog("accelerate. angle: " + angle, "Update()");
					MyAPIGateway.Utilities.TryInvokeOnGameThread(() => {
						if (!Stopped)
							MyEntity.Physics.LinearVelocity += MyEntity.WorldMatrix.Forward * addSpeedPerUpdate;
					});
				}
			}

			if (!MyAPIGateway.Multiplayer.IsServer)
				return;

			if (myDescr.DetonateRange > 0f)
			{ // detonate missile before it hits anything to increase the damage
				Vector3D position = MyEntity.GetPosition();
				Vector3D nextPosition = position + MyEntity.Physics.LinearVelocity * Globals.UpdateDuration * 5f;
				Vector3D contact = Vector3D.Zero;
				if (MyHudCrosshair.GetTarget(position, nextPosition, ref contact))
				{
					// there are a few false positives, perform a second test
					if (RayCast.Obstructed(new LineD(position, nextPosition), new IMyEntity[] { MyEntity }, checkVoxel: false))
					{
						myLogger.debugLog("detonating in front of entity at " + contact);
						DestroyAllNearbyMissiles();
						MyAPIGateway.Utilities.TryInvokeOnGameThread(() => {
							if (Vector3D.DistanceSquared(position, contact) > 1f)
							{
								MyEntity.SetPosition(contact - Vector3.Normalize(MyEntity.Physics.LinearVelocity));
								myLogger.debugLog("moved entity from " + position + " to " + MyEntity.GetPosition());
							}
							Explode();
						});
						m_stage = Stage.Terminated;
						return;
					}
					else
						myLogger.debugLog("failed obstructed test, maybe ray hit this entity. missile position: " + MyEntity.GetPosition() + ", contact: " + contact);
				}
			}

			{ // detonate when barely missing the target
				if (MyAPIGateway.Multiplayer.IsServer &&
					Vector3.DistanceSquared(MyEntity.GetPosition(), cached.GetPosition()) <= myDescr.DetonateRange * myDescr.DetonateRange &&
					Vector3.Normalize(MyEntity.GetLinearVelocity()).Dot(targetDirection) < Cos_Angle_Detonate)
				{
					myLogger.debugLog("proximity detonation");
					DestroyAllNearbyMissiles();
					MyAPIGateway.Utilities.TryInvokeOnGameThread(Explode);
					m_stage = Stage.Terminated;
					return;
				}
			}
		}

		/// <remarks>
		/// Runs on separate thread.
		/// </remarks>
		private void UpdateCluster()
		{
			const float moveSpeed = 3f;
			const float moveUpdateSq = moveSpeed * moveSpeed * Globals.UpdateDuration * Globals.UpdateDuration;
			const float maxVelChangeSq = 10000f * 10000f * Globals.UpdateDuration * Globals.UpdateDuration;

			Vector3[] slaveVelocity = new Vector3[myCluster.Slaves.Count];
			if (myTarget.Entity != null)
				myCluster.AdjustMulti(myTarget.Entity.LocalAABB.GetLongestDim() * 0.5f);

			MatrixD masterMatrix = MyEntity.WorldMatrix;

			for (int i = 0; i < slaveVelocity.Length; i++)
			{
				Vector3D slavePos = myCluster.Slaves[i].GetPosition();
				Vector3D offset = myCluster.SlaveOffsets[i] * myCluster.OffsetMulti;
				Vector3D destination;
				Vector3D.Transform(ref offset, ref masterMatrix, out destination);
				double distSquared = Vector3D.DistanceSquared(slavePos, destination);

				if (distSquared >= moveUpdateSq)
				{
					slaveVelocity[i] = (destination - slavePos) / (float)Math.Sqrt(distSquared) * moveSpeed;
					//myLogger.debugLog("slave: " + i + ", pos: " + slavePos + ", destination: " + destination + ", dist: " + ((float)Math.Sqrt(distSquared)) + ", velocity: " + slaveVelocity[i], "UpdateCluster()");
				}
				else
					slaveVelocity[i] = Vector3.Zero;
			}

			MyAPIGateway.Utilities.TryInvokeOnGameThread(() => {
				if (Stopped)
					return;

				// when master hits a target, before it explodes, there are a few frames with strange velocity
				Vector3 masterVelocity = myCluster.Master.Physics.LinearVelocity;
				float distSq;
				Vector3.DistanceSquared(ref masterVelocity, ref myCluster.masterVelocity, out distSq);
				if (distSq > maxVelChangeSq)
				{
					myLogger.debugLog("massive change in master velocity, terminating", Logger.severity.INFO);
					m_stage = Stage.Terminated;
					return;
				}
				myCluster.masterVelocity = masterVelocity;

				myLogger.debugLog(myCluster == null, "myCluster == null", Logger.severity.FATAL);
				MatrixD worldMatrix = MyEntity.WorldMatrix;

				for (int i = 0; i < myCluster.Slaves.Count; i++)
				{
					if (myCluster.Slaves[i].Closed)
						continue;
					worldMatrix.Translation = myCluster.Slaves[i].GetPosition();
					myCluster.Slaves[i].WorldMatrix = worldMatrix;
					myCluster.Slaves[i].Physics.LinearVelocity = MyEntity.Physics.LinearVelocity + slaveVelocity[i];
					//myLogger.debugLog("slave: " + i + ", linear velocity: " + myCluster.Slaves[i].Physics.LinearVelocity, "UpdateCluster()");
				}

			});
		}

		/// <summary>
		/// Only call from game thread! Spawns a rock to explode the missile.
		/// </summary>
		private void Explode()
		{
			myLogger.debugLog(!MyAPIGateway.Multiplayer.IsServer, "Not server!", Logger.severity.FATAL);

			if (MyEntity.Closed || m_stage == Stage.Exploded)
				return;
			m_stage = Stage.Exploded;

			MyEntity.Physics.LinearVelocity = Vector3.Zero;

			RemoveRock();

			MyObjectBuilder_InventoryItem item = new MyObjectBuilder_InventoryItem() { Amount = 1, PhysicalContent = new MyObjectBuilder_Ore() { SubtypeName = "Stone" } };

			MyObjectBuilder_FloatingObject rockBuilder = new MyObjectBuilder_FloatingObject();
			rockBuilder.Item = item;
			rockBuilder.PersistentFlags = MyPersistentEntityFlags2.InScene;
			rockBuilder.PositionAndOrientation = new MyPositionAndOrientation()
			{
				Position = MyEntity.GetPosition(),
				Forward = Vector3.Forward,
				Up = Vector3.Up
			};

			myRock = MyEntities.CreateFromObjectBuilderAndAdd(rockBuilder);
			if (myRock == null)
			{
				myLogger.alwaysLog("failed to create rock, builder:\n" + MyAPIGateway.Utilities.SerializeToXML(rockBuilder), Logger.severity.ERROR);
				return;
			}
			myLogger.debugLog("created rock at " + myRock.PositionComp.GetPosition() + ", " + myRock.getBestName());
		}

		/// <summary>
		/// Only call from game thread! Remove the rock created by Explode().
		/// </summary>
		private void RemoveRock()
		{
			if (myRock == null || myRock.Closed || !MyAPIGateway.Multiplayer.IsServer)
				return;

			//myLogger.debugLog("removing rock", "RemoveRock()");
			myRock.Delete();
		}

		/// <summary>
		/// Destroys missiles in blast radius, safe to call from any thread.
		/// </summary>
		private void DestroyAllNearbyMissiles()
		{
			if (myCluster != null || m_destroyedNearbyMissiles)
				return;
			m_destroyedNearbyMissiles = true;

			BoundingSphereD explosion = new BoundingSphereD(MyEntity.GetPosition(), myAmmo.MissileDefinition.MissileExplosionRadius);
			List<MyEntity> entitiesInExplosion = new List<MyEntity>();
			MyGamePruningStructure.GetAllTopMostEntitiesInSphere(ref explosion, entitiesInExplosion, MyEntityQueryType.Dynamic);

			MyAPIGateway.Utilities.TryInvokeOnGameThread(() => {
				foreach (MyEntity entity in entitiesInExplosion)
					if (!entity.Closed && entity.IsMissile() && entity != MyEntity)
					{
						GuidedMissile hit;
						if (Registrar.TryGetValue(entity, out hit))
							hit.Explode();
						else
							entity.Delete();
					}
			});
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
							myGuidanceEnds = Globals.ElapsedTime.Add(TimeSpan.FromSeconds(myDescr.GuidanceSeconds));
							myLogger.debugLog("past arming range, semi-active.", Logger.severity.INFO);
							m_stage = Stage.SemiActive;
							return;
						}

						if (myAmmo.Description.BoostDistance > 1f)
						{
							myLogger.debugLog("past arming range, starting boost stage", Logger.severity.INFO);
							StartGravity();
							m_stage = Stage.Boost;
							if (m_gravData == null)
							{
								myLogger.debugLog("no gravity, terminating", Logger.severity.WARNING);
								m_stage = Stage.Terminated;
							}
						}
						else
						{
							myGuidanceEnds = Globals.ElapsedTime.Add(TimeSpan.FromSeconds(myDescr.GuidanceSeconds));
							myLogger.debugLog("past arming range, starting guidance.", Logger.severity.INFO);
							m_stage = Stage.Guided;
						}
					}
					return;
				case Stage.Boost:
					if (Vector3D.DistanceSquared(CubeBlock.GetPosition(), MyEntity.GetPosition()) >= myAmmo.Description.BoostDistance * myAmmo.Description.BoostDistance)
					{
						myLogger.debugLog("completed boost stage, starting mid course stage", Logger.severity.INFO);
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
						myLogger.debugLog("closer to target(" + toTarget + ") than to launch(" + toLaunch + "), starting guidance", Logger.severity.INFO);
						m_stage = Stage.Guided;
						myGuidanceEnds = Globals.ElapsedTime.Add(TimeSpan.FromSeconds(myDescr.GuidanceSeconds));
						m_gravData = null;
					}
					return;
				case Stage.SemiActive:
				case Stage.Guided:
					if (Globals.ElapsedTime >= myGuidanceEnds)
					{
						myLogger.debugLog("finished guidance", Logger.severity.INFO);
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

			if (m_launcherRelay == null)
			{
				myLogger.debugLog("No launcher client", Logger.severity.WARNING);
				return;
			}

			RelayStorage store = m_launcherRelay.GetStorage();
			if (store == null)
			{
				myLogger.debugLog("Launcher's net client does not have a storage", Logger.severity.WARNING);
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

			float speed = myAmmo.MissileDefinition.MissileInitialSpeed + (float)(Globals.ElapsedTime - m_rail.Created).TotalSeconds * myAmmo.MissileDefinition.MissileAcceleration;
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
						myLogger.debugLog("Target is not in gravity well, target position: " + targetPosition + ", planet: " + planet.getBestName(), Logger.severity.WARNING);
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

			//myLogger.debugLog("updated gravity, norm: " + m_gravData.Normal, "UpdateGravity()");
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
