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
		private readonly WeaponTargeting m_weaponTarget;
		private IMyCubeBlock CubeBlock { get { return m_weaponTarget.CubeBlock; } }
		private IMyFunctionalBlock FuncBlock;
		/// <summary>Local position where the magic happens (hopefully).</summary>
		private readonly BoundingBox MissileSpawnBox;
		private readonly Interfaces.IMyInventory myInventory;

		private ReceiverBlock myAntenna;
		private DateTime nextCheckInventory;
		private MyFixedPoint prev_mass;
		private MyFixedPoint prev_volume;
		private Ammo loadedAmmo;
		//private GuidedMissile clusterMain;
		private List<IMyEntity> m_cluster = new List<IMyEntity>();

		private bool onCooldown;
		private DateTime cooldownUntil;

		public GuidedMissileLauncher(WeaponTargeting weapon)
		{
			m_weaponTarget = weapon;
			FuncBlock = CubeBlock as IMyFunctionalBlock;
			myLogger = new Logger("GuidedMissileLauncher", CubeBlock);

			var defn = CubeBlock.GetCubeBlockDefinition();

			Vector3[] points = new Vector3[3];
			Vector3 forwardAdjust = Vector3.Forward * WeaponDescription.GetFor(CubeBlock).MissileSpawnForward;
			points[0] = CubeBlock.LocalAABB.Min + forwardAdjust;
			points[1] = CubeBlock.LocalAABB.Max + forwardAdjust;
			points[2] = CubeBlock.LocalAABB.Min + Vector3.Up * CubeBlock.GetCubeBlockDefinition().Size.Y * CubeBlock.CubeGrid.GridSize + forwardAdjust;

			MissileSpawnBox = BoundingBox.CreateFromPoints(points);
			if (m_weaponTarget.myTurret != null)
			{
				myLogger.debugLog("original box: " + MissileSpawnBox, "GuidedMissileLauncher()");
				MissileSpawnBox.Inflate(CubeBlock.CubeGrid.GridSize * 2f);
			}

			myLogger.debugLog("MissileSpawnBox: " + MissileSpawnBox, "GuidedMissileLauncher()");

			myInventory = (CubeBlock as Interfaces.IMyInventoryOwner).GetInventory(0);

			Registrar.Add(weapon.FuncBlock, this);
			m_weaponTarget.GuidedLauncher = true;
		}

		public void Update1()
		{
			UpdateLoadedMissile();
			CheckCooldown();
		}

		private bool MissileBelongsTo(IMyEntity missile)
		{
			if (m_weaponTarget.myTurret == null && !(CubeBlock as Ingame.IMyUserControllableGun).IsShooting)
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
			if (m_weaponTarget.myTurret == null)
			{
				if (Vector3D.RectangularDistance(CubeBlock.WorldMatrix.Forward, missile.WorldMatrix.Forward) > 0.01)
				{
					myLogger.debugLog("Facing the wrong way: " + missile + ", missile direction: " + missile.WorldMatrix.Forward + ", block direction: " + CubeBlock.WorldMatrix.Forward 
						+ ", RectangularDistance: " + Vector3D.RectangularDistance(CubeBlock.WorldMatrix.Forward, missile.WorldMatrix.Forward), "MissileBelongsTo()");
					return false;
				}
			}
			else
			{
				Vector3 turretDirection;
				Vector3.CreateFromAzimuthAndElevation(m_weaponTarget.myTurret.Azimuth, m_weaponTarget.myTurret.Elevation, out turretDirection);
				turretDirection = Vector3.Transform(turretDirection, CubeBlock.WorldMatrix.GetOrientation());
				if (Vector3D.RectangularDistance(turretDirection, missile.WorldMatrix.Forward) > 0.01)
				{
					myLogger.debugLog("Facing the wrong way: " + missile + ", missile direction: " + missile.WorldMatrix.Forward + ", turret direction: " + turretDirection
						+ ", RectangularDistance: " + Vector3D.RectangularDistance(CubeBlock.WorldMatrix.Forward, missile.WorldMatrix.Forward), "MissileBelongsTo()");
					return false;
				}
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

			//myLogger.debugLog("Opts: " + m_weaponTarget.Options, "MissileBelongsTo()");
			try
			{
				//if (clusterMain != null)
				//{
				//	if (loadedAmmo.IsCluster)
				//	{
				//		if (clusterMain.AddToCluster(missile))
				//		{
				//			myLogger.debugLog("reached max cluster, on cooldown", "MissileBelongsTo()", Logger.severity.DEBUG);
				//			StartCooldown();
				//		}
				//	}
				//	else
				//	{
				//		myLogger.alwaysLog("deleting extraneous missile: " + missile, "MissileBelongsTo()", Logger.severity.WARNING);
				//		missile.Delete();
				//	}
				//	return true;
				//}

				if (loadedAmmo.IsCluster)
				{
					if (m_cluster.Count == 0)
						FuncBlock.ApplyAction("Shoot_On");

					m_cluster.Add(missile);
					if (m_cluster.Count >= loadedAmmo.MagazineDefinition.Capacity)
					{
						myLogger.debugLog("Final missile in cluster: " + missile, "MissileBelongsTo()", Logger.severity.DEBUG);
					}
					else
					{
						myLogger.debugLog("Added to cluster: " + missile, "MissileBelongsTo()", Logger.severity.DEBUG);
						return true;
					}
				}

				LastSeen initialTarget = null;
				if (findAntenna())
				{
					if (m_weaponTarget.Options.TargetEntityId.HasValue)
						myAntenna.tryGetLastSeen(m_weaponTarget.Options.TargetEntityId.Value, out initialTarget);
					else
					{
						myLogger.debugLog("Searching for target", "MissileBelongsTo()", Logger.severity.DEBUG);
						float closestDistanceSquared = float.MaxValue;
						myAntenna.ForEachLastSeen(seen => {
							if (!seen.isRecent())
								return false;
							IMyCubeGrid grid = seen.Entity as IMyCubeGrid;
							if (grid != null && CubeBlock.canConsiderHostile(grid) && m_weaponTarget.Options.CanTargetType(grid))
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
				if (m_cluster.Count != 0)
				{
					new GuidedMissile(new Cluster(m_cluster), CubeBlock, m_weaponTarget.Options.Clone(), loadedAmmo, initialTarget);
					StartCooldown();
					m_cluster.Clear();
				}
				else
					new GuidedMissile(missile, CubeBlock, m_weaponTarget.Options.Clone(), loadedAmmo, initialTarget);
				//if (loadedAmmo.IsCluster)
				//{
				//	myLogger.debugLog("missile is a cluster missile", "MissileBelongsTo()");
				//	clusterMain = gm;
				//	missile.OnClose += ClusterMain_OnClose;
				//	FuncBlock.ApplyAction("Shoot_On");
				//}
			}
			catch (Exception ex)
			{
				myLogger.alwaysLog("failed to create GuidedMissile", "MissileBelongsTo()", Logger.severity.ERROR);
				myLogger.alwaysLog("Exception: " + ex, "MissileBelongsTo()",  Logger.severity.ERROR);
			}

			return true;
		}

		//private void ClusterMain_OnClose(IMyEntity obj)
		//{
		//	myLogger.debugLog("clusterMain closed, on cooldown", "ClusterMain_OnClose()", Logger.severity.DEBUG);
		//	StartCooldown();
		//}

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
			//if (clusterMain != null)
			//	clusterMain.MyEntity.OnClose -= ClusterMain_OnClose;

			//clusterMain = null;
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
				// do not restore shooting toggle, makes it difficult to turn the thing off
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
				myLogger.debugLog(CubeBlock.gridBlockName() + " cannot fetch from " + antenna.CubeBlock.gridBlockName(), "searchForAntenna()", Logger.severity.TRACE);
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
				myLogger.debugLog(CubeBlock.gridBlockName() + " cannot fetch from " + antenna.CubeBlock.gridBlockName(), "searchForAntenna()", Logger.severity.TRACE);
				return false;
			});

			return myAntenna != null;
		}

	}
}
