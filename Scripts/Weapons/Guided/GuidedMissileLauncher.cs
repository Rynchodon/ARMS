using System;
using Rynchodon.AntennaRelay;
using Sandbox.Common.ObjectBuilders;
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
		}

		public static bool IsGuidedMissileLauncher(IMyCubeBlock block)
		{
			return block is Ingame.IMyUserControllableGun && WeaponDescription.GetFor(block).GuidedMissileLauncher;
		}

		private static void Entities_OnEntityAdd(IMyEntity obj)
		{
			if (obj is MyAmmoBase && obj.ToString().StartsWith("MyMissile"))
			{
				Registrar.ForEach((GuidedMissileLauncher launcher) => {
					return launcher.MissileBelongsTo(obj);
				});
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

			// might need this again for shorter range missiles
			//MissileSpawnBox = MissileSpawnBox.Include(MissileSpawnBox.Min + CubeBlock.LocalMatrix.Forward * 10f);
			//MissileSpawnBox = MissileSpawnBox.Include(MissileSpawnBox.Max + CubeBlock.LocalMatrix.Forward * 10f);

			myLogger.debugLog("MissileSpawnBox: " + MissileSpawnBox, "GuidedMissileLauncher()");

			myInventory = (CubeBlock as Interfaces.IMyInventoryOwner).GetInventory(0);

			myFixed.AllowedState = WeaponTargeting.State.GetOptions;
			Registrar.Add(weapon.FuncBlock, this);
		}

		public void Update1()
		{
			UpdateLoadedMissile();
			CheckCooldown();
		}

		private bool MissileBelongsTo(IMyEntity missile)
		{
			if (!(CubeBlock as Ingame.IMyUserControllableGun).IsShooting)
			{
				myLogger.debugLog("Not mine, not shooting", "MissileBelongsTo()");
				return false;
			}
			Vector3D local = Vector3D.Transform(missile.GetPosition(), CubeBlock.WorldMatrixNormalizedInv);
			if (MissileSpawnBox.Contains(local) != ContainmentType.Contains)
			{
				myLogger.debugLog("Not in my box: " + missile + ", position: " + local, "MissileBelongsTo()");
				return false;
			}
			if (Vector3D.RectangularDistance(CubeBlock.WorldMatrix.Forward, missile.WorldMatrix.Forward) > 0.01)
			{
				myLogger.debugLog("Facing the wrong way: " + missile + ", RectangularDistance: " + Vector3D.RectangularDistance(CubeBlock.WorldMatrix.Forward, missile.WorldMatrix.Forward), "MissileBelongsTo()");
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

				LastSeen initialTarget = null;
				if (findAntenna())
				{
					if (myFixed.Options.TargetEntityId.HasValue)
						myAntenna.tryGetLastSeen(myFixed.Options.TargetEntityId.Value, out initialTarget);
					else
					{
						myLogger.debugLog("Searching for target", "MissileBelongsTo()", Logger.severity.DEBUG);
						float closestDistanceSquared = float.MaxValue;
						myAntenna.ForEachLastSeen(seen => {
							if (!seen.isRecent())
								return false;
							IMyCubeGrid grid = seen.Entity as IMyCubeGrid;
							if (grid != null && CubeBlock.canConsiderHostile(grid) && myFixed.Options.CanTargetType(grid))
							{
								float distSquared = Vector3.DistanceSquared(CubeBlock.GetPosition(), grid.GetCentre());
								if (distSquared < closestDistanceSquared)
								{
									closestDistanceSquared = distSquared;
									initialTarget = seen;
								}
							}
							return false;
						});
					}
				}

				myLogger.debugLog("creating new guided missile", "MissileBelongsTo()");
				GuidedMissile gm = new GuidedMissile(missile as MyAmmoBase, CubeBlock, myFixed.Options.Clone(), loadedAmmo, initialTarget);
				if (loadedAmmo.IsCluster)
				{
					myLogger.debugLog("missile is a cluster missile", "MissileBelongsTo()");
					clusterMain = gm;
					missile.OnClose += ClusterMain_OnClose;
					var builder = CubeBlock.GetObjectBuilder_Safe() as MyObjectBuilder_UserControllableGun ;
					restoreShootingToggle = builder.IsShootingFromTerminal;
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

		private void ClusterMain_OnClose(IMyEntity obj)
		{
			myLogger.debugLog("clusterMain closed, on cooldown", "ClusterMain_OnClose()", Logger.severity.DEBUG);
			StartCooldown();
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
			if (clusterMain != null)
				clusterMain.MyEntity.OnClose -= ClusterMain_OnClose;

			clusterMain = null;
			FuncBlock.RequestEnable(false);
			FuncBlock.ApplyAction("Shoot_Off");
			onCooldown = true;
			cooldownUntil = DateTime.UtcNow + TimeSpan.FromSeconds(loadedAmmo.Description.ClusterCooldown);
		}

		private void CheckCooldown()
		{
			if (!onCooldown)
				return;

			if (cooldownUntil > DateTime.UtcNow)
			{
				if (FuncBlock.Enabled)
				{
					FuncBlock.RequestEnable(false);
					FuncBlock.ApplyAction("Shoot_Off");
				}
			}
			else
			{
				myLogger.debugLog("off cooldown", "CheckCooldown()");
				onCooldown = false;
				FuncBlock.RequestEnable(true);
				if (restoreShootingToggle)
					FuncBlock.ApplyAction("Shoot_On");
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
