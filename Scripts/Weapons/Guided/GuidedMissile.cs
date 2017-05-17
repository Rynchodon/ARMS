using System;
using System.Collections.Generic;
using Rynchodon.AntennaRelay;
using Rynchodon.Utility;
using Rynchodon.Utility.Network;
using Rynchodon.Weapons.SystemDisruption;
using Sandbox.Game.Entities;
using Sandbox.Game.Weapons;
using Sandbox.ModAPI;
using VRage.Collections;
using VRage.Game.Entity;
using VRage.ModAPI;
using VRageMath;

namespace Rynchodon.Weapons.Guided
{
	public sealed class GuidedMissile : TargetingBase
	{

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

		public enum Stage : byte { Rail, Boost, MidCourse, SemiActive, Golis, Guided, Ballistic, Terminated, Exploded }

		private class StaticVariables
		{
			public readonly float Angle_AccelerateWhen = 0.02f;
			public readonly float Cos_Angle_Detonate = (float)Math.Cos(0.3f);

			public CachingList<GuidedMissile> AllGuidedMissiles = new CachingList<GuidedMissile>();
			public List<byte> SerialPositions;
		}

		private static StaticVariables value_static;
		private static StaticVariables Static
		{
			get
			{
				if (Globals.WorldClosed)
					throw new Exception("World closed");
				if (value_static == null)
					value_static = new StaticVariables();
				return value_static;
			}
			set { value_static = value; }
		}

		[OnWorldClose]
		private static void Unload()
		{
			Static = null;
		}

		static GuidedMissile()
		{
			MessageHandler.AddHandler(MessageHandler.SubMod.GuidedMissile, ReceiveMissilePositions);
		}

		public static void Update1()
		{
#if PROFILE
			Profiler.StartProfileBlock();
			try
			{
#endif

			Static.AllGuidedMissiles.ApplyChanges();
			if (Static.AllGuidedMissiles.Count == 0)
				return;

			foreach (GuidedMissile missile in Static.AllGuidedMissiles)
			{
				try
				{
					if (missile.Stopped)
						continue;
					if (missile.myCluster != null)
						missile.UpdateCluster();
					if (missile.CurrentTarget.TType == TargetType.None)
						continue;
					if (missile.m_stage >= Stage.MidCourse && missile.m_stage <= Stage.Guided)
						missile.SetFiringDirection();
					missile.Update();
					if (missile.m_rail != null)
						missile.UpdateRail();
					if (missile.m_stage == Stage.Boost)
						missile.ApplyGravity();
				}
				catch (Exception ex)
				{
					Logger.AlwaysLog("Exception with missile: " + missile.MyEntity + ":\n" + ex);
					Static.AllGuidedMissiles.Remove(missile);
				}
			}

#if PROFILE
			}
			finally
			{
				Profiler.EndProfileBlock();
			}
#endif
		}

		public static void Update10()
		{
#if PROFILE
			Profiler.StartProfileBlock();
			try
			{
#endif

			if (Static.AllGuidedMissiles.Count == 0)
				return;

			foreach (GuidedMissile missile in Static.AllGuidedMissiles)
			{
				try
				{
					if (missile.Stopped)
						continue;
					if (missile.m_stage == Stage.SemiActive)
						missile.TargetSemiActive();
					else if (missile.m_stage != Stage.Golis)
					{
						if (missile.m_stage == Stage.Guided && missile.myDescr.TargetRange > 1f)
							missile.UpdateTarget();
						if ((missile.CurrentTarget.TType == TargetType.None || missile.CurrentTarget is LastSeenTarget) && missile.myAntenna != null)
							missile.GetLastSeenTarget(missile.myAntenna.Storage, missile.myAmmo.MissileDefinition.MaxTrajectory);
					}
					missile.CheckGuidance();
				}
				catch (Exception ex)
				{
					Logger.AlwaysLog("Exception with missile: " + missile.MyEntity + ":\n" + ex);
					Static.AllGuidedMissiles.Remove(missile);
				}
			}

#if PROFILE
			}
			finally
			{
				Profiler.EndProfileBlock();
			}
#endif
		}

