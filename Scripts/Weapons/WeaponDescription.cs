using System;
using System.Collections.Generic;
using Sandbox.Definitions;
using Sandbox.ModAPI;
using VRage.Game.ModAPI;
using VRage.ObjectBuilders;

namespace Rynchodon.Weapons
{
	public class WeaponDescription
	{

		private static Dictionary<SerializableDefinitionId, WeaponDescription> KnownDescriptions = new Dictionary<SerializableDefinitionId, WeaponDescription>();

		public static WeaponDescription GetFor(IMyCubeBlock block)
		{
			WeaponDescription descr;
			if (!KnownDescriptions.TryGetValue(block.BlockDefinition, out descr))
			{
				descr = CreateFrom(block.GetCubeBlockDefinition());
				KnownDescriptions.Add(block.BlockDefinition, descr);
			}
			return descr;
		}

		static WeaponDescription()
		{
			MyAPIGateway.Entities.OnCloseAll += Entities_OnCloseAll;
		}

		private static void Entities_OnCloseAll()
		{
			MyAPIGateway.Entities.OnCloseAll -= Entities_OnCloseAll;
			KnownDescriptions = null;
		}

		private static WeaponDescription CreateFrom(MyCubeBlockDefinition definition)
		{
			if (string.IsNullOrWhiteSpace(definition.DescriptionString))
				return new WeaponDescription();

			WeaponDescription desc = new WeaponDescription();
			try
			{
				XML_Amendments<WeaponDescription> ammender = new XML_Amendments<WeaponDescription>(desc);
				ammender.AmendAll(definition.DescriptionString, true);
				return ammender.Deserialize();
			}
			catch (Exception ex)
			{
				Logger.DebugNotify("Failed to load description for a weapon", 10000, Logger.severity.ERROR);
				Logger log = new Logger(() => definition.Id.ToString());
				log.alwaysLog("Failed to load description for a weapon", Logger.severity.ERROR);
				log.alwaysLog("Exception: " + ex, Logger.severity.ERROR);
				return new WeaponDescription();
			}
		}

		/// <summary>Allows engager/fighter to control the block.</summary>
		public bool AllowFighterControl = true;
		/// <summary>Allows guided missile control of fired missiles.</summary>
		public bool GuidedMissileLauncher = false;
		/// <summary>Allows targeting with last seen information.</summary>
		public bool LastSeenTargeting = false;
		/// <summary>Distance to move the missile spawn box forward.</summary>
		public float MissileSpawnForward = 0f;

	}
}
