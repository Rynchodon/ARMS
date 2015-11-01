using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Rynchodon.AntennaRelay;
using Rynchodon.Threading;
using Sandbox.Common;
using Sandbox.Common.Components;
using Sandbox.Common.ObjectBuilders;
using Sandbox.Common.ObjectBuilders.Definitions;
using Sandbox.Common.ObjectBuilders.Voxels;
using Sandbox.Common.ObjectBuilders.VRageData;
using Sandbox.Definitions;
using Sandbox.Game;
using Sandbox.Game.Entities;
using Sandbox.Game.Gui;
using Sandbox.ModAPI;
using VRage;
using VRage.Collections;
using VRage.Components;
using VRage.Game.ObjectBuilders;
using VRage.Library.Utils;
using VRage.ModAPI;
using VRage.ObjectBuilders;
using VRage.Utils;
using VRage.Voxels;
using VRageMath;
using Ingame = Sandbox.ModAPI.Ingame;
using Interfaces = Sandbox.ModAPI.Interfaces;

namespace Rynchodon.Weapons.Guided
{
	// TODO: ARH, GOLIS, SARH, and semi-active magic homing
	public class GuidedMissile : TargetingBase
	{
		private class MissileAntenna : Receiver
		{
			private object myMissile;

			public override object ReceiverObject { get { return myMissile; } }

			//public Dictionary<long, LastSeen> MyLastSeen { get { return myLastSeen; } }

			//public FastResourceLock lock_MyLastSeen { get { return lock_myLastSeen; } }

			public MissileAntenna(IMyEntity missile)
				: base(missile)
			{
				this.myMissile = missile;
				//AllReceivers_NoBlock.Add(this);
				//missile.OnClose += (ent) => AllReceivers_NoBlock.Remove(this);
			}
		}

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