		public static void Update100()
		{
#if PROFILE
			Profiler.StartProfileBlock();
			try
			{
#endif

			if (Static.AllGuidedMissiles.Count == 0)
				return;

			if (MyAPIGateway.Multiplayer.IsServer)
			{
				if (Static.SerialPositions == null)
					Static.SerialPositions = new List<byte>();
				else
					Static.SerialPositions.Clear();
				ByteConverter.AppendBytes(Static.SerialPositions, MessageHandler.SubMod.GuidedMissile);
			}

			for (int index = Static.AllGuidedMissiles.Count - 1; index >= 0; index--)
			{
				GuidedMissile missile = Static.AllGuidedMissiles[index];

				try
				{
					if (!missile.Stopped)
					{
						missile.ClearBlacklist();
						if (missile.m_gravData != null)
							missile.UpdateGravity();
					}
					if (!missile.Stopped)
						missile.UpdateNetwork();

					if (MyAPIGateway.Multiplayer.IsServer && Static.SerialPositions.Count < 4000) // message fails at 4096
					{
						ByteConverter.AppendBytes(Static.SerialPositions, missile.MyEntity.PositionComp.GetPosition());
						ByteConverter.AppendBytes(Static.SerialPositions, missile.MyEntity.Physics.LinearVelocity);
					}
				}
				catch (Exception ex)
				{
					Logger.AlwaysLog("Exception with missile: " + missile.MyEntity + ":\n" + ex);
					Static.AllGuidedMissiles.Remove(missile);
				}
			}

			if (MyAPIGateway.Multiplayer.IsServer)
				if (!MyAPIGateway.Multiplayer.SendMessageToOthers(MessageHandler.ModId, Static.SerialPositions.ToArray(), false))
					Logger.AlwaysLog("Missile sync failed, too many missiles in play: " + Static.AllGuidedMissiles.Count + ", byte count: " + Static.SerialPositions.Count);

#if PROFILE
			}
			finally
			{
				Profiler.EndProfileBlock();
			}
#endif
		}

