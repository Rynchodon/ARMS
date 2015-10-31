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
using Sandbox.Game.Weapons;
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
	public class GuidedMissileLauncher
	{
		private static readonly TimeSpan checkInventoryInterval = new TimeSpan(0, 0, 1);

		#region Static

		private static readonly Logger staticLogger = new Logger("GuidedMissileLauncher");
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
			if (obj is MyAmmoBase && obj.ToString().StartsWith("MyMissile"))
				foreach (GuidedMissileLauncher launcher in AllLaunchers)
					if (launcher.MissileBelongsTo(obj))
						return;
		}

		#endregion

		private readonly Logger myLogger;
		private readonly FixedWeapon myFixed;
		private IMyCubeBlock CubeBlock { get { return myFixed.CubeBlock; } }
		private IMyFunctionalBlock FuncBlock;
		/// <summary>Local position where the magic happens (hopefully).</summary>
		private readonly BoundingBox MissileSpawnBox;

		private readonly Interfaces.IMyInventory myInventory;
		private DateTime nextCheckInventory;
		private MyFixedPoint prev_mass;
		private MyFixedPoint prev_volume;
		private Ammo loadedAmmo;
		private GuidedMissile clusterMain;

		private bool onCooldown;
		private bool restoreShootingToggle;
		private DateTime cooldownUntil;

		public GuidedMissileLauncher(FixedWeapon weapon)
		{
			myFixed = weapon;
			FuncBlock = CubeBlock as IMyFunctionalBlock;
			myLogger = new Logger("GuidedMissileLauncher", CubeBlock);

			MissileSpawnBox = CubeBlock.LocalAABB;
			MissileSpawnBox.Max.Z = MissileSpawnBox.Min.Z;
			MissileSpawnBox.Min.Z -= 1;

			myInventory = (CubeBlock as Interfaces.IMyInventoryOwner).GetInventory(0);

			AllLaunchers.Add(this);
		}

		public void Update1()
		{
			UpdateLoadedMissile();
			CheckCooldown();
		}

		private bool MissileBelongsTo(IMyEntity missile)
		{
			Vector3D local = Vector3D.Transform(missile.GetPosition(), CubeBlock.WorldMatrixNormalizedInv);
			if (MissileSpawnBox.Contains(local) != ContainmentType.Contains)
				return false;
			if (Vector3D.RectangularDistance(CubeBlock.WorldMatrix.Forward, missile.WorldMatrix.Forward) > 0.001)
				return false;

			if (loadedAmmo == null)
			{
				myLogger.debugLog("Mine but no loaded ammo!", "MissileBelongsTo()", Logger.severity.WARNING);
				return true;
			}

			if (loadedAmmo.Description == null || !loadedAmmo.Description.GuidedMissile)
				return true;

			myLogger.debugLog("Opts: " + myFixed.Options, "MissileBelongsTo()");
			try
			{
				if (clusterMain != null)
				{
					if (loadedAmmo.IsCluster)
					{
						if (clusterMain.AddToCluster(missile))
						{
							myLogger.debugLog("reached max cluster, on cooldown", "MissileBelongsTo()", Logger.severity.DEBUG);
							StartCooldown();
						}
					}
					else
					{
						myLogger.alwaysLog("deleting extraneous missile: " + missile, "MissileBelongsTo()", Logger.severity.WARNING);
						missile.Delete();
					}
					return true;
				}

				myLogger.debugLog("creating new guided missile", "MissileBelongsTo()");
				GuidedMissile gm = new GuidedMissile(missile as MyAmmoBase, CubeBlock, myFixed.Options.Clone(), loadedAmmo);
				if (loadedAmmo.IsCluster)
				{
					myLogger.debugLog("missile is a cluster missile", "MissileBelongsTo()");
					clusterMain = gm;
					missile.OnClose += m => {
						myLogger.debugLog("clusterMain closed, on cooldown", "MissileBelongsTo()", Logger.severity.DEBUG);
						StartCooldown();
					};
					FuncBlock.ApplyAction("Shoot_On");
				}
			}
			catch (Exception ex)
			{
				myLogger.alwaysLog("failed to create GuidedMissile", "MissileBelongsTo()", Logger.severity.ERROR);
				myLogger.alwaysLog("Exception: " + ex, "MissileBelongsTo()",  Logger.severity.ERROR);
			}

			return true;
		}

		// TODO: determine if ammo can actually change // I don't remember what this means
		private void UpdateLoadedMissile()
		{
			if (myInventory.CurrentMass == prev_mass && myInventory.CurrentVolume == prev_volume && nextCheckInventory > DateTime.UtcNow)
				return;

			nextCheckInventory = DateTime.UtcNow + checkInventoryInterval;
			prev_mass = myInventory.CurrentMass;
			prev_volume = myInventory.CurrentVolume;

			Ammo newAmmo = Ammo.GetLoadedAmmo(CubeBlock);
			if (newAmmo != null && newAmmo != loadedAmmo)
			{
				//if (newAmmo.Description == null)
				//{
				//	myLogger.debugLog("ammo does not have a description: " + newAmmo.AmmoDefinition, "UpdateLoadedMissile()");
				//	loadedAmmo = null;
				//	return;
				//}
				loadedAmmo = newAmmo;
				myLogger.debugLog("loaded ammo: " + loadedAmmo.AmmoDefinition, "UpdateLoadedMissile()");
				//if (prev_clusterMain != null)
				//	prev_clusterMain.OnAmmoChanged();
			}
		}

		private void StartCooldown()
		{
			clusterMain = null;
			FuncBlock.RequestEnable(false);
			onCooldown = true;
			cooldownUntil = DateTime.UtcNow + TimeSpan.FromSeconds(loadedAmmo.Description.ClusterCooldown);
		}

		private void CheckCooldown()
		{
			if (!onCooldown)
				return;

			if (cooldownUntil > DateTime.UtcNow)
				FuncBlock.RequestEnable(false);
			else
			{
				myLogger.debugLog("off cooldown", "CheckCooldown()");
				onCooldown = false;
				FuncBlock.RequestEnable(true);
			}
		}

	}
}