		private static long UpdateCount;

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
						if (missile.Stopped || UpdateCount < missile.startGuidanceAt)
							return;
						missile.UpdateCluster();
						if (missile.SetTarget)
						{
							missile.SetFiringDirection();
							missile.Update();
							return;
						}
						if (missile.CurrentTarget.TType == TargetType.None)
							missile.TargetLastSeen();
						if (missile.CurrentTarget.TType == TargetType.None)
							return;
						missile.SetFiringDirection();
						missile.Update();
					});

			UpdateCount++;
		}

		public static void Update10()
		{
			using (lock_AllGuidedMissiles.AcquireSharedUsing())
				foreach (GuidedMissile missile in AllGuidedMissiles)
					Thread.EnqueueAction(() => {
						if (!missile.Stopped && !missile.SetTarget)
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
		private readonly long startGuidanceAt;
		/// <summary>A target set here will prevent targeting from running.</summary>
		private readonly bool SetTarget;

		private Cluster myCluster;
		private IMyEntity myRock;
		private DateTime failed_lastSeenTarget;
		private long targetLastSeen;
		private bool exploded;

		//private Dictionary<long, LastSeen> myLastSeen { get { return myAntenna.MyLastSeen; } }

		private bool Stopped
		{ get { return MyEntity.Closed || exploded; } }

		/// <summary>
		/// Helper for other constructors.
		/// </summary>
		private GuidedMissile(IMyEntity missile, IMyCubeBlock firedBy, Ammo ammo)
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

			startGuidanceAt = UpdateCount + 10;
		}

		/// <summary>
		/// Creates a missile with homing and target finding capabilities.
		/// </summary>
		public GuidedMissile(IMyEntity missile, IMyCubeBlock firedBy, TargetingOptions opt, Ammo ammo)
			: this(missile, firedBy, ammo)
		{
			Options = opt;
			Options.TargetingRange = ammo.Description.TargetRange;

			myLogger.debugLog("Options: " + Options, "GuidedMissile()");
			myLogger.debugLog("AmmoDescription: \n" + MyAPIGateway.Utilities.SerializeToXML<Ammo.AmmoDescription>(myDescr), "GuidedMissile()");

			if (myAmmo.IsCluster)
				myCluster = new Cluster(myAmmo.MagazineDefinition.Capacity - 1);
		}

		/// <summary>
		/// Creates a missile with a set target and homing capability.
		/// </summary>
		private GuidedMissile(IMyEntity missile, IMyCubeBlock firedBy, IMyEntity target, Ammo ammo)
			: this(missile, firedBy, ammo)
		{
			myLogger = new Logger("GuidedMissile", () => missile.getBestName(), () => target.getBestName());
			SetTarget = true;
			myTarget = new Target(target, TargetType.None);

			myLogger.debugLog("PreTargeted, AmmoDescription: \n" + MyAPIGateway.Utilities.SerializeToXML<Ammo.AmmoDescription>(myDescr), "GuidedMissile()");
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

		protected override float ProjectileSpeed()
		{
			// TODO: actual calculation for speed

			return myDescr.Acceleration;
		}

		/// <remarks>
		/// Runs on separate thread.
		/// </remarks>
		private void TargetLastSeen()
		{
			if (!myDescr.HasAntenna
				|| DateTime.UtcNow - failed_lastSeenTarget < checkLastSeen)
				return;


			if (myAntenna.lastSeenCount == 0)
				return;

			LastSeen previous;
			if (myAntenna.tryGetLastSeen(targetLastSeen, out previous) && previous.isRecent())
			{
				myLogger.debugLog("using previous last seen: " + previous.Entity.getBestName(), "TargetLastSeen()");
				myTarget = new Target(previous.Entity, TargetType.AllGrid);
				SetFiringDirection();
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
				targetLastSeen = long.MinValue;
			}
			else
			{
				myLogger.debugLog("got a target from last seen: " + closest.Entity.getBestName(), "TargetLastSeen()");
				myTarget = new Target(closest.Entity, TargetType.AllGrid);
				SetFiringDirection();
				targetLastSeen = closest.Entity.EntityId;
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

			myLogger.debugLog("target position: " + cached.InterceptionPoint, "Update()");

			Vector3 forward = MyEntity.WorldMatrix.Forward;
			Vector3 newForward;

			Vector3 targetDirection = cached.InterceptionPoint.Value - ProjectilePosition() + Braking(cached);
			targetDirection.Normalize();

			float angle = forward.AngleBetween(targetDirection);

			{ // rotate missile
				if (angle <= myDescr.RotationPerUpdate)
					newForward = targetDirection;
				else
				{
					Vector3 axis = forward.Cross(targetDirection);
					axis.Normalize();
					Quaternion rotation = Quaternion.CreateFromAxisAngle(axis, myDescr.RotationPerUpdate);

					newForward = Vector3.Transform(forward, rotation);
					newForward.Normalize();
				}

				MatrixD WorldMatrix = MyEntity.WorldMatrix;
				WorldMatrix.Forward = newForward;

				MyAPIGateway.Utilities.InvokeOnGameThread(() => {
					if (!Stopped)
						MyEntity.WorldMatrix = WorldMatrix;
				});
			}

			myLogger.debugLog("targetDirection: " + targetDirection + ", forward: " + forward + ", newForward: " + newForward, "Update()");

			{ // accelerate if facing target
				if (angle < Angle_AccelerateWhen)
				{
					myLogger.debugLog("accelerate. angle: " + angle, "Update()");
					MyAPIGateway.Utilities.InvokeOnGameThread(() => {
						if (!Stopped)
							MyEntity.Physics.LinearVelocity += newForward * myDescr.Acceleration / 60f;
					});
				}
			}

			{ // check for proxmity det
				if (angle >= Angle_Detonate && myDescr.DetonateRange > 0f)
				{
					float distSquared = Vector3.DistanceSquared(MyEntity.GetPosition(), cached.Entity.GetPosition());
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
					float distSquared = Vector3.DistanceSquared(MyEntity.GetPosition(), cached.Entity.GetPosition());
					if (distSquared <= myDescr.ClusterSplitRange * myDescr.ClusterSplitRange)
					{
						myLogger.debugLog("firing cluster", "Update()");
						FireCluster(cached.Entity.GetPosition());
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

			if (!MainFarEnough())
				return;

			// slave cluster missiles
			MatrixD[] partWorldMatrix = new MatrixD[myCluster.Slaves.Count];
			for (int i = 0; i < partWorldMatrix.Length; i++)
			{
				partWorldMatrix[i] = MyEntity.WorldMatrix;
				partWorldMatrix[i].Translation = Vector3.Transform(myAmmo.ClusterOffsets[i], MyEntity.WorldMatrix);
			}

			MyAPIGateway.Utilities.InvokeOnGameThread(() => {
				if (myCluster == null)
					return;
				int index = 0;
				myCluster.Slaves.ForEach(missile => {
					if (missile.Closed || index == partWorldMatrix.Length)
						return;
					missile.WorldMatrix = partWorldMatrix[index++];
					missile.Physics.LinearVelocity = MyEntity.Physics.LinearVelocity;
				});
			});
		}

		private Vector3 Braking(Target t)
		{
			Vector3 targetDirection = t.FiringDirection.Value;
			Vector3 velocity = MyEntity.Physics.LinearVelocity;

			float speedOrth = Vector3.Dot(velocity, targetDirection);
			return targetDirection * speedOrth - velocity;
		}

		/// <summary>
		/// Spawns a rock to explode the missile.
		/// </summary>
		/// <remarks>
		/// Runs on separate thread. (sort-of)
		/// </remarks>
		private void Explode()
		{
			MyAPIGateway.Utilities.InvokeOnGameThread(() => {
				// do not check for exploded, might need to try again
				if (MyEntity.Closed)
					return;
				exploded = true;

				MyEntity.Physics.LinearVelocity = Vector3.Zero;

				RemoveRock();

				MyObjectBuilder_InventoryItem item = new MyObjectBuilder_InventoryItem() { Amount = 100, Content = new MyObjectBuilder_Ore() { SubtypeName = "Stone" } };

				MyObjectBuilder_FloatingObject rockBuilder = new MyObjectBuilder_FloatingObject();
				rockBuilder.Item = item;
				rockBuilder.PersistentFlags = MyPersistentEntityFlags2.InScene;
				rockBuilder.PositionAndOrientation = new MyPositionAndOrientation()
				{
					Position = MyEntity.GetPosition()// + MyEntity.Physics.LinearVelocity / 60f,
					//Forward = new SerializableVector3(0, 0, 1),
					//Up = new SerializableVector3(0, 1, 0)
				};


				myLogger.debugLog("creating rock", "Explode()");
				myRock = MyAPIGateway.Entities.CreateFromObjectBuilderAndAdd(rockBuilder);
			});
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
		private void FireCluster(Vector3D targetPosition)
		{
			if (myCluster == null)
				return;

			Cluster temp = myCluster;
			myCluster = null;

			myLogger.debugLog("firing cluster, count: " + temp.Slaves.Count, "FireCluster()");

			MatrixD[] ClusterWorldMatrix = new MatrixD[temp.Slaves.Count];
			Vector3[] Velocity = new Vector3[temp.Slaves.Count];

			// TODO: calculate intercept point
			//float speed = MyEntity.Physics.LinearVelocity.Length() / 8;
			float speed = myAmmo.AmmoDefinition.DesiredSpeed;
			Vector3 targetDirection = targetPosition - MyEntity.GetPosition();
			targetDirection.Normalize();

			// I can't imagine this this the best way to do this
			Vector3 axis = MyEntity.WorldMatrix.Forward.Cross(targetDirection);
			float angle = (float)MyEntity.WorldMatrix.Forward.AngleBetween(targetDirection);
			axis.Normalize();
			Quaternion rotation = Quaternion.CreateFromAxisAngle(axis, angle);
			Matrix BaseWorldMatrix = Matrix.Transform(MyEntity.WorldMatrix.GetOrientation(), rotation);
			BaseWorldMatrix.Translation = MyEntity.GetPosition();
			MatrixD BaseRotationMatrix = BaseWorldMatrix.GetOrientation();

			//myLogger.debugLog("entity matrix: " + MyEntity.WorldMatrix, "FireCluster()");
			//myLogger.debugLog("BaseWorldMatrix: " + BaseWorldMatrix, "FireCluster()");

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

				Velocity[index] = direction * speed;

				index++;
			});

			Vector3 VelocityMain = targetDirection * speed;

			MyAPIGateway.Utilities.InvokeOnGameThread(() => {
				if (Stopped)
					return;

				// need to move this missile or it will run into cluster
				MyEntity.WorldMatrix = BaseWorldMatrix;
				MyEntity.Physics.LinearVelocity = VelocityMain;

				index = 0;
				temp.Slaves.ForEach(missile => {
					if (!missile.Closed)
					{
						//myLogger.debugLog("cluster " + index + ", World: " + ClusterWorldMatrix[index] + ", Velocity: " + Velocity[index], "FireCluster()");
						missile.WorldMatrix = ClusterWorldMatrix[index];
						missile.Physics.LinearVelocity = Velocity[index];

						//myLogger.debugLog("creating guidance for cluster missile " + index, "FireCluster()");
						//new GuidedMissile(missile, CubeBlock, CurrentTarget.Entity, myAmmo);
					}
					index++;
				});
			});
		}

		/// <summary>
		/// Determines if the missile has moved far enough to allow the cluster to be formed.
		/// </summary>
		private bool MainFarEnough()
		{
			if (myCluster.MainFarEnough)
				return true;

			double minDist = (MyEntity.WorldAABB.Max - MyEntity.WorldAABB.Min).AbsMax();
			minDist *= 2;
			minDist += myDescr.ClusterFormDistance;
			//myLogger.debugLog("minimum distance: " + minDist + ", distance: " + CubeBlock.WorldAABB.Distance(MyEntity.GetPosition()), "MainFarEnough()");

			myCluster.MainFarEnough = CubeBlock.WorldAABB.DistanceSquared(MyEntity.GetPosition()) >= minDist * minDist;
			if (myCluster.MainFarEnough)
				myLogger.debugLog("past arming range, ok to form", "MainFarEnough()");
			return myCluster.MainFarEnough;
		}

	}
}