		private static void ReceiveMissilePositions(byte[] data, int pos)
		{
			if (MyAPIGateway.Multiplayer.IsServer)
				return;

			int index = Static.AllGuidedMissiles.Count - 1;
			bool updated = false;
			while (pos < data.Length)
			{
				Vector3D worldPos = ByteConverter.GetVector3D(data, ref pos);
				Vector3 velocity = ByteConverter.GetVector3(data, ref pos);

				while (true)
				{
					if (index < 0)
					{
						if (!updated)
							// it could be that server only has missiles that were generated before the client connects and client only has missiles that were generated after server sent message
							Logger.AlwaysLog("Failed to update missile positions. Local count: " + Static.AllGuidedMissiles.Count + ", bytes received: " + data.Length, Logger.severity.INFO);
						else
							// normal when client connects while missiles are in play
							Logger.DebugLog("Server has more missiles than client. Local count: " + Static.AllGuidedMissiles.Count + ", bytes received: " + data.Length, Logger.severity.INFO);
						return;
					}

					GuidedMissile missile = Static.AllGuidedMissiles[index--];

					// it is possible that the client has extra missiles
					Vector3D currentPosition = missile.MyEntity.PositionComp.GetPosition();
					if (Vector3D.DistanceSquared(worldPos, currentPosition) > 100d)
					{
						if (updated)
							Logger.AlwaysLog("Interruption in missile position updates, it is likely that threshold needs to be adjusted. " +
								"Local count: " + Static.AllGuidedMissiles.Count + ", bytes received: " + data.Length + ", distance squared: " + Vector3D.DistanceSquared(worldPos, currentPosition), Logger.severity.WARNING);
						else
							Logger.DebugLog("Position values are far apart, trying next missile");
						continue;
					}

					updated = true;
					Logger.DebugLog("Update position of " + missile.MyEntity.EntityId + " from " + missile.MyEntity.PositionComp.GetPosition() + " to " + worldPos);
					missile.MyEntity.SetPosition(worldPos);
					missile.MyEntity.Physics.LinearVelocity = velocity;
					break;
				}
			}
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

		private readonly Ammo myAmmo;
		private readonly RelayNode myAntenna;
		private readonly GuidedMissileLauncher m_launcher;
		private readonly long m_owner;

		private Cluster myCluster;
		private TimeSpan myGuidanceEnds;
		private float addSpeedPerUpdate, acceleration;
		private IMyEntity m_entity;
		private Stage m_stage;
		private RailData m_rail;
		private GravityData m_gravData;
		private RadarEquipment m_radar;
		private bool m_destroyedNearbyMissiles;

		public int ClusterCount
		{
			get { return myCluster == null ? 1 : myCluster.Slaves.Count + 1; }
		}

		public bool Stopped
		{ get { return MyEntity.Closed || m_stage >= Stage.Terminated; } }

		private Ammo.AmmoDescription myDescr
		{ get { return myAmmo.Description; } }

		private IRelayPart m_launcherRelay
		{ get { return m_launcher.m_relayPart; } }

		private Logable Log
		{ get { return new Logable(myAmmo.AmmoDefinition.DisplayNameText, m_entity.getBestName(), m_stage.ToString()); } }

		/// <summary>
		/// Creates a missile with homing and target finding capabilities.
		/// </summary>
		public GuidedMissile(IMyEntity missile, GuidedMissileLauncher launcher, Target initialTarget)
			: base(missile, launcher.CubeBlock)
		{
			m_launcher = launcher;
			myAmmo = launcher.loadedAmmo;
			m_entity = missile;
			m_owner = launcher.CubeBlock.OwnerId;
			if (myAmmo.Description.HasAntenna)
				myAntenna = new RelayNode(missile, () => m_owner, ComponentRadio.CreateRadio(missile, 0f));
			TryHard = true;
			SEAD = myAmmo.Description.SEAD;

			Static.AllGuidedMissiles.Add(this);
			Registrar.Add(MyEntity, this);
			MyEntity.OnClose += MyEntity_OnClose;

			acceleration = myDescr.Acceleration + myAmmo.MissileDefinition.MissileAcceleration;
			addSpeedPerUpdate = myDescr.Acceleration * Globals.UpdateDuration;
			if (!(launcher.CubeBlock is Sandbox.ModAPI.Ingame.IMyLargeTurretBase))
				m_rail = new RailData(Vector3D.Transform(MyEntity.GetPosition(), CubeBlock.WorldMatrixNormalizedInv));

			Options = m_launcher.m_weaponTarget.Options.Clone();
			Options.TargetingRange = myAmmo.Description.TargetRange;

			SetTarget(initialTarget);

			if (myAmmo.RadarDefinition != null)
			{
				Log.DebugLog("Has a radar definiton");
				m_radar = new RadarEquipment(missile, myAmmo.RadarDefinition, launcher.CubeBlock);
				if (myAntenna == null)
				{
					Log.DebugLog("Creating node for radar");
					myAntenna = new RelayNode(missile, () => m_owner, null);
				}
			}

			Log.DebugLog("Options: " + Options + ", initial target: " + (CurrentTarget == null ? "null" : CurrentTarget.Entity.getBestName()) + ", entityId: " + missile.EntityId);
			//Log.DebugLog("AmmoDescription: \n" + MyAPIGateway.Utilities.SerializeToXML<Ammo.AmmoDescription>(myDescr), "GuidedMissile()");
		}

		public GuidedMissile(Cluster missiles, GuidedMissileLauncher launcher, Target initialTarget)
			: this(missiles.Master, launcher, initialTarget)
		{
			myCluster = missiles;
		}

		private void MyEntity_OnClose(IMyEntity obj)
		{
			m_stage = Stage.Exploded;

			if (Globals.WorldClosed)
				return;

			Log.DebugLog("on close");

			Static.AllGuidedMissiles.Remove(this);
			if (!MyAPIGateway.Multiplayer.IsServer)
				return;

			if (myDescr.EMP_Seconds > 0f && myDescr.EMP_Strength > 0f)
			{
				Log.DebugLog("Creating EMP effect. EMP_Seconds: " + myDescr.EMP_Seconds + ", EMP_Strength: " + myDescr.EMP_Strength);
				BoundingSphereD empSphere = new BoundingSphereD(ProjectilePosition(), myAmmo.MissileDefinition.MissileExplosionRadius);
				EMP.ApplyEMP(empSphere, myDescr.EMP_Strength, TimeSpan.FromSeconds(myDescr.EMP_Seconds));
			}
		}

		protected override bool CanRotateTo(ref Vector3D targetPos, IMyEntity target)
		{
			if (myDescr.AcquisitionAngle == MathHelper.Pi)
				return true;

			Vector3 displacement = targetPos - MyEntity.GetPosition();
			displacement.Normalize();
			Vector3 direction = Vector3.Normalize(MyEntity.Physics.LinearVelocity);
			bool canRotate = Vector3D.Dot(displacement, direction) > myDescr.CosAcquisitionAngle;

			if (!canRotate && target == CurrentTarget.Entity)
			{
				Log.DebugLog("Losing current target, checking proximity", Logger.severity.DEBUG);
				ProximityDetonate();
			}

			return canRotate;
		}

		protected override bool Obstructed(ref Vector3D targetPos, IMyEntity target)
		{
			return false;
		}

		protected override float ProjectileSpeed(ref Vector3D targetPos)
		{
			return acceleration;
		}

		private void TargetSemiActive()
		{
			SemiActiveTarget sat = CurrentTarget as SemiActiveTarget;
			if (sat == null)
			{
				Log.DebugLog("creating semi-active target", Logger.severity.DEBUG);
				sat = new SemiActiveTarget(m_launcher.CubeBlock);
				SetTarget(sat);
			}

			sat.Update(MyEntity);
		}

		private void Update()
		{
			Target cached = CurrentTarget;
			if (!cached.FiringDirection.HasValue)
				return;

			Log.TraceLog("target: " + cached.Entity.getBestName() + ", ContactPoint: " + cached.ContactPoint);

			Log.TraceLog("FiringDirection invalid: " + cached.FiringDirection, Logger.severity.FATAL, condition: !cached.FiringDirection.IsValid());
			Log.TraceLog("ContactPoint invalid: " + cached.ContactPoint, Logger.severity.FATAL, condition: !cached.ContactPoint.IsValid());

			Vector3 targetDirection;

			switch (m_stage)
			{
				case Stage.Boost:
					Log.DebugLog("m_gravData == null", Logger.severity.FATAL, condition: m_gravData == null);
					targetDirection = -m_gravData.Normal;
					break;
				case Stage.MidCourse:
					Vector3 toTarget = cached.GetPosition() - MyEntity.GetPosition();
					targetDirection = Vector3.Normalize(Vector3.Reject(toTarget, m_gravData.Normal));
					break;
				case Stage.SemiActive:
				case Stage.Golis:
				case Stage.Guided:
					targetDirection = cached.FiringDirection.Value;
					break;
				default:
					return;
			}

			Vector3 forward = MyEntity.WorldMatrix.Forward;
			float angle = (float)Math.Acos(Vector3.Dot(forward, targetDirection));

			Log.DebugLog("forward: " + forward + ", targetDirection: " + targetDirection + ", angle: " + angle);

			if (m_stage <= Stage.Guided && angle > 0.001f) // if the angle is too small, the matrix will be invalid
			{ // rotate missile
				float rotate = Math.Min(angle, myDescr.RotationPerUpdate);
				Vector3 axis = forward.Cross(targetDirection);
				axis.Normalize();
				Quaternion rotation = Quaternion.CreateFromAxisAngle(axis, rotate);

				if (!Stopped)
				{
					MatrixD WorldMatrix = MyEntity.WorldMatrix;
					MatrixD newMatrix = WorldMatrix.GetOrientation();
					newMatrix = MatrixD.Transform(newMatrix, rotation);
					newMatrix.Translation = WorldMatrix.Translation;

					MyEntity.WorldMatrix = newMatrix;
				}
			}

			{ // accelerate if facing target
				if (angle < Static.Angle_AccelerateWhen && addSpeedPerUpdate > 0f && MyEntity.GetLinearVelocity().LengthSquared() < myAmmo.AmmoDefinition.DesiredSpeed * myAmmo.AmmoDefinition.DesiredSpeed)
				{
					//Log.DebugLog("accelerate. angle: " + angle, "Update()");
					if (!Stopped)
						MyEntity.Physics.LinearVelocity += MyEntity.WorldMatrix.Forward * addSpeedPerUpdate;
				}
			}

			if (myDescr.TargetRange != 0f && CurrentTarget is LastSeenTarget)
			{
				Vector3D myPosition = MyEntity.GetPosition();
				Vector3D realTargetPos = CurrentTarget.Entity.GetPosition();
				double distSq; Vector3D.DistanceSquared(ref myPosition, ref realTargetPos, out distSq);
				if (distSq < myDescr.TargetRange * myDescr.TargetRange)
				{
					Log.DebugLog("Promoting targeting");
					SetTarget(new TurretTarget(CurrentTarget.Entity, CurrentTarget.TType));
				}
			}

			if (myDescr.AcquisitionAngle == MathHelper.Pi) // otherwise we check while outside of AcquisitionAngle see CanRotateTo
				ProximityDetonate();
		}

		/// <summary>
		/// If the missile is near the target and not getting closer, detonate.
		/// </summary>
		private void ProximityDetonate()
		{
			if (!MyAPIGateway.Multiplayer.IsServer || myDescr.DetonateRange == 0f)
				return;

			Target cached = CurrentTarget;
			if (!cached.FiringDirection.HasValue)
				return;

			Vector3D myPosition = MyEntity.GetPosition();
			Vector3D targetPosition = cached.GetPosition();

			double distSq; Vector3D.DistanceSquared(ref myPosition, ref targetPosition, out distSq);
			if (distSq < myDescr.DetonateRange * myDescr.DetonateRange &&	Vector3.Normalize(MyEntity.GetLinearVelocity()).Dot(cached.FiringDirection.Value) < Static.Cos_Angle_Detonate)
			{
				Log.DebugLog("proximity detonation, target: " + cached.Entity + ", target position: " + cached.GetPosition() + ", real position: " + cached.Entity.GetPosition() + ", type: " + cached.GetType().Name, Logger.severity.INFO);
				Explode();
				m_stage = Stage.Terminated;
			}
		}

		private void UpdateCluster()
		{
			const float moveSpeed = 3f;
			const float moveUpdateSq = moveSpeed * moveSpeed * Globals.UpdateDuration * Globals.UpdateDuration;
			const float maxVelChangeSq = 10000f * 10000f * Globals.UpdateDuration * Globals.UpdateDuration;

			Vector3[] slaveVelocity = new Vector3[myCluster.Slaves.Count];
			if (CurrentTarget.Entity != null)
				myCluster.AdjustMulti(CurrentTarget.Entity.LocalAABB.GetLongestDim() * 0.5f);

			MatrixD masterMatrix = MyEntity.WorldMatrix;

			for (int i = 0; i < slaveVelocity.Length; i++)
			{
				if (myCluster.Slaves[i].Closed)
					continue;

				Vector3D slavePos = myCluster.Slaves[i].GetPosition();
				Vector3D offset = myCluster.SlaveOffsets[i] * myCluster.OffsetMulti;
				Vector3D destination;
				Vector3D.Transform(ref offset, ref masterMatrix, out destination);
				double distSquared = Vector3D.DistanceSquared(slavePos, destination);

				if (distSquared >= moveUpdateSq)
				{
					slaveVelocity[i] = (destination - slavePos) / (float)Math.Sqrt(distSquared) * moveSpeed;
					//Log.DebugLog("slave: " + i + ", pos: " + slavePos + ", destination: " + destination + ", dist: " + ((float)Math.Sqrt(distSquared)) + ", velocity: " + slaveVelocity[i], "UpdateCluster()");
				}
				else
					slaveVelocity[i] = Vector3.Zero;
			}

			if (Stopped)
				return;

			// when master hits a target, before it explodes, there are a few frames with strange velocity
			Vector3 masterVelocity = myCluster.Master.Physics.LinearVelocity;
			float distSq;
			Vector3.DistanceSquared(ref masterVelocity, ref myCluster.masterVelocity, out distSq);
			if (distSq > maxVelChangeSq)
			{
				Log.DebugLog("massive change in master velocity, terminating. master position: " + myCluster.Master.GetPosition() + ", master velocity: " + myCluster.Master.Physics.LinearVelocity, Logger.severity.INFO);
				m_stage = Stage.Terminated;
				return;
			}
			myCluster.masterVelocity = masterVelocity;

			Log.DebugLog("myCluster == null", Logger.severity.FATAL, condition: myCluster == null);
			MatrixD worldMatrix = MyEntity.WorldMatrix;

			for (int i = 0; i < myCluster.Slaves.Count; i++)
			{
				if (myCluster.Slaves[i].Closed)
					continue;
				worldMatrix.Translation = myCluster.Slaves[i].GetPosition();
				myCluster.Slaves[i].WorldMatrix = worldMatrix;
				myCluster.Slaves[i].Physics.LinearVelocity = MyEntity.Physics.LinearVelocity + slaveVelocity[i];
				//Log.DebugLog("slave: " + i + ", linear velocity: " + myCluster.Slaves[i].Physics.LinearVelocity, "UpdateCluster()");
			}
		}

		private void Explode()
		{
			DestroyAllNearbyMissiles();
			((MyAmmoBase)MyEntity).Explode();
		}

		/// <summary>
		/// Destroys missiles in blast radius.
		/// </summary>
		private void DestroyAllNearbyMissiles()
		{
			if (myCluster != null || m_destroyedNearbyMissiles)
				return;
			m_destroyedNearbyMissiles = true;

			BoundingSphereD explosion = new BoundingSphereD(MyEntity.GetPosition(), myAmmo.MissileDefinition.MissileExplosionRadius);
			Log.DebugLog("Explosion: " + explosion);
			List<MyEntity> entitiesInExplosion = new List<MyEntity>();
			MyGamePruningStructure.GetAllTopMostEntitiesInSphere(ref explosion, entitiesInExplosion, MyEntityQueryType.Dynamic);

			foreach (MyEntity entity in entitiesInExplosion)
				if (!entity.Closed && entity.IsMissile() && entity != MyEntity)
				{
					Log.DebugLog("Explode: " + entity + ", position: " + entity.PositionComp.GetPosition());
					((MyAmmoBase)entity).Explode();
				}

			explosion.Radius *= 10f;
			entitiesInExplosion.Clear();
			MyGamePruningStructure.GetAllTopMostEntitiesInSphere(ref explosion, entitiesInExplosion, MyEntityQueryType.Dynamic);
			foreach (MyEntity entity in entitiesInExplosion)
			{
				if (!entity.Closed && entity.IsMissile() && entity != MyEntity)
				{
					Log.DebugLog("nearby: " + entity + ", position: " + entity.PositionComp.GetPosition());
				}
			}
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
						myGuidanceEnds = Globals.ElapsedTime.Add(TimeSpan.FromSeconds(myDescr.GuidanceSeconds));
						m_rail = null;
						if (myDescr.SemiActiveLaser)
						{
							Log.DebugLog("past arming range, semi-active.", Logger.severity.INFO);
							m_stage = Stage.SemiActive;
							return;
						}

						if (CurrentTarget is GolisTarget)
						{
							Log.DebugLog("past arming range, golis active", Logger.severity.INFO);
							m_stage = Stage.Golis;
							return;
						}

						if (myAmmo.Description.BoostDistance > 1f)
						{
							Log.DebugLog("past arming range, starting boost stage", Logger.severity.INFO);
							StartGravity();
							m_stage = Stage.Boost;
							if (m_gravData == null)
							{
								Log.DebugLog("no gravity, terminating", Logger.severity.WARNING);
								m_stage = Stage.Terminated;
							}
						}
						else
						{
							Log.DebugLog("past arming range, starting guidance.", Logger.severity.INFO);
							m_stage = Stage.Guided;
						}
					}
					return;
				case Stage.Boost:
					if (Vector3D.DistanceSquared(CubeBlock.GetPosition(), MyEntity.GetPosition()) >= myAmmo.Description.BoostDistance * myAmmo.Description.BoostDistance)
					{
						Log.DebugLog("completed boost stage, starting mid course stage", Logger.severity.INFO);
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
						Log.DebugLog("closer to target(" + toTarget + ") than to launch(" + toLaunch + "), starting guidance", Logger.severity.INFO);
						m_stage = Stage.Guided;
						myGuidanceEnds = Globals.ElapsedTime.Add(TimeSpan.FromSeconds(myDescr.GuidanceSeconds));
						m_gravData = null;
					}
					return;
				case Stage.SemiActive:
				case Stage.Golis:
				case Stage.Guided:
					if (Globals.ElapsedTime >= myGuidanceEnds)
					{
						Log.DebugLog("finished guidance", Logger.severity.INFO);
						m_stage = Stage.Ballistic;
					}
					return;
			}
		}

