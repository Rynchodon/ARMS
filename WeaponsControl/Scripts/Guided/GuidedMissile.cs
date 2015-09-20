using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Rynchodon.AntennaRelay;
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

			public Dictionary<long, LastSeen> MyLastSeen { get { return myLastSeen; } }

			public FastResourceLock lock_MyLastSeen { get { return lock_myLastSeen; } }

			public MissileAntenna(IMyEntity missile)
			{
				this.myMissile = missile;
				AllReceivers_NoBlock.Add(this);
				missile.OnClose += (ent) => AllReceivers_NoBlock.Remove(this);
			}
		}

		private class Cluster
		{
			public IMyEntity[] Parts;
			public byte NumShoot;
			public byte NumCreated;
			/// <summary>Launcher sets to true after ammo switches.</summary>
			public bool Creating;
			/// <summary>Set to true when main missile is far enough from launcher to start spawning.</summary>
			public bool MainFarEnough;

			public Cluster(byte num)
			{
				Parts = new IMyEntity[num];
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
		private readonly long startGuidanceAt;

		private Cluster myCluster;
		private IMyEntity myRock;
		private DateTime failed_lastSeenTarget;
		private long targetLastSeen;
		private bool exploded;

		//private Dictionary<long, LastSeen> myLastSeen { get { return myAntenna.MyLastSeen; } }

		private bool Stopped
		{ get { return MyEntity.Closed || exploded; } }

		public GuidedMissile(IMyEntity missile, IMyCubeBlock firedby, TargetingOptions opt, Ammo ammo)
			: base(missile, firedby)
		{
			myLogger = new Logger("GuidedMissile", () => missile.getBestName());
			myAmmo = ammo;
			myDescr = ammo.Description;
			if (ammo.Description.HasAntenna)
				myAntenna = new MissileAntenna(missile);
			Options = opt;
			Options.TargetingRange = ammo.Description.TargetRange;
			TryHard = true;

			AllGuidedMissiles.Add(this);
			missile.OnClose += obj => {
				AllGuidedMissiles.Remove(this);
				RemoveRock();
			};

			myLogger.debugLog("Options: " + Options, "GuidedMissile()");
			myLogger.debugLog("AmmoDescription: \n" + MyAPIGateway.Utilities.SerializeToXML<Ammo.AmmoDescription>(myDescr), "GuidedMissile()");

			startGuidanceAt = UpdateCount + 10;
			if (myAmmo.IsClusterMain)
			{
				myLogger.debugLog("adding the ammo mag", "GuidedMissile()");
				(CubeBlock as IMyInventoryOwner).GetInventory(0).AddItems(1, myAmmo.ClusterMagazine, 0);
				myCluster = new Cluster(myDescr.ClusterCount);

				// before abandoning this process, try disabling the weapon until ammo is switched from ClusterMagazine to something else
				MyAPIGateway.Utilities.InvokeOnGameThread(() => (CubeBlock as IMyFunctionalBlock).RequestEnable(false));
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

		protected override float ProjectileSpeed()
		{
			// TODO: actual calculation for speed

			return myDescr.Acceleration;
		}

		/// <remarks>
		/// Runs on separate thread.
		/// </remarks>
		private void Update()
		{
			Target cached = CurrentTarget;
			if (!cached.FiringDirection.HasValue)
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
					Matrix rotation = Matrix.CreateFromAxisAngle(axis, myDescr.RotationPerUpdate);

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
					if (distSquared <= myDescr.ClusterRadius * myDescr.ClusterRadius)
					{
						myLogger.debugLog("firing cluster", "Update()");
						FireCluster();
					}
				}
			}
		}

		/// <remarks>
		/// Runs on separate thread.
		/// </remarks>
		private void TargetLastSeen()
		{
			if (!myDescr.HasAntenna
				|| DateTime.UtcNow - failed_lastSeenTarget < checkLastSeen)
				return;

			using (myAntenna.lock_MyLastSeen.AcquireSharedUsing())
				if (myAntenna.MyLastSeen.Count == 0)
					return;

			LastSeen previous;
			using (myAntenna.lock_MyLastSeen.AcquireSharedUsing())
				if (myAntenna.MyLastSeen.TryGetValue(targetLastSeen, out previous) && previous.isRecent())
				{
					myLogger.debugLog("using previous last seen: " + previous.Entity.getBestName(), "TargetLastSeen()");
					myTarget = new Target(previous.Entity, TargetType.AllGrid);
					SetFiringDirection();
				}

			Vector3D myPos = MyEntity.GetPosition();
			LastSeen closest = null;
			double closestDist = double.MaxValue;

			using (myAntenna.lock_MyLastSeen.AcquireSharedUsing())
			{
				myLogger.debugLog("last seen count: " + myAntenna.MyLastSeen.Count, "TargetLastSeen()");
				foreach (LastSeen seen in myAntenna.MyLastSeen.Values)
				{
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
				}
			}

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
		private void UpdateCluster()
		{
			if (myCluster == null)
				return;

			myLogger.debugLog("numShoot: " + myCluster.NumShoot + ", total: " + myDescr.ClusterCount + ", creating: " + myCluster.Creating + ", far enough: " + MainFarEnough(), "UpdateCluster()");
			if (myCluster.NumShoot < myDescr.ClusterCount)
			{
				if (myCluster.Creating && MainFarEnough())
				{
					//myLogger.debugLog("going to shoot: " + myCluster.NumShoot + " < " + myCluster.Parts.Length, "UpdateCluster()");
					myCluster.NumShoot++;
					MyAPIGateway.Utilities.InvokeOnGameThread(() => {
						myLogger.debugLog("shooting", "UpdateCluster()");
						//(CubeBlock as IMyMissileGunObject).ShootMissile(CubeBlock.WorldMatrix.Forward);
						try { (CubeBlock as IMyMissileGunObject).ShootMissile(CubeBlock.WorldMatrix.Forward); }
						catch (Exception ex)
						{ myLogger.alwaysLog("failed to shoot: " + ex, "UpdateCluster()", Logger.severity.ERROR); }
					});
				}
			}
			//else if (myCluster.Creating)
			//	MyAPIGateway.Utilities.InvokeOnGameThread(() => (CubeBlock as IMyFunctionalBlock).RequestEnable(true));

			// slave cluster missiles
			myLogger.debugLog("slave cluster missiles", "UpdateCluster()");
			MatrixD[] partWorldMatrix = new MatrixD[myCluster.NumCreated];
			for (int i = 0; i < myCluster.NumCreated; i++)
			{
				partWorldMatrix[i] = MyEntity.WorldMatrix;
				partWorldMatrix[i].Translation = Vector3.Transform(myAmmo.ClusterOffsets[i], MyEntity.WorldMatrix);
			}

			MyAPIGateway.Utilities.InvokeOnGameThread(() => {
				if (myCluster == null)
					return;
				for (int i = 0; i < myCluster.NumCreated; i++)
				{
					try
					{
						if (myCluster.Parts.Length < myCluster.NumCreated) myLogger.debugLog("cluster parts is too short: " + myCluster.Parts.Length + ", " + myCluster.NumCreated, "UpdateCluster()");
						if (myCluster.Parts[i] == null) myLogger.debugLog("cluster part is missing", "UpdateCluster()");
						if (myCluster.Parts[i].Physics == null) myLogger.debugLog("cluster part is missing physics", "UpdateCluster()");

						myLogger.debugLog("i: " + i + ", parts count: " + myCluster.Parts.Length + ", matrix count: " + partWorldMatrix.Length, "UpdateCluster()");

						myCluster.Parts[i].WorldMatrix = partWorldMatrix[i];
						myCluster.Parts[i].Physics.LinearVelocity = MyEntity.Physics.LinearVelocity;
					}
					catch (Exception ex)
					{
						myLogger.alwaysLog("need to fix this: " + ex, "UpdateCluster()", Logger.severity.ERROR);
					}
				}
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
		/// Runs on separate thread.
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

		public void AddToCluster(IMyEntity missile)
		{
			if (myCluster.NumCreated == myDescr.ClusterCount)
			{
				myLogger.debugLog("extra cluster part fired", "AddToCluster()");
				missile.Delete();
				return;
			}

			myCluster.Parts[myCluster.NumCreated++] = missile;
			myLogger.debugLog("added to cluster, count is " + myCluster.NumCreated, "AddToCluster()");
			if (myCluster.NumCreated == myDescr.ClusterCount)
			{
				myLogger.debugLog("removing ammo magazine", "AddToCluster()");
				(CubeBlock as IMyInventoryOwner).GetInventory(0).RemoveItemsAt(0); // TODO: might be locking up the launchers
			}
		}

		private void FireCluster()
		{
			if (myCluster == null)
				return;

			Cluster temp = myCluster;
			myCluster = null;
		}

		/// <summary>
		/// Determines if the missile has moved far enough to allow the cluster to be spawned.
		/// </summary>
		private bool MainFarEnough()
		{
			if (myCluster.MainFarEnough)
				return true;

			double minDist = (MyEntity.WorldAABB.Max - MyEntity.WorldAABB.Min).AbsMax();
			minDist *= 2;
			minDist += myDescr.ClusterSpawnDistance;
			myLogger.debugLog("minimum distance: " + minDist + ", distance: " + CubeBlock.WorldAABB.Distance(MyEntity.GetPosition()), "MainFarEnough()");

			myCluster.MainFarEnough = CubeBlock.WorldAABB.DistanceSquared(MyEntity.GetPosition()) >= minDist * minDist;
			if (myCluster.MainFarEnough)
				myLogger.debugLog("past arming range, ok to spawn", "MainFarEnough()");
			return myCluster.MainFarEnough;
		}

		public bool IsAddingToCluster()
		{
			// left out myCluster.Creating because we still would not want more missiles to be fired
			return myCluster != null && myCluster.NumCreated < myDescr.ClusterCount;
		}

		/// <remarks>
		/// Launcher calls this, only for cluster main, when ammo changes.
		/// </remarks>
		public void OnAmmoChanged()
		{
			myCluster.Creating = true;

			if (myCluster.NumCreated == myDescr.ClusterCount)
			{
				myLogger.debugLog("ammo switched FROM cluster", "OnAmmoChanged()");
				(CubeBlock as IMyFunctionalBlock).RequestEnable(true);
			}
		}

	}
}
