using System;
using Rynchodon.AntennaRelay;
using Rynchodon.Threading;
using Rynchodon.Weapons.SystemDisruption;
using Sandbox.Common.ObjectBuilders;
using Sandbox.Game.Entities;
using Sandbox.ModAPI;
using VRage;
using VRage.Collections;
using VRage.ModAPI;
using VRage.ObjectBuilders;
using VRageMath;

namespace Rynchodon.Weapons.Guided
{
	/*
	 * TODO:
	 * Rail system for safer launching while rotating/accelerating.
	 * Lockon notification for ready-to-launch and to warn of incoming (only some tracking types)
	 * ARH, SA*
	 */
	public class GuidedMissile : TargetingBase
	{

		private class Cluster
		{
			public readonly LockedQueue<IMyEntity> Slaves;
			public readonly int Max;
			/// <summary>Set to true when main missile is far enough from launcher to start forming.</summary>
			public bool MainFarEnough;

			private bool value_fullyFormed;

			public Cluster(int num)
			{
				Max = num;
				Slaves = new LockedQueue<IMyEntity>(num);
			}

			public bool FullyFormed
			{
				get { return value_fullyFormed; }
				set
				{
					if (Slaves.Count == Max)
						value_fullyFormed = value;
				}
			}

		}

		private enum Stage : byte { None, Guided, Ballistic, Terminated }

		private const float Angle_AccelerateWhen = 0.02f;
		private const float Angle_Detonate = 0.1f;
		private const float Angle_Cluster = 0.05f;
		private static readonly TimeSpan checkLastSeen = new TimeSpan(0, 0, 10);

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

		public static void Update1()
		{
			if (lock_AllGuidedMissiles.TryAcquireExclusive())
			{
				AllGuidedMissiles.ApplyChanges();
				lock_AllGuidedMissiles.ReleaseExclusive();
			}

			using (lock_AllGuidedMissiles.AcquireSharedUsing())
				foreach (GuidedMissile missile in AllGuidedMissiles)
					Thread.EnqueueAction(() => {
						if (missile.Stopped)
							return;
						missile.UpdateCluster();
						if (missile.CurrentTarget.TType == TargetType.None)
							return;
						missile.SetFiringDirection();
						missile.Update();
					});
		}

		public static void Update10()
		{
			using (lock_AllGuidedMissiles.AcquireSharedUsing())
				foreach (GuidedMissile missile in AllGuidedMissiles)
					Thread.EnqueueAction(() => {
						if (missile.Stopped)
							return;
						missile.UpdateTarget();
						if (missile.CurrentTarget.TType == TargetType.None || missile.CurrentTarget is LastSeenTarget)
							missile.TargetLastSeen();
					});
		}

		public static void Update100()
		{
			using (lock_AllGuidedMissiles.AcquireSharedUsing())
				foreach (GuidedMissile missile in AllGuidedMissiles)
					Thread.EnqueueAction(() => {
						if (!missile.Stopped)
							missile.ClearBlacklist();
					});
		}


		private readonly Logger myLogger;
		private readonly Ammo myAmmo;
		private readonly Ammo.AmmoDescription myDescr;
		private readonly MissileAntenna myAntenna;

		private LastSeen myTargetSeen;
		private Cluster myCluster;
		private IMyEntity myRock;
		private DateTime failed_lastSeenTarget;
		private DateTime myGuidanceEnds;
		private float addSpeedPerUpdate, accelerationPerUpdate;
		private uint m_bulletsEaten;
		private Stage m_stage;

		private bool Stopped
		{ get { return MyEntity.Closed || m_stage == Stage.Terminated; } }