		/// <summary>
		/// Updates myAntenna and sends LastSeen of this missile to launcher.
		/// </summary>
		private void UpdateNetwork()
		{
			if (myAntenna != null)
				myAntenna.Update100();

			if (m_launcherRelay == null)
			{
				Log.DebugLog("No launcher client", Logger.severity.WARNING);
				return;
			}

			RelayStorage store = m_launcherRelay.GetStorage();
			if (store == null)
			{
				Log.DebugLog("Launcher's net client does not have a storage", Logger.severity.WARNING);
				return;
			}

			//Log.DebugLog("Updating launcher with location of this missile", "UpdateNetwork()");
			store.Receive(new LastSeen(MyEntity, LastSeen.DetectedBy.None));
		}

		private void UpdateRail()
		{
			MatrixD matrix = CubeBlock.WorldMatrix;
			m_rail.Rail.From = Vector3D.Transform(m_rail.RailStart, matrix);
			m_rail.Rail.To = m_rail.Rail.From + matrix.Forward * 100d;

			Vector3D closest = m_rail.Rail.ClosestPoint(MyEntity.GetPosition());
			Log.DebugLog("my position: " + MyEntity.GetPosition() + ", closest point: " + closest + ", distance: " + Vector3D.Distance(MyEntity.GetPosition(), closest));
			//Log.DebugLog("my forward: " + MyEntity.WorldMatrix.Forward + ", block forward: " + matrix.Forward + ", angle: " + MyEntity.WorldMatrix.Forward.AngleBetween(matrix.Forward), "UpdateRail()");

			matrix.Translation = closest;
			MyEntity.WorldMatrix = matrix;

			float speed = myAmmo.MissileDefinition.MissileInitialSpeed + (float)(Globals.ElapsedTime - m_rail.Created).TotalSeconds * myAmmo.MissileDefinition.MissileAcceleration;
			MyEntity.Physics.LinearVelocity = CubeBlock.CubeGrid.Physics.LinearVelocity + matrix.Forward * myAmmo.MissileDefinition.MissileInitialSpeed;
		}

		private void StartGravity()
		{
			Vector3D position = MyEntity.GetPosition();
			foreach (MyPlanet planet in Globals.AllPlanets())
				if (planet.IsPositionInGravityWell(position))
				{
					Vector3D targetPosition = CurrentTarget.GetPosition();
					if (!planet.IsPositionInGravityWell(targetPosition))
					{
						Log.DebugLog("Target is not in gravity well, target position: " + targetPosition + ", planet: " + planet.getBestName(), Logger.severity.WARNING);
						return;
					}
					Vector3 gravAtTarget = planet.GetWorldGravityNormalized(ref targetPosition);
					m_gravData = new GravityData(planet, gravAtTarget);
					break;
				}

			if (m_gravData != null)
				UpdateGravity();
		}

		/// <summary>
		/// Updates stored gravity values.
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

			//Log.DebugLog("updated gravity, norm: " + m_gravData.Normal, "UpdateGravity()");
		}

		/// <summary>
		/// Applies gravitational acceleration to the missile.
		/// </summary>
		private void ApplyGravity()
		{
			MyEntity.Physics.LinearVelocity += m_gravData.AccelPerUpdate;
		}

	}
}
