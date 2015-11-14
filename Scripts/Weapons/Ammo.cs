using System;
using System.Collections.Generic;
using Sandbox.Common.ObjectBuilders;
using Sandbox.Definitions;
using Sandbox.Game.Entities;
using Sandbox.ModAPI;
using Sandbox.ModAPI.Interfaces;
using VRageMath;

namespace Rynchodon.Weapons
{
	public class Ammo
	{
		public class AmmoDescription
		{
			public static AmmoDescription CreateFrom(MyAmmoDefinition ammo)
			{
				if (string.IsNullOrWhiteSpace(ammo.DescriptionString))
					return null;

				AmmoDescription desc = new AmmoDescription(ammo.Id.SubtypeName);
				try
				{
					XML_Amendments<AmmoDescription> ammender = new XML_Amendments<AmmoDescription>(desc);
					ammender.AmendAll(ammo.DescriptionString, true);
					return ammender.Deserialize();
				}
				catch (Exception ex)
				{
					Logger.debugNotify("Failed to load description for an ammo", 10000, Logger.severity.ERROR);
					desc.myLogger.alwaysLog("Failed to load description for an ammo", "CreateFrom()", Logger.severity.ERROR);
					desc.myLogger.alwaysLog("Exception: " + ex, "CreateFrom()", Logger.severity.ERROR);
					return null;
				}
			}

			public float GuidanceSeconds = 0f;

			#region Performance

			public float RotationPerUpdate = 0.0349065850398866f; // 2°
			/// <summary>In metres per second</summary>
			public float Acceleration = 0f;

			#endregion Performance
			#region Tracking

			/// <summary>Targets shall be ignored where angle between missile's forward and direction to target is greater than.</summary>
			public float RotationAttemptLimit = 3.1415926535897932384626433f; // 180°
			/// <summary>Range of turret magic.</summary>
			public float TargetRange = 0f;
			/// <summary>Not implemented.</summary>
			public float RadarPower = 0f;
			/// <summary>If true, missile can receive LastSeen information from radio antennas.</summary>
			public bool HasAntenna = false;

			#endregion Tracking
			#region Payload

			/// <summary>Detonate when this close to target.</summary>
			public float DetonateRange = 0f;

			public int EMP_Strength = 0;
			public float EMP_Seconds = 0f;

			#region Cluster
			#region Mandatory

			// trying clusters without swapping ammo, might go back to main/part model later

			/// <summary>Split the cluster when this far from target.</summary>
			public float ClusterSplitRange;
			/// <summary>Seconds from last cluster missile being fired until it can fire again.</summary>
			public float ClusterCooldown;

			#endregion Mandatory
			#region Optional

			/// <summary>How far apart each cluster missile will be from main missile when they split. Units = squigglies.</summary>
			public float ClusterInitSpread = 1f;
			/// <summary>Circle of missiles will be this far behind main missile.</summary>
			public float ClusterOffset_Back = 0f;
			/// <summary>Distance along the circumference of the circle of missiles between missiles</summary>
			public float ClusterOffset_Radial = 2f;
			/// <summary>Distance between launcher and missile for missile to move into formation.</summary>
			public float ClusterFormDistance = 1f;

			#endregion Optional
			#endregion Cluster
			#endregion Payload

			private readonly Logger myLogger;

			public AmmoDescription()
			{
				myLogger = new Logger("AmmoDescription", null, () => "Deserialized");
			}

			private AmmoDescription(string SubtypeName)
			{
				myLogger = new Logger("AmmoDescription", null, () => SubtypeName);
			}
		}

		private static Dictionary<MyDefinitionId, Ammo> KnownDefinitions_Ammo = new Dictionary<MyDefinitionId, Ammo>();

		static Ammo()
		{
			MyAPIGateway.Entities.OnCloseAll += Entities_OnCloseAll;
		}

		private static void Entities_OnCloseAll()
		{
			MyAPIGateway.Entities.OnCloseAll -= Entities_OnCloseAll;
			KnownDefinitions_Ammo = null;
		}

		public static Ammo GetLoadedAmmo(IMyCubeBlock weapon)
		{
			var invOwn = (weapon as IMyInventoryOwner);
			if (invOwn == null)
				throw new InvalidOperationException("not an IMyInventoryOwner: " + weapon.getBestName());
			var inv = invOwn.GetInventory(0).GetItems();
			if (inv.Count == 0)
				return null;

			Ammo value;
			MyDefinitionId magazineId = inv[0].Content.GetId();
			if (KnownDefinitions_Ammo.TryGetValue(magazineId, out value))
				return value;

			MyAmmoMagazineDefinition magDef = MyDefinitionManager.Static.GetAmmoMagazineDefinition(magazineId);
			if (magDef == null)
				throw new InvalidOperationException("inventory contains item that is not a magazine: " + weapon.getBestName());

			value = new Ammo(magDef);
			KnownDefinitions_Ammo.Add(magazineId, value);
			return value;
		}

