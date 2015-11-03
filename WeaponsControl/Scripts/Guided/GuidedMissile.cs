using System;
using Rynchodon.AntennaRelay;
using Rynchodon.Threading;
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
	// TODO: ARH, SA*
	public class GuidedMissile : TargetingBase
	{

		private class Cluster
		{
			public readonly LockedQueue<IMyEntity> Slaves;
			public readonly int Max;
			/// <summary>Launcher sets to true after ammo switches.</summary>
			public bool Creating;
			/// <summary>Set to true when main missile is far enough from launcher to start forming.</summary>
			public bool MainFarEnough;

			public Cluster(int num)
			{
				Max = num;
				Slaves = new LockedQueue<IMyEntity>(num);
			}
		}

		private const float Angle_AccelerateWhen = 0.0174532925199433f; // 1°
		private const float Angle_Detonate = 0.1745329251994329f; // 10°
		private static readonly TimeSpan checkLastSeen = new TimeSpan(0, 0, 10);

		private static readonly Logger staticLogger = new Logger("GuidedMissile");
		private static readonly ThreadManager Thread = new ThreadManager();
		private static readonly CachingList<GuidedMissile> AllGuidedMissiles = new CachingList<GuidedMissile>();
		private static readonly FastResourceLock lock_AllGuidedMissiles = new FastResourceLock();

		//private static long UpdateCount;

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
						if (missile.Stopped || !missile.HasGuidance())
							return;
						missile.UpdateCluster();
						if (missile.CurrentTarget.TType == TargetType.None)
							missile.TargetLastSeen();
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
						if (!missile.Stopped)
							missile.UpdateTarget();
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
		private bool startedGuidance;
		private bool m_stopped;

		private bool Stopped
		{ get { return MyEntity.Closed || m_stopped; } }

		/// <summary>
		/// Creates a missile with homing and target finding capabilities.
		/// </summary>
		public GuidedMissile(IMyEntity missile, IMyCubeBlock firedBy, TargetingOptions opt, Ammo ammo, LastSeen initialTarget = null, bool isSlave = false)
			: base(missile, firedBy)
		{
			myLogger = new Logger("GuidedMissile", () => missile.getBestName());
			myAmmo = ammo;
			myDescr = ammo.Description;
			if (ammo.Description.HasAntenna)
				myAntenna = new MissileAntenna(missile);
			TryHard = true;

			AllGuidedMissiles.Add(this);
			missile.OnClose += obj => {
				AllGuidedMissiles.Remove(this);
				RemoveRock();
			};

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
					myTarget = new Target(myTargetSeen);
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
				myTarget = new Target(fetched);
				SetFiringDirection();
				return;
			}

			if (Options.TargetEntityId.HasValue)
			{
				if (myAntenna.tryGetLastSeen(Options.TargetEntityId.Value, out fetched))
				{
					myLogger.debugLog("using last seen from entity id: " + fetched.Entity.getBestName(), "TargetLastSeen()");
					myTarget = new Target(fetched);
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
				if (seen.isRecent() && CubeBlock.canConsiderHostile(seen.Entity))
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
				myTarget = new Target(closest);
				SetFiringDirection();
				myTargetSeen = closest;
			}
		}

		/// <remarks>
		/// Runs on separate thread.
		/// </remarks>
		private void Update()
		{
			Target cached = CurrentTarget;
			if (!cached.FiringDirection.HasValue)
				return;

			// do not guide clusters until they are formed
			if (myCluster != null && myCluster.Slaves.Count < myCluster.Max)
				return;

			myLogger.debugLog("target: " + cached.Entity.getBestName() + ", target position: " + cached.InterceptionPoint, "Update()");

			Vector3 forward = MyEntity.WorldMatrix.Forward;
			//Vector3 newForward;

			Vector3 targetDirection = cached.InterceptionPoint.Value - ProjectilePosition();
			targetDirection.Normalize();
			myLogger.debugLog("InterceptionPoint: " + cached.InterceptionPoint.Value + ", Position: " + ProjectilePosition() + ", target direction: " + targetDirection, "Update()");

			float angle = forward.AngleBetween(targetDirection);

			{ // rotate missile
				if (angle > 0.001f) // if the angle is too small, the matrix will be invalid
				{
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
			}

			myLogger.debugLog("targetDirection: " + targetDirection + ", forward: " + forward, "Update()");

			{ // accelerate if facing target
				if (angle < Angle_AccelerateWhen && addSpeedPerUpdate > 0f)
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
				if (myCluster != null)
				{
					float distSquared = Vector3.DistanceSquared(MyEntity.GetPosition(), cached.GetPosition());
					if (distSquared <= myDescr.ClusterSplitRange * myDescr.ClusterSplitRange)
					{
						myLogger.debugLog("firing cluster", "Update()");
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
			MatrixD[] partWorldMatrix = new MatrixD[myCluster.Slaves.Count];
			for (int i = 0; i < partWorldMatrix.Length; i++)
			{
				partWorldMatrix[i] = MyEntity.WorldMatrix;
				partWorldMatrix[i].Translation = Vector3.Transform(myAmmo.ClusterOffsets[i], MyEntity.WorldMatrix);
			}

			MyAPIGateway.Utilities.TryInvokeOnGameThread(() => {
				if (myCluster == null)
					return;
				int index = 0;
				myCluster.Slaves.ForEach(missile => {
					if (missile.Closed || index == partWorldMatrix.Length)
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
				m_stopped = true;

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
			temp.Slaves.ForEach(missile => {
				float spread = myAmmo.Description.ClusterSpread * (float)rand.NextDouble();
				myLogger.debugLog("spread: " + spread, "FireCluster()");

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

		private bool HasGuidance()
		{
			if (startedGuidance)
			{
				if (DateTime.UtcNow > myGuidanceEnds)
				{
					myLogger.debugLog("terminating guidance", "HasGuidance()");
					m_stopped = true;
					return false;
				}
				return true;
			}

			double minDist = (MyEntity.WorldAABB.Max - MyEntity.WorldAABB.Min).AbsMax();
			minDist *= 2;

			startedGuidance = CubeBlock.WorldAABB.DistanceSquared(MyEntity.GetPosition()) >= minDist * minDist;
			if (startedGuidance)
			{
				myLogger.debugLog("past arming range, starting guidance", "MainFarEnough()");
				myGuidanceEnds = DateTime.UtcNow.AddSeconds(myDescr.GuidanceSeconds);
			}
			return startedGuidance;
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
