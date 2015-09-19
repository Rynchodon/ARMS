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
			public byte NumCreated;

			public Cluster(byte num)
			{ Parts = new IMyEntity[num]; }
		}

		private const float Angle_ChangePerUpdate = 0.0174532925199433f; // 1°
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
						if (missile.Stopped)
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
		private readonly Ammo.Description myDescr;
		private readonly MissileAntenna myAntenna;
		private readonly long createdAt;

		private Cluster myCluster;
		private IMyEntity myRock;
		private DateTime failed_lastSeenTarget;
		private long targetLastSeen;
		private bool exploded;

		private Dictionary<long, LastSeen> myLastSeen { get { return myAntenna.MyLastSeen; } }

		private bool Stopped
		{ get { return MyEntity.Closed || exploded; } }

		public GuidedMissile(IMyEntity missile, IMyCubeBlock firedby, TargetingOptions opt, Ammo ammo)
			: base(missile, firedby)
		{
			myLogger = new Logger("GuidedMissile", () => missile.getBestName());
			myDescr = ammo.AmmoDescription;
			if (ammo.AmmoDescription.HasAntenna)
				myAntenna = new MissileAntenna(missile);
			Options = opt;
			Options.TargetingRange = ammo.AmmoDescription.TargetRange;
			TryHard = true;

			AllGuidedMissiles.Add(this);
			missile.OnClose += obj => {
				AllGuidedMissiles.Remove(this);
				RemoveRock();
			};

			myLogger.debugLog("Options: " + Options, "GuidedMissile()");
			myLogger.debugLog("Description: \n" + MyAPIGateway.Utilities.SerializeToXML<Ammo.Description>(myDescr), "GuidedMissile()");

			if (myDescr.IsClusterMain)
			{
				myLogger.debugLog("adding the ammo mag", "GuidedMissile()");
				(CubeBlock as IMyInventoryOwner).GetInventory(0).AddItems(1, myDescr.ClusterMagazine, 0);
				myCluster = new Cluster(myDescr.ClusterCount);
			}
			createdAt = UpdateCount;
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
				if (angle <= Angle_ChangePerUpdate)
					newForward = targetDirection;
				else
				{
					Vector3 axis = forward.Cross(targetDirection);
					axis.Normalize();
					Matrix rotation = Matrix.CreateFromAxisAngle(axis, Angle_ChangePerUpdate);

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
		private void Explode()
		{
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
				Position = MyEntity.GetPosition() + MyEntity.Physics.LinearVelocity / 60f,
				Forward = new SerializableVector3(0, 0, 1),
				Up = new SerializableVector3(0, 1, 0)
			};

			MyAPIGateway.Utilities.InvokeOnGameThread(() => {
				myLogger.debugLog("creating rock", "Explode()");
				myRock = MyAPIGateway.Entities.CreateFromObjectBuilderAndAdd(rockBuilder);
			});
		}

		private void UpdateCluster()
		{
			if (myCluster == null)
				return;

			if (myCluster.NumCreated < myCluster.Parts.Length)
			{
				if (UpdateCount >= createdAt + 100)
				{
					// add cluster
					MyAPIGateway.Utilities.InvokeOnGameThread(() => {
						myLogger.debugLog("shooting", "UpdateCluster()");
						(CubeBlock as IMyMissileGunObject).ShootMissile(CubeBlock.WorldMatrix.Forward);
					});
				}
				return;
			}

			// slave cluster missiles
			myLogger.debugLog("slave cluster missiles", "UpdateCluster()");
			MatrixD WorldMatrix = MyEntity.WorldMatrix;
			WorldMatrix.Translation = MyEntity.GetPosition() + WorldMatrix.Backward * 2 + WorldMatrix.Up;
			MyAPIGateway.Utilities.InvokeOnGameThread(() => {
				myCluster.Parts[0].SetWorldMatrix(WorldMatrix);
				myCluster.Parts[0].Physics.LinearVelocity = MyEntity.Physics.LinearVelocity;
			});
		}

		public void AddToCluster(IMyEntity missile)
		{
			myCluster.Parts[myCluster.NumCreated++] = missile;
			myLogger.debugLog("added to cluster, count is " + myCluster.NumCreated, "AddToCluster()");
			if (myCluster.NumCreated == myCluster.Parts.Length)
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
		/// Only call from game thread! Remove the rock created by Explode().
		/// </summary>
		private void RemoveRock()
		{
			if (myRock == null || myRock.Closed)
				return;

			myLogger.debugLog("removing rock", "RemoveRock()");
			myRock.Delete();
		}

		private void TargetLastSeen()
		{
			if (!myDescr.HasAntenna
				|| myLastSeen.Count == 0
				|| DateTime.UtcNow - failed_lastSeenTarget < checkLastSeen)
				return;

			LastSeen previous;
			if (myLastSeen.TryGetValue(targetLastSeen, out previous) && previous.isRecent())
			{
				myLogger.debugLog("using previous last seen: " + previous.Entity.getBestName(), "TargetLastSeen()");
				myTarget = new Target(previous.Entity, TargetType.AllGrid);
				SetFiringDirection();
			}

			Vector3D myPos = MyEntity.GetPosition();
			LastSeen closest = null;
			double closestDist = double.MaxValue;

			myLogger.debugLog("last seen count: " + myLastSeen.Count, "TargetLastSeen()");
			foreach (LastSeen seen in myLastSeen.Values)
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

	}
}
