using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

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
using Sandbox.ModAPI.Interfaces;
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

namespace Rynchodon.Weapons.Guided
{
	public class GuidedMissile : TargetingBase
	{

		private static readonly Logger staticLogger = new Logger("GuidedMissile");
		private static readonly ThreadManager Thread = new ThreadManager(4);
		private static readonly CachingList<GuidedMissile> AllGuidedMissiles = new CachingList<GuidedMissile>();
		private static readonly FastResourceLock lock_AllGuidedMissiles = new FastResourceLock();

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
						if (!missile.MyEntity.Closed && missile.CurrentTarget.TType != TargetType.None)
						{
							missile.SetFiringDirection();
							missile.RotateTowardsTarget();
						}
					});
		}

		public static void Update10()
		{

			using (lock_AllGuidedMissiles.AcquireSharedUsing())
				foreach (GuidedMissile missile in AllGuidedMissiles)
					Thread.EnqueueAction(() => {
						if (!missile.MyEntity.Closed)
							missile.UpdateTarget();
					});
		}

		public static void Update100()
		{

			using (lock_AllGuidedMissiles.AcquireSharedUsing())
				foreach (GuidedMissile missile in AllGuidedMissiles)
					Thread.EnqueueAction(() => {
						if (!missile.MyEntity.Closed)
							missile.ClearBlacklist();
					});
		}

		public class Definition
		{
			public float MissileRotationPerUpdate = 0.0174532925199433f; // °
			public float MissileRotationAttemptLimit = 1.570796326794897f; // 90°
			//public float MissileBraking = 1f;
			public float MissileTargetingRange = 400f;
			public float MissileRadarRange = 0f;
		}

		private readonly Logger myLogger;
		private readonly Definition myDef;

		public GuidedMissile(IMyEntity missile, IMyCubeBlock firedby, Definition def, TargetingOptions opt)
			: base(missile, firedby)
		{
			myLogger = new Logger("GuidedMissile", () => missile.getBestName());
			myDef = def;
			Options = opt;
			Options.TargetingRange = myDef.MissileTargetingRange;

			AllGuidedMissiles.Add(this);
			missile.OnClose += obj => AllGuidedMissiles.Remove(this); ;

			myLogger.debugLog("initialized, count is " + AllGuidedMissiles.Count, "GuidedMissile()");
			myLogger.debugLog("Options: " + Options, "GuidedMissile()");
		}

		protected override bool PhysicalProblem(Vector3D targetPos)
		{
			// test angle
			Vector3 direction = targetPos - ProjectilePosition();
			direction.Normalize();
			Vector3 velDirect = MyEntity.Physics.LinearVelocity;
			velDirect.Normalize();
			float angleBetween = direction.AngleBetween(velDirect);
			if (!angleBetween.IsValid() || angleBetween > myDef.MissileRotationAttemptLimit)
			{
				myLogger.debugLog("angle between too great. direction: " + direction + ", velDirect: " + velDirect + ", angle between: " + angleBetween, "PhysicalProblem()");
				return true;
			}
			else
				myLogger.debugLog("angle acceptable. direction: " + direction + ", velDirect: " + velDirect + ", angle between: " + angleBetween, "PhysicalProblem()");

			// obstruction test?

			return false;
		}

		protected override float ProjectileSpeed()
		{
			// TODO: actual calculation for speed

			return MyEntity.Physics.LinearVelocity.Length() + 100;
		}

		private void RotateTowardsTarget()
		{
			Target cached = CurrentTarget;
			if (!cached.FiringDirection.HasValue)
			{
				myLogger.debugLog("no firing direction", "RotateTowardsTarget()");
				return;
			}

			// rotate missile
			Vector3 forward = MyEntity.WorldMatrix.Forward;
			Vector3 velocity = MyEntity.Physics.LinearVelocity;

			Vector3 targetDirection = cached.InterceptionPoint.Value - ProjectilePosition() + Braking();
			targetDirection.Normalize();
			Vector3 axis = forward.Cross(targetDirection);

			//Vector3 axis = forward.Cross(cached.FiringDirection.Value);

			axis.Normalize();
			Matrix rotation = Matrix.CreateFromAxisAngle(axis, myDef.MissileRotationPerUpdate);

			Vector3 newForward = Vector3.Transform(forward, rotation);

			MatrixD WorldMatrix = MyEntity.WorldMatrix;
			WorldMatrix.Forward = newForward;

			MyAPIGateway.Utilities.InvokeOnGameThread(() => {
				if (!MyEntity.Closed)
					MyEntity.WorldMatrix = WorldMatrix;
			});

			myLogger.debugLog("targetDirection: " + targetDirection + ", velocity: " + velocity + ", forward: " + forward + ", newForward: " + newForward, "RotateTowardsTarget()");
			//myLogger.debugLog("velocity: " + velocity + ", forward: " + forward + ", newForward: " + newForward, "RotateTowardsTarget()");


			//// target velocity = velocity
			//// target direction = newForward

			//// apply tangental braking
			//myLogger.debugLog("velocity: " + velocity, "RotateTowardsTarget()");
			//myLogger.debugLog("newForward: " + newForward, "RotateTowardsTarget()");

			//float speedOrth = Vector3.Dot(velocity, newForward);
			////Vector3 velOrth = speedOrth * newForward;
			////myLogger.debugLog("velOrth: " + velOrth, "RotateTowardsTarget()");
			////Vector3 velTang = velocity - velOrth;
			////myLogger.debugLog("velTang: " + velTang, "RotateTowardsTarget()");
			//Vector3 brake = newForward * speedOrth - velocity;
			////myLogger.debugLog("brake: " + brake, "RotateTowardsTarget()");
			//brake.Normalize();
			////brake *= myDef.MissileBraking;

			//myLogger.debugLog("braking: " + brake, "RotateTowardsTarget()");
			//MyEntity.Physics.LinearVelocity += brake;

			////myLogger.debugLog("RotationPerUpdate: " + myDef.RotationPerUpdate, "RotateTowardsTarget()");
			////myLogger.debugLog("target at " + cached.InterceptionPoint + ", forward: " + forward + ", missile new direction: " + newForward, "RotateTowardsTarget()");
			////myLogger.debugLog("angle to target changed from " + MathHelper.ToDegrees(cached.FiringDirection.Value.AngleBetween(forward)) + " to " + MathHelper.ToDegrees(cached.FiringDirection.Value.AngleBetween(newForward)), "RotateTowardsTarget()");
		}

		private Vector3 Braking()
		{
			Vector3 targetDirection = CurrentTarget.FiringDirection.Value;
			Vector3 velocity = MyEntity.Physics.LinearVelocity;

			float speedOrth = Vector3.Dot(velocity, targetDirection);
			return targetDirection * speedOrth - velocity;
		}

	}
}
