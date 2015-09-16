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
	public class GuidedMissileLauncher
	{
		#region Static

		private static readonly Logger staticLogger = new Logger("GuidedMissileLauncher");
		private static readonly Dictionary<string, GuidedMissile.Definition> AllDefinitions = new Dictionary<string, GuidedMissile.Definition>();
		private static readonly List<GuidedMissileLauncher> AllLaunchers = new List<GuidedMissileLauncher>();

		static GuidedMissileLauncher()
		{
			MyAPIGateway.Entities.OnEntityAdd += Entities_OnEntityAdd;
		}

		public static bool IsGuidedMissileLauncher(IMyCubeBlock block)
		{
			return block is Ingame.IMyUserControllableGun; // all of them!
		}

		private static void Entities_OnEntityAdd(IMyEntity obj)
		{
			if (obj.ToString().StartsWith("MyMissile"))
				foreach (GuidedMissileLauncher launcher in AllLaunchers)
					if (launcher.MissileBelongsTo(obj))
						return;
		}

		private static GuidedMissile.Definition GetDefinition(IMyCubeBlock block)
		{
			GuidedMissile.Definition result;
			string ID = block.BlockDefinition.ToString();

			if (AllDefinitions.TryGetValue(ID, out result))
			{
				staticLogger.debugLog("definition already loaded for " + ID, "GetDefinition()");
				return result;
			}

			staticLogger.debugLog("creating new definition for " + ID, "GetDefinition()");
			result = new GuidedMissile.Definition();

			MyCubeBlockDefinition def = DefinitionCache.GetCubeBlockDefinition(block);
			if (def == null)
				throw new NullReferenceException("no block definition found for " + block.getBestName());

			if (string.IsNullOrWhiteSpace(def.DescriptionString))
			{
				staticLogger.debugLog("no description in data file for " + ID, "GetDefinition()", Logger.severity.INFO);
				AllDefinitions.Add(ID, result);
				return result;
			}

			// parse description
			string[] properties = def.DescriptionString.Split(new char[] { }, StringSplitOptions.RemoveEmptyEntries);
			foreach (string prop in properties)
			{
				string[] propValue = prop.Split('=');
				if (propValue.Length != 2)
				{
					staticLogger.alwaysLog("for " + block.BlockDefinition.ToString() + ", incorrect format for property: \"" + prop + '"', "GetDefinition()", Logger.severity.WARNING);
					continue;
				}

				// remaining properties are floats, so test first
				float value;
				if (!float.TryParse(propValue[1], out value))
				{
					staticLogger.alwaysLog("for " + block.BlockDefinition.ToString() + ", not a float: \"" + propValue[1] + '"', "GetDefinition()", Logger.severity.WARNING);
					continue;
				}

				switch (propValue[0])
				{
					case "MissileRotationPerUpdate":
						result.MissileRotationPerUpdate = value;
						continue;
					case "MissileRotationAttemptLimit":
						result.MissileRotationAttemptLimit = value;
						continue;
					case "MissileTargetingRange":
						result.MissileTargetingRange = value;
						continue;
					case "MissileRadarRange":
						result.MissileRadarRange = value;
						continue;
					default:
						staticLogger.alwaysLog("for " + block.BlockDefinition.ToString() + ", failed to match to a property: \"" + propValue[0] + '"', "GetDefinition()", Logger.severity.WARNING);
						continue;
				}
			}

			staticLogger.debugLog("parsed description for " + ID, "GetDefinition()", Logger.severity.INFO);
			staticLogger.debugLog("serialized definition:\n" + MyAPIGateway.Utilities.SerializeToXML(result), "GetDefinition()", Logger.severity.TRACE);
			AllDefinitions.Add(ID, result);
			return result;
		}

		#endregion

		private readonly Logger myLogger;
		private readonly FixedWeapon myFixed;
		private IMyCubeBlock CubeBlock { get { return myFixed.CubeBlock; } }
		//private readonly Ingame.IMyUserControllableGun ControlGun;
		/// <summary>Local position where the magic happens (hopefully).</summary>
		private readonly BoundingBox MissileSpawnBox;
		private readonly GuidedMissile.Definition myMissileDef;

		public GuidedMissileLauncher(FixedWeapon weapon)
		{
			myFixed = weapon;
			myLogger = new Logger("GuidedMissileLauncher", CubeBlock);
			//ControlGun = block as Ingame.IMyUserControllableGun;

			MissileSpawnBox = CubeBlock.LocalAABB;
			MissileSpawnBox.Max.Z = MissileSpawnBox.Min.Z;
			MissileSpawnBox.Min.Z -= 1;

			myMissileDef = GetDefinition(CubeBlock);

			AllLaunchers.Add(this);
		}

		private bool MissileBelongsTo(IMyEntity missile)
		{
			Vector3D local = Vector3D.Transform(missile.GetPosition(), CubeBlock.WorldMatrixNormalizedInv);
			if (MissileSpawnBox.Contains(local) != ContainmentType.Contains)
				return false;
			if (Vector3D.RectangularDistance(CubeBlock.WorldMatrix.Forward, missile.WorldMatrix.Forward) > 0.001)
				return false;

			myLogger.debugLog("Opts: " + myFixed.Options, "MissileBelongsTo()");
			GuidedMissile gm = new GuidedMissile(missile, CubeBlock, myMissileDef, myFixed.Options.Clone());
			//MyMissiles.Add(gm);
			//missile.OnClose += m=> MyMissiles.Remove(gm);

			myLogger.debugLog("added a new missile", "MissileBelongsTo()");

			return true;
		}

	}
}