		/// <summary>
		/// Creates a missile with homing and target finding capabilities.
		/// </summary>
		public GuidedMissile(IMyEntity missile, IMyCubeBlock firedBy, TargetingOptions opt, Ammo ammo, LastSeen initialTarget = null, bool isSlave = false)
			: base(missile, firedBy)
		{
			myLogger = new Logger("GuidedMissile", () => missile.getBestName(), () => m_stage.ToString());
			myAmmo = ammo;
			myDescr = ammo.Description;
			if (ammo.Description.HasAntenna)
				myAntenna = new MissileAntenna(missile);
			TryHard = true;

			AllGuidedMissiles.Add(this);
			missile.OnClose += missile_OnClose;

			if (myAmmo.IsCluster && !isSlave)
				myCluster = new Cluster(myAmmo.MagazineDefinition.Capacity - 1);
			accelerationPerUpdate = (myDescr.Acceleration + myAmmo.MissileDefinition.MissileAcceleration) / 60f;
			addSpeedPerUpdate = myDescr.Acceleration / 60f;

			Options = opt;
			Options.TargetingRange = ammo.Description.TargetRange;
			myTargetSeen = initialTarget;

			myLogger.debugLog("Options: " + Options, "GuidedMissile()");
			//myLogger.debugLog("AmmoDescription: \n" + MyAPIGateway.Utilities.SerializeToXML<Ammo.AmmoDescription>(myDescr), "GuidedMissile()");
		}

		private void missile_OnClose(IMyEntity obj)
		{
			if (AllGuidedMissiles != null)
			{
				AllGuidedMissiles.Remove(this);
				RemoveRock();

				myLogger.debugLog("EMP_Seconds: " + myDescr.EMP_Seconds + ", EMP_Strength: " + myDescr.EMP_Strength, "missile_OnClose()");
				if (myDescr.EMP_Seconds > 0f && myDescr.EMP_Strength > 0)
				{
					myLogger.debugLog("Creating EMP effect", "missile_OnClose()", Logger.severity.DEBUG);
					BoundingSphereD empSphere = new BoundingSphereD(ProjectilePosition(), myAmmo.MissileDefinition.MissileExplosionRadius);
					EMP.ApplyEMP(empSphere, myDescr.EMP_Strength, TimeSpan.FromSeconds(myDescr.EMP_Seconds), CubeBlock.OwnerId);
				}
			}
		}

		protected override bool PhysicalProblem(Vector3D targetPos)
		{
			// test angle
			if (myDescr.RotationAttemptLimit < 3.14f)
			{
				Vector3 direction = targetPos - ProjectilePosition();
				myLogger.debugLog("targetPos: " + targetPos + ", ProjectilePosition: " + ProjectilePosition() + ", direction: " + direction, "PhysicalProblem()");
				Vector3 forward = MyEntity.WorldMatrix.Forward;
				float angleBetween = direction.AngleBetween(forward);
				if (!angleBetween.IsValid() || angleBetween > myDescr.RotationAttemptLimit)
				{
					myLogger.debugLog("angle between too great. direction: " + direction + ", velDirect: " + forward + ", angle between: " + angleBetween, "PhysicalProblem()");
					return true;
				}
				else
					myLogger.debugLog("angle acceptable. direction: " + direction + ", velDirect: " + forward + ", angle between: " + angleBetween, "PhysicalProblem()");
			}

			// obstruction test?

			return false;
		}

		protected override float ProjectileSpeed(Vector3D targetPos)
		{
			return accelerationPerUpdate;
		}