		public readonly MyAmmoDefinition AmmoDefinition;
		public readonly MyMissileAmmoDefinition MissileDefinition;
		public readonly MyAmmoMagazineDefinition MagazineDefinition;

		public readonly float TimeToMaxSpeed;
		public readonly float DistanceToMaxSpeed;

		public readonly AmmoDescription Description;

		public readonly Vector3[] ClusterOffsets;
		public readonly bool IsCluster;

		private readonly Logger myLogger;

		private Ammo(MyAmmoMagazineDefinition ammoMagDef)
		{
			MyAmmoDefinition ammoDef = MyDefinitionManager.Static.GetAmmoDefinition(ammoMagDef.AmmoDefinitionId);
			this.myLogger = new Logger("Ammo", () => ammoMagDef.Id.ToString(), () => ammoDef.Id.ToString());

			this.AmmoDefinition = ammoDef;
			this.MissileDefinition = AmmoDefinition as MyMissileAmmoDefinition;
			this.MagazineDefinition = ammoMagDef;

			if (MissileDefinition != null && !MissileDefinition.MissileSkipAcceleration)
			{
				this.TimeToMaxSpeed = (MissileDefinition.DesiredSpeed - MissileDefinition.MissileInitialSpeed) / MissileDefinition.MissileAcceleration;
				this.DistanceToMaxSpeed = (MissileDefinition.DesiredSpeed + MissileDefinition.MissileInitialSpeed) / 2 * TimeToMaxSpeed;
			}
			else
			{
				this.TimeToMaxSpeed = 0;
				this.DistanceToMaxSpeed = 0;
			}

			Description = AmmoDescription.CreateFrom(AmmoDefinition);

			if (Description == null)
				return;

			#region Check Cluster

			if (Description.ClusterSplitRange < 1 || Description.ClusterCooldown < 1) // if any value is bad
			{
				if (Description.ClusterSplitRange >= 1 || Description.ClusterCooldown >= 1) // if any value is good
				{
					Logger.debugNotify("Cluster description is incomplete", 10000, Logger.severity.ERROR);
					//myLogger.alwaysLog("Cluster description is incomplete: " + Description.ClusterSplitRange + ", " + Description.ClusterSpread + ", " + Description.ClusterCooldown, "VerifyCluster()", Logger.severity.ERROR);
				}
				return;
			}

			// BuildOffsets
			// ClusterOffset_Back can be +/-
			Description.ClusterOffset_Radial = MathHelper.Max(Description.ClusterOffset_Radial, 0f);
			Description.ClusterFormDistance = MathHelper.Max(Description.ClusterFormDistance, 1f);
			ClusterOffsets = new Vector3[ammoMagDef.Capacity - 1];
			float radius = ClusterOffsets.Length / MathHelper.TwoPi * Description.ClusterOffset_Radial;
			float angle = MathHelper.TwoPi / ClusterOffsets.Length;

			for (int i = 0; i < ClusterOffsets.Length; i++)
			{
				float partAngle = angle * i;
				float right = (float)Math.Sin(partAngle) * radius;
				float up = (float)Math.Cos(partAngle) * radius;
				ClusterOffsets[i] = new Vector3(right, up, Description.ClusterOffset_Back);
			}

			myLogger.debugLog("Is a cluster missile", "VerifyCluster()");
			IsCluster = true;

			#endregion
		}

		public float MissileSpeed(float distance)
		{
			myLogger.debugLog("distance = " + distance + ", DistanceToMaxSpeed = " + DistanceToMaxSpeed, "LoadedAmmoSpeed()");
			if (distance < DistanceToMaxSpeed)
			{
				float finalSpeed = (float)Math.Sqrt(MissileDefinition.MissileInitialSpeed * MissileDefinition.MissileInitialSpeed + 2 * MissileDefinition.MissileAcceleration * distance);

				//myLogger.debugLog("close missile calc: " + ((missileAmmo.MissileInitialSpeed + finalSpeed) / 2), "LoadedAmmoSpeed()");
				return (MissileDefinition.MissileInitialSpeed + finalSpeed) / 2;
			}
			else
			{
				float distanceAfterMaxVel = distance - DistanceToMaxSpeed;
				float timeAfterMaxVel = distanceAfterMaxVel / MissileDefinition.DesiredSpeed;

				myLogger.debugLog("DistanceToMaxSpeed = " + DistanceToMaxSpeed + ", TimeToMaxSpeed = " + TimeToMaxSpeed + ", distanceAfterMaxVel = " + distanceAfterMaxVel + ", timeAfterMaxVel = " + timeAfterMaxVel
					+ ", average speed = " + (distance / (TimeToMaxSpeed + timeAfterMaxVel)), "LoadedAmmoSpeed()");
				//myLogger.debugLog("far missile calc: " + (distance / (LoadedAmmo.TimeToMaxSpeed + timeAfterMaxVel)), "LoadedAmmoSpeed()");
				return distance / (TimeToMaxSpeed + timeAfterMaxVel);
			}
		}

	}
}
