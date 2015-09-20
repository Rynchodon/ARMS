using System;
using System.Collections.Generic;
using Sandbox.Common.ObjectBuilders;
using Sandbox.Definitions;
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

			#region Performance

			public float RotationPerUpdate = 0.0349065850398866f; // 2°
			/// <summary>In metres per second</summary>
			public float Acceleration = 100f;

			#endregion Performance
			#region Tracking

			/// <summary>Targets shall be ignored where angle between missile's forward and direction to target is greater than.</summary>
			public float RotationAttemptLimit = 3.1415926535897932384626433f; // 180°
			/// <summary>Range of turret magic.</summary>
			public float TargetRange = 400f;
			/// <summary>Not implemented.</summary>
			public float RadarPower = 0f;
			/// <summary>If true, missile can receive LastSeen information from radio antennas.</summary>
			public bool HasAntenna = false;

			#endregion Tracking
			#region Payload

			/// <summary>Detonate when this close to target.</summary>
			public float DetonateRange = 0f;

			#region Cluster
			#region Mandatory

			/// <summary>Id of the magazine that contains the ammo that will be spawned.</summary>
			public string ClusterMagazineSubtypeId = "blank";
			/// <summary>Split the cluster when this far from target.</summary>
			public float ClusterSplitRange;
			/// <summary>Accuracy of the cluster.</summary>
			public float ClusterRadius;
			/// <summary>Number of missiles that are spawned.</summary>
			public byte ClusterCount;

			#endregion Mandatory
			#region Optional

			/// <summary>Circle of missiles will be this far behind main missile.</summary>
			public float ClusterOffset_Back;
			/// <summary>Distance along the circumference of the circle of missiles between missiles</summary>
			public float ClusterOffset_Radial;
			/// <summary>Distance primary missile must move before cluster is spawned.</summary>
			public float ClusterSpawnDistance;
			/// <summary>No need to set in Ammo.sbc</summary>
			public bool IsClusterPart;

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

		public const string ClusterDescriptionString = "IsClusterPart=true";
		private static Dictionary<MyDefinitionId, Ammo> KnownDefinitions_Ammo = new Dictionary<MyDefinitionId, Ammo>();

		// TODO: creative mode support
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
			MyAmmoDefinition ammoDef = MyDefinitionManager.Static.GetAmmoDefinition(magDef.AmmoDefinitionId);

			value = new Ammo(ammoDef);
			KnownDefinitions_Ammo.Add(magazineId, value);
			return value;
		}

		public readonly MyAmmoDefinition AmmoDefinition;
		public readonly MyMissileAmmoDefinition MissileDefinition;

		public readonly float TimeToMaxSpeed;
		public readonly float DistanceToMaxSpeed;

		public readonly AmmoDescription Description;

		public readonly MyObjectBuilder_AmmoMagazine ClusterMagazine;
		public readonly Vector3[] ClusterOffsets;
		public readonly bool IsClusterMain;

		private readonly Logger myLogger;

		public Ammo(MyAmmoDefinition Definiton)
		{
			this.myLogger = new Logger("Ammo", null, () => Definiton.Id.ToString());

			this.AmmoDefinition = Definiton;
			this.MissileDefinition = AmmoDefinition as MyMissileAmmoDefinition;

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

			#region Check Cluster

			if (Description.ClusterSplitRange < 1 || Description.ClusterRadius < 1 || Description.ClusterCount < 1) // if any value is bad
			{
				if (Description.ClusterSplitRange >= 1 || Description.ClusterRadius >= 1 || Description.ClusterCount >= 1) // if any value is good
				{
					Logger.debugNotify("Cluster description is incomplete", 10000, Logger.severity.ERROR);
					myLogger.alwaysLog("Cluster description is incomplete: " + Description.ClusterMagazineSubtypeId + ", " + Description.ClusterSplitRange + ", " + Description.ClusterRadius + ", " + Description.ClusterCount, "VerifyCluster()", Logger.severity.ERROR);
				}
				return;
			}

			ClusterMagazine = new MyObjectBuilder_AmmoMagazine() { SubtypeName = Description.ClusterMagazineSubtypeId };

			// enforce constraints on cluster part
			var magDef = MyDefinitionManager.Static.GetAmmoMagazineDefinition(ClusterMagazine.GetObjectId());
			var ammoDef = MyDefinitionManager.Static.GetAmmoDefinition(magDef.AmmoDefinitionId);
			ammoDef.DescriptionString = ClusterDescriptionString;

			myLogger.debugLog("cluster part: " + ammoDef.Id, "VerifyCluster()");

			// BuildOffsets
			Description.ClusterOffset_Radial = MathHelper.Max(Description.ClusterOffset_Radial, 1f);
			ClusterOffsets = new Vector3[Description.ClusterCount];
			float radius = Description.ClusterCount / MathHelper.TwoPi * Description.ClusterOffset_Radial;
			float angle = MathHelper.TwoPi / Description.ClusterCount;
			myLogger.debugLog("radius: " + radius + ", angle: " + angle, "VerifyCluster()");

			for (int i = 0; i < Description.ClusterCount; i++)
			{
				float partAngle = angle * i;
				float right = (float)Math.Sin(partAngle) * radius;
				float up = (float)Math.Cos(partAngle) * radius;
				ClusterOffsets[i] = new Vector3(right, up, Description.ClusterOffset_Back);
				myLogger.debugLog("partAngle: " + partAngle + ", right: " + right + ", up: " + up, "VerifyCluster()");
				myLogger.debugLog("offset: " + ClusterOffsets[i], "VerifyCluster()");
			}

			myLogger.debugLog("Is a cluster missile", "VerifyCluster()");
			IsClusterMain = true;

			#endregion
		}
	}
}