		/// <remarks>
		/// Runs on separate thread.
		/// </remarks>
		private void TargetLastSeen()
		{
			if (myAntenna == null || myAntenna.lastSeenCount == 0)
			{
				if (myTargetSeen != null && myTarget.TType == TargetType.None)
				{
					myLogger.debugLog("Retargeting last", "TargetLastSeen()");
					myTarget = new LastSeenTarget(myTargetSeen);
					SetFiringDirection();
				}
				return;
			}

			if (DateTime.UtcNow - failed_lastSeenTarget < checkLastSeen)
				return;

			LastSeen fetched;
			if (myTargetSeen != null && myAntenna.tryGetLastSeen(myTargetSeen.Entity.EntityId, out fetched) && fetched.isRecent())
			{
				myLogger.debugLog("using previous last seen: " + fetched.Entity.getBestName(), "TargetLastSeen()");
				myTarget = new LastSeenTarget(fetched);
				SetFiringDirection();
				return;
			}

			if (Options.TargetEntityId.HasValue)
			{
				if (myAntenna.tryGetLastSeen(Options.TargetEntityId.Value, out fetched))
				{
					myLogger.debugLog("using last seen from entity id: " + fetched.Entity.getBestName(), "TargetLastSeen()");
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

			myLogger.debugLog("last seen count: " + myAntenna.lastSeenCount, "TargetLastSeen()");
			myAntenna.ForEachLastSeen(seen => {
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
				return false;
			});

			if (closest == null)
			{
				myLogger.debugLog("failed to get a target from last seen", "TargetLastSeen()");
				failed_lastSeenTarget = DateTime.UtcNow;
				myTargetSeen = null;
			}
			else
			{
				myLogger.debugLog("got a target from last seen: " + closest.Entity.getBestName(), "TargetLastSeen()");
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
			// do not guide clusters until they are formed
			// comes before CheckGuidance() so that timer starts after forming
			if (myCluster != null && !myCluster.FullyFormed)
				return;

			CheckGuidance();
			if (m_stage == Stage.None)
				return;

			Target cached = CurrentTarget;
			if (!cached.FiringDirection.HasValue)
				return;

			myLogger.debugLog("target: " + cached.Entity.getBestName() + ", target position: " + cached.InterceptionPoint, "Update()");

			Vector3 forward = MyEntity.WorldMatrix.Forward;
			//Vector3 newForward;

			Vector3 targetDirection = cached.InterceptionPoint.Value - ProjectilePosition();
			targetDirection.Normalize();
			myLogger.debugLog("InterceptionPoint: " + cached.InterceptionPoint.Value + ", Position: " + ProjectilePosition() + ", target direction: " + targetDirection, "Update()");

			float angle = forward.AngleBetween(targetDirection);

			if (m_stage == Stage.Guided && angle > 0.001f) // if the angle is too small, the matrix will be invalid
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

			myLogger.debugLog("targetDirection: " + targetDirection + ", forward: " + forward, "Update()");

			{ // accelerate if facing target
				if (angle < Angle_AccelerateWhen && addSpeedPerUpdate > 0f && MyEntity.GetLinearVelocity().LengthSquared() < myAmmo.AmmoDefinition.DesiredSpeed * myAmmo.AmmoDefinition.DesiredSpeed)
				{
					myLogger.debugLog("accelerate. angle: " + angle, "Update()");
					MyAPIGateway.Utilities.TryInvokeOnGameThread(() => {
						if (!Stopped)
							MyEntity.Physics.LinearVelocity += MyEntity.WorldMatrix.Forward * addSpeedPerUpdate;
					}, myLogger);
				}
			}

			{ // check for proxmity det
				if (angle >= Angle_Detonate && myDescr.DetonateRange > 0f)
				{
					float distSquared = Vector3.DistanceSquared(MyEntity.GetPosition(), cached.GetPosition());
					myLogger.debugLog("distSquared: " + distSquared, "Update()");
					if (distSquared <= myDescr.DetonateRange * myDescr.DetonateRange)
					{
						Explode();
						return;
					}
				}
			}

			{ // check for cluster split
				if (myCluster != null && (angle < Angle_Cluster || m_stage == Stage.Ballistic))
				{
					float distSquared = Vector3.DistanceSquared(MyEntity.GetPosition(), cached.GetPosition());
					if (distSquared <= myDescr.ClusterSplitRange * myDescr.ClusterSplitRange
						&& MyEntity.Physics.LinearVelocity.AngleBetween(forward) <= Angle_Cluster) // comparing velocity to forward works better for ballistic
					{
						myLogger.debugLog("Firing cluster", "Update()");
						FireCluster(cached.FiringDirection.Value);
					}
				}
			}

		}

		/// <remarks>
		/// Runs on separate thread.
		/// </remarks>
		private void UpdateCluster()
		{
			if (myCluster == null)
				return;

			if (!myCluster.MainFarEnough)
			{
				update_mainFarEnough();
				return;
			}

			// slave cluster missiles
			bool formed = true;
			MatrixD[] partWorldMatrix = new MatrixD[myCluster.Slaves.Count];
			float moveBy = myCluster.FullyFormed ? 0f : MyEntity.Physics.LinearVelocity.Length() * 2f * Globals.UpdateDuration;
			float moveBySq = moveBy * moveBy;
			for (int i = 0; i < partWorldMatrix.Length; i++)
			{
				partWorldMatrix[i] = MyEntity.WorldMatrix;
				if (myCluster.FullyFormed)
				{
					//myLogger.debugLog("Cluster is fully formed", "UpdateCluster()");
					partWorldMatrix[i].Translation = Vector3.Transform(myAmmo.ClusterOffsets[i], MyEntity.WorldMatrix);
				}
				else if (MyEntity.WorldAABB.DistanceSquared(CubeBlock.WorldAABB) > myDescr.ClusterFormDistance) // away from launcher
				{
					Vector3D slavePos = myCluster.Slaves[i].GetPosition();
					Vector3D destination = Vector3.Transform(myAmmo.ClusterOffsets[i], MyEntity.WorldMatrix);
					double distSqua = Vector3D.DistanceSquared(slavePos, destination);
					if (distSqua > moveBySq)
					{
						myLogger.debugLog("Slave " + i + " far from master, distance to slave: " + (float)Math.Sqrt(distSqua) + ", moving: " + moveBy, "UpdateCluster()");
						formed = false;
						Vector3D direction = (destination - slavePos) / (float)Math.Sqrt(distSqua);
						partWorldMatrix[i].Translation = slavePos + direction * moveBy;
					}
					else
					{
						//myLogger.debugLog("Slave " + i + " close to master", "UpdateCluster()");
						partWorldMatrix[i].Translation = destination;
					}
				}
				else
				{
					myLogger.debugLog("Slave " + i + " close to launcher", "UpdateCluster()");
					formed = false;
				}
			}
			if (!myCluster.FullyFormed && formed)
			{
				myLogger.debugLog("Cluster is now fully formed, starting guidance shortly", "UpdateCluster()", Logger.severity.INFO);
				myCluster.FullyFormed = formed;
			}

			MyAPIGateway.Utilities.TryInvokeOnGameThread(() => {
				if (Stopped || myCluster == null)
					return;

				if (!myCluster.FullyFormed)
				{
					// suppress acceleration of master
					MyEntity.Physics.LinearVelocity += MyEntity.WorldMatrix.Backward * myAmmo.MissileDefinition.MissileAcceleration * Globals.UpdateDuration;
				}

				int index = 0;
				myCluster.Slaves.ForEach(missile => {
					if (missile.Closed || index >= partWorldMatrix.Length)
						return;
					missile.WorldMatrix = partWorldMatrix[index++];
					missile.Physics.LinearVelocity = MyEntity.Physics.LinearVelocity;
				});
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
		/// Adds a missile to this missile's cluster.
		/// </summary>
		/// <returns>true iff at/past maximum cluster size</returns>
		public bool AddToCluster(IMyEntity missile)
		{
			if (myCluster.Slaves.Count == myCluster.Max)
			{
				myLogger.alwaysLog("extra cluster part fired", "AddToCluster()", Logger.severity.WARNING);
				missile.Delete();
				return true;
			}

			myCluster.Slaves.Enqueue(missile);
			myLogger.debugLog("added to cluster, count is " + myCluster.Slaves.Count, "AddToCluster()");
			if (myCluster.Slaves.Count == myCluster.Max)
			{
				myLogger.debugLog("reached maximum cluster size", "AddToCluster()");
				return true;
			}

			return false;
		}

		/// <summary>
		/// Fires the cluster missiles towards the target.
		/// </summary>
		/// <remarks>
		/// Runs on separate thread.
		/// </remarks>
		private void FireCluster(Vector3D targetDirection)
		{
			if (myCluster == null)
				return;

			m_stage = Stage.Ballistic;
			Cluster temp = myCluster;
			myCluster = null;

			myLogger.debugLog("firing cluster, count: " + temp.Slaves.Count, "FireCluster()");

			MatrixD[] ClusterWorldMatrix = new MatrixD[temp.Slaves.Count];
			Vector3[] Velocity = new Vector3[temp.Slaves.Count];

			MatrixD BaseWorldMatrix = MyEntity.WorldMatrix;
			BaseWorldMatrix.Forward = Vector3.Normalize(MyEntity.GetLinearVelocity());
			float speed = MyEntity.GetLinearVelocity().Length();

			Random rand = new Random();

			int index = 0;
			float spreadMax = myTarget.Entity.LocalAABB.GetShortestDim() * 0.5f / myAmmo.ClusterOffsets[0].Y;
			temp.Slaves.ForEach(missile => {
				float spread = spreadMax * (float)rand.NextDouble();
				myLogger.debugLog("spreadMax: " + spreadMax + ", spread: " + spread + ", local: " + myAmmo.ClusterOffsets[index], "FireCluster()");

				Vector3 localOrigin = myAmmo.ClusterOffsets[index];
				localOrigin.X *= myAmmo.Description.ClusterInitSpread;
				localOrigin.Y *= myAmmo.Description.ClusterInitSpread;
				localOrigin.Z = myAmmo.Description.ClusterOffset_Back;

				Vector3 localDest = myAmmo.ClusterOffsets[index];
				localDest.X *= spread;
				localDest.Y *= spread;
				localDest.Z = -myAmmo.Description.ClusterSplitRange;

				//myLogger.debugLog("offset: " + myAmmo.ClusterOffsets[index] + ", localDest: " + localDest, "FireCluster()");

				Vector3 worldOrigin = Vector3.Transform(localOrigin, BaseWorldMatrix);
				Vector3 worldDest = Vector3.Transform(localDest, BaseWorldMatrix);

				Vector3 direction = worldDest - worldOrigin;
				direction.Normalize();

				//myLogger.debugLog("worldDest: " + worldDest.ToGpsTag("dest " + index) + ", worldOrigin: " + worldOrigin.ToGpsTag("origin " + index) + ", direction: " + direction, "FireCluster()");

				ClusterWorldMatrix[index] = BaseWorldMatrix;
				ClusterWorldMatrix[index].Forward = direction;
				ClusterWorldMatrix[index].Translation = worldOrigin;

				Velocity[index] = direction * speed * (0.5f + (float)rand.NextDouble());

				index++;
			});

			MyAPIGateway.Utilities.InvokeOnGameThread(() => {
				if (Stopped)
					return;

				index = 0;
				temp.Slaves.ForEach(missile => {
					if (!missile.Closed)
					{
						//myLogger.debugLog("cluster " + index + ", World: " + ClusterWorldMatrix[index] + ", Velocity: " + Velocity[index], "FireCluster()");
						missile.WorldMatrix = ClusterWorldMatrix[index];
						missile.Physics.LinearVelocity = Velocity[index];

						//myLogger.debugLog("creating guidance for cluster missile " + index, "FireCluster()");
						//new GuidedMissile(missile, CubeBlock, Options, myAmmo, myTargetSeen, true);
					}
					index++;
				});
			});
		}

		/// <summary>
		/// Updates m_stage if guidance starts or stops.
		/// </summary>
		private void CheckGuidance()
		{
			switch (m_stage)
			{
				case Stage.None:
					double minDist = (MyEntity.WorldAABB.Max - MyEntity.WorldAABB.Min).AbsMax();
					minDist *= 2;

					if (CubeBlock.WorldAABB.DistanceSquared(MyEntity.GetPosition()) >= minDist * minDist)
					{
						myLogger.debugLog("past arming range, starting guidance", "MainFarEnough()");
						myGuidanceEnds = DateTime.UtcNow.AddSeconds(myDescr.GuidanceSeconds);
						m_stage = Stage.Guided;
					}
					break;
				case Stage.Guided:
					if (DateTime.UtcNow > myGuidanceEnds)
					{
						myLogger.debugLog("terminating guidance", "HasGuidance()");
						m_stage = Stage.Ballistic;
					}
					break;
			}
		}

		/// <summary>
		/// Determines if the missile has moved far enough to allow the cluster to be formed.
		/// </summary>
		private void update_mainFarEnough()
		{
			double minDist = (MyEntity.WorldAABB.Max - MyEntity.WorldAABB.Min).AbsMax();
			minDist *= 2;
			minDist += myDescr.ClusterFormDistance;
			//myLogger.debugLog("minimum distance: " + minDist + ", distance: " + CubeBlock.WorldAABB.Distance(MyEntity.GetPosition()), "MainFarEnough()");

			myCluster.MainFarEnough = CubeBlock.WorldAABB.DistanceSquared(MyEntity.GetPosition()) >= minDist * minDist;
			if (myCluster.MainFarEnough)
				myLogger.debugLog("past forming range, ok to form", "MainFarEnough()");
		}

	}
}
