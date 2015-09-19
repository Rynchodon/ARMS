using System;
using System.Collections.Generic;
using Sandbox.Common.ObjectBuilders;
using Sandbox.Definitions;
using Sandbox.ModAPI;
using Sandbox.ModAPI.Interfaces;

namespace Rynchodon.Weapons
{
	public class Ammo
	{
		public class Description
		{
			public const string ClusterDescriptionString = "IsClusterPart=true";

			public static Description CreateFrom(MyAmmoDefinition ammo)
			{
				if (string.IsNullOrWhiteSpace(ammo.DescriptionString))
					return null;

				Description def = new Description(ammo.Id.SubtypeName);
				try
				{
					XML_Amendments<Description> ammender = new XML_Amendments<Description>(def);
					ammender.AmendAll(ammo.DescriptionString, true);
					def = ammender.Deserialize();
				}
				catch (Exception ex)
				{
					Logger.debugNotify("Failed to load description for an ammo", 10000, Logger.severity.ERROR);
					def.myLogger.alwaysLog("Failed to load description for an ammo", "CreateFrom()", Logger.severity.ERROR);
					def.myLogger.alwaysLog("Exception: " + ex, "CreateFrom()", Logger.severity.ERROR);
					return null;
				}

				def.VerifyCluster();
				return def;
			}

			#region Performance

			/// <summary>Objects shall be ignored where angle between missile's forward and direction to target is greater than.</summary>
			public float RotationAttemptLimit = 3.1415926535897932384626433f; // 180°
			/// <summary>In metres per second</summary>
			public float Acceleration = 100f;

			#endregion
			#region Tracking

			/// <summary>Range of turret magic.</summary>
			public float TargetRange = 400f;
			/// <summary>Not implemented.</summary>
			public float RadarPower = 0f;

			#endregion
			#region Payload

			/// <summary>Detonate when this close to target.</summary>
			public float DetonateRange = 0f;

			public MyObjectBuilder_AmmoMagazine ClusterMagazine;
			public string ClusterMagazineSubtypeId = "blank";
			public float ClusterSplitRange;
			public float ClusterRadius;
			public byte ClusterCount;
			/// <summary>Set by VerifyCluster, any value from .sbc file will be overriden.</summary>
			public bool IsClusterMain;
			public bool IsClusterPart;

			#endregion

			/// <summary>If true, missile can receive LastSeen information from radio antennas.</summary>
			public bool HasAntenna = false;

			private readonly Logger myLogger;

			public Description()
			{
				myLogger = new Logger("Description", null, () => "Deserialized");
			}

			private Description(string SubtypeName)
			{
				myLogger = new Logger("Description", null, () => SubtypeName);
			}

			private void VerifyCluster()
			{
				IsClusterMain = false;

				if (ClusterSplitRange < 1 || ClusterRadius < 1 || ClusterCount < 1) // if any value is bad
				{
					if (ClusterSplitRange >= 1 || ClusterRadius >= 1 || ClusterCount >= 1) // if any value is good
					{
						Logger.debugNotify("Cluster description is incomplete", 10000, Logger.severity.ERROR);
						myLogger.alwaysLog("Cluster description is incomplete: " + ClusterMagazineSubtypeId + ", " + ClusterSplitRange + ", " + ClusterRadius + ", " + ClusterCount, "VerifyCluster()", Logger.severity.ERROR);
					}
					return;
				}

				ClusterMagazine = new MyObjectBuilder_AmmoMagazine() { SubtypeName = ClusterMagazineSubtypeId };

				// enforce constraints on cluster part
				var magDef = MyDefinitionManager.Static.GetAmmoMagazineDefinition(ClusterMagazine.GetObjectId());
				var ammoDef = MyDefinitionManager.Static.GetAmmoDefinition(magDef.AmmoDefinitionId);
				ammoDef.DescriptionString = ClusterDescriptionString;

				myLogger.debugLog("cluster part: " + ammoDef.Id, "VerifyCluster()");

				myLogger.debugLog("Is a cluster missile", "VerifyCluster()");
				IsClusterMain = true;
			}
		}


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

		public readonly Description AmmoDescription;

		public Ammo(MyAmmoDefinition Definiton)
		{
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

			AmmoDescription = Description.CreateFrom(AmmoDefinition);
		}
	}
}
