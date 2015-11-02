using System;
using System.Collections.Generic;
using Rynchodon.AntennaRelay;
using Sandbox.Game.Weapons;
using Sandbox.ModAPI;
using VRage;
using VRage.ModAPI;
using VRageMath;
using Ingame = Sandbox.ModAPI.Ingame;
using Interfaces = Sandbox.ModAPI.Interfaces;

namespace Rynchodon.Weapons.Guided
{
	public class GuidedMissileLauncher
	{
		private static readonly TimeSpan checkInventoryInterval = new TimeSpan(0, 0, 1);

		#region Static

		private static Logger staticLogger = new Logger("GuidedMissileLauncher");
		private static List<GuidedMissileLauncher> AllLaunchers = new List<GuidedMissileLauncher>();

		static GuidedMissileLauncher()
		{
			MyAPIGateway.Entities.OnEntityAdd += Entities_OnEntityAdd;
			MyAPIGateway.Entities.OnCloseAll += Entities_OnCloseAll;
		}

		private static void Entities_OnCloseAll()
		{
			MyAPIGateway.Entities.OnEntityAdd -= Entities_OnEntityAdd;
			MyAPIGateway.Entities.OnCloseAll -= Entities_OnCloseAll;
			staticLogger = null;
			AllLaunchers = null;
		}

		public static bool IsGuidedMissileLauncher(IMyCubeBlock block)
		{
			return block is Ingame.IMyUserControllableGun; // all of them!
		}

		private static void Entities_OnEntityAdd(IMyEntity obj)
		{
			if (obj is MyAmmoBase && obj.ToString().StartsWith("MyMissile"))
			{
				foreach (GuidedMissileLauncher launcher in AllLaunchers)
					if (launcher.MissileBelongsTo(obj))
						return;
				staticLogger.debugLog("No one claimed: " + obj, "Entities_OnEntityAdd()");
			}
		}

		#endregion

		private readonly Logger myLogger;
		private readonly FixedWeapon myFixed;
		private IMyCubeBlock CubeBlock { get { return myFixed.CubeBlock; } }
		private IMyFunctionalBlock FuncBlock;
		/// <summary>Local position where the magic happens (hopefully).</summary>
		private readonly BoundingBox MissileSpawnBox;
		private readonly Interfaces.IMyInventory myInventory;

		private ReceiverBlock myAntenna;
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
			// * 10 is temporary and shall be removed once we have the actual model
			MissileSpawnBox = MissileSpawnBox.Include(MissileSpawnBox.Min + CubeBlock.LocalMatrix.Forward * 10);
			MissileSpawnBox = MissileSpawnBox.Include(MissileSpawnBox.Max + CubeBlock.LocalMatrix.Forward * 10);

			myLogger.debugLog("MissileSpawnBox: " + MissileSpawnBox, "GuidedMissileLauncher()");

			myInventory = (CubeBlock as Interfaces.IMyInventoryOwner).GetInventory(0);

			myFixed.AllowedState = WeaponTargeting.State.GetOptions;

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
			{
				myLogger.debugLog("Not in my box: " + missile + ", position: " + local, "MissileBelongsTo()");
				return false;
			}
			if (Vector3D.RectangularDistance(CubeBlock.WorldMatrix.Forward, missile.WorldMatrix.Forward) > 0.001)
			{
				myLogger.debugLog("Facing the wrong way: " + missile, "MissileBelongsTo()");
				return false;
			}

			if (loadedAmmo == null)
			{
				myLogger.debugLog("Mine but no loaded ammo!", "MissileBelongsTo()", Logger.severity.INFO);
				return true;
			}

			if (loadedAmmo.Description == null || loadedAmmo.Description.GuidanceSeconds < 1f)
			{
				myLogger.debugLog("Mine but not a guided missile!", "MissileBelongsTo()", Logger.severity.INFO);
				return true;
			}

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

				LastSeen initialTarget;
				if (myFixed.Options.TargetEntityId.HasValue && findAntenna())
					myAntenna.tryGetLastSeen(myFixed.Options.TargetEntityId.Value, out initialTarget);
				else
					initialTarget = null;

				myLogger.debugLog("creating new guided missile", "MissileBelongsTo()");
				GuidedMissile gm = new GuidedMissile(missile as MyAmmoBase, CubeBlock, myFixed.Options.Clone(), loadedAmmo, initialTarget);
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
				loadedAmmo = newAmmo;
				myLogger.debugLog("loaded ammo: " + loadedAmmo.AmmoDefinition, "UpdateLoadedMissile()");
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

		/// <summary>
		/// Search for an attached antenna, if we do not have one.
		/// </summary>
		/// <returns>true iff current antenna is valid or one was found</returns>
		private bool findAntenna()
		{
			if (myAntenna.IsOpen()) // already have one
				return true;

			myAntenna = null;
			Registrar.ForEach((RadioAntenna antenna) => {
				if (antenna.CubeBlock.canSendTo(CubeBlock, true))
				{
					myLogger.debugLog("found antenna: " + antenna.CubeBlock.DisplayNameText, "searchForAntenna()", Logger.severity.INFO);
					myAntenna = antenna;
					return true;
				}
				return false;
			});

			if (myAntenna != null)
				return true;

			Registrar.ForEach((LaserAntenna antenna) => {
				if (antenna.CubeBlock.canSendTo(CubeBlock, true))
				{
					myLogger.debugLog("found antenna: " + antenna.CubeBlock.DisplayNameText, "searchForAntenna()", Logger.severity.INFO);
					myAntenna = antenna;
					return true;
				}
				return false;
			});

			return myAntenna != null;
		}

	}
}
