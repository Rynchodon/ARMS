using System;
using System.Collections.Generic;
using Rynchodon.AntennaRelay;
using Sandbox.Definitions;
using Sandbox.Game.Weapons;
using Sandbox.ModAPI;
using VRage;
using VRage.Game;
using VRage.Game.Entity;
using VRage.Game.ModAPI;
using VRage.ModAPI;
using VRageMath;
using Ingame = Sandbox.ModAPI.Ingame;

namespace Rynchodon.Weapons.Guided
{
	public class GuidedMissileLauncher
	{
		private const ulong checkInventoryInterval = Globals.UpdatesPerSecond;

		#region Static

		private static Logger staticLogger = new Logger();

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
			if (obj.IsMissile())
			{
				Registrar.ForEach((GuidedMissileLauncher launcher) => {
					return launcher.MissileBelongsTo(obj);
				});
			}
		}

		#endregion

		private readonly Logger myLogger;
		public readonly WeaponTargeting m_weaponTarget;
		public IMyCubeBlock CubeBlock { get { return m_weaponTarget.CubeBlock; } }
		public IMyFunctionalBlock FuncBlock { get { return CubeBlock as IMyFunctionalBlock; } }
		/// <summary>Local position where the magic happens (hopefully).</summary>
		private readonly BoundingBox MissileSpawnBox;
		private readonly MyInventoryBase myInventory;
		public readonly IRelayPart m_relayPart;

		private List<IMyEntity> m_cluster = new List<IMyEntity>();

		private bool m_onCooldown, m_onGameCooldown;
		private TimeSpan m_gameCooldownTime;
		private TimeSpan cooldownUntil;

		public Ammo loadedAmmo { get { return m_weaponTarget.LoadedAmmo; } }

		public GuidedMissileLauncher(WeaponTargeting weapon)
		{
			m_weaponTarget = weapon;
			myLogger = new Logger(CubeBlock);
			m_relayPart = RelayClient.GetOrCreateRelayPart(m_weaponTarget.CubeBlock);

			MyWeaponBlockDefinition defn = (MyWeaponBlockDefinition)CubeBlock.GetCubeBlockDefinition();

			Vector3[] points = new Vector3[3];
			Vector3 forwardAdjust = Vector3.Forward * WeaponDescription.GetFor(CubeBlock).MissileSpawnForward;
			points[0] = CubeBlock.LocalAABB.Min + forwardAdjust;
			points[1] = CubeBlock.LocalAABB.Max + forwardAdjust;
			points[2] = CubeBlock.LocalAABB.Min + Vector3.Up * defn.Size.Y * CubeBlock.CubeGrid.GridSize + forwardAdjust;

			MissileSpawnBox = BoundingBox.CreateFromPoints(points);
			if (m_weaponTarget.myTurret != null)
			{
				//myLogger.debugLog("original box: " + MissileSpawnBox, "GuidedMissileLauncher()");
				MissileSpawnBox.Inflate(CubeBlock.CubeGrid.GridSize * 2f);
			}

			//myLogger.debugLog("MissileSpawnBox: " + MissileSpawnBox, "GuidedMissileLauncher()");

			myInventory = ((MyEntity)CubeBlock).GetInventoryBase(0);

			Registrar.Add(weapon.FuncBlock, this);
			m_weaponTarget.GuidedLauncher = true;

			m_gameCooldownTime = TimeSpan.FromSeconds(60d / MyDefinitionManager.Static.GetWeaponDefinition(defn.WeaponDefinitionId).WeaponAmmoDatas[(int)MyAmmoType.Missile].RateOfFire);
			myLogger.debugLog("m_gameCooldownTime: " + m_gameCooldownTime);
		}

		public void Update1()
		{
			CheckCooldown();
		}

		private bool MissileBelongsTo(IMyEntity missile)
		{
			if (m_weaponTarget.myTurret == null && !(CubeBlock as Ingame.IMyUserControllableGun).IsShooting)
			{
				//myLogger.debugLog("Not mine, not shooting", "MissileBelongsTo()");
				return false;
			}
			Vector3D local = Vector3D.Transform(missile.GetPosition(), CubeBlock.WorldMatrixNormalizedInv);
			if (MissileSpawnBox.Contains(local) != ContainmentType.Contains)
			{
				//myLogger.debugLog("Not in my box: " + missile + ", position: " + local, "MissileBelongsTo()");
				return false;
			}
			if (m_weaponTarget.myTurret == null)
			{
				if (Vector3D.RectangularDistance(CubeBlock.WorldMatrix.Forward, missile.WorldMatrix.Forward) > 0.01)
				{
					//myLogger.debugLog("Facing the wrong way: " + missile + ", missile direction: " + missile.WorldMatrix.Forward + ", block direction: " + CubeBlock.WorldMatrix.Forward
					//	+ ", RectangularDistance: " + Vector3D.RectangularDistance(CubeBlock.WorldMatrix.Forward, missile.WorldMatrix.Forward), "MissileBelongsTo()");
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
					//myLogger.debugLog("Facing the wrong way: " + missile + ", missile direction: " + missile.WorldMatrix.Forward + ", turret direction: " + turretDirection
					//	+ ", RectangularDistance: " + Vector3D.RectangularDistance(CubeBlock.WorldMatrix.Forward, missile.WorldMatrix.Forward), "MissileBelongsTo()");
					return false;
				}
			}

			if (loadedAmmo == null)
			{
				//myLogger.debugLog("Mine but no loaded ammo!", "MissileBelongsTo()", Logger.severity.INFO);
				return true;
			}

			if (loadedAmmo.Description == null || loadedAmmo.Description.GuidanceSeconds < 1f)
			{
				//myLogger.debugLog("Mine but not a guided missile!", "MissileBelongsTo()", Logger.severity.INFO);
				return true;
			}

			//myLogger.debugLog("Opts: " + m_weaponTarget.Options, "MissileBelongsTo()");
			try
			{
				if (loadedAmmo.IsCluster)
				{
					if (m_cluster.Count == 0)
						FuncBlock.ApplyAction("Shoot_On");

					m_cluster.Add(missile);
					if (m_cluster.Count >= loadedAmmo.MagazineDefinition.Capacity)
					{
						//myLogger.debugLog("Final missile in cluster: " + missile, "MissileBelongsTo()", Logger.severity.DEBUG);
					}
					else
					{
						//myLogger.debugLog("Added to cluster: " + missile, "MissileBelongsTo()", Logger.severity.DEBUG);
						return true;
					}
				}

				//myLogger.debugLog("creating new guided missile", "MissileBelongsTo()");
				Target initialTarget;
				if (m_weaponTarget.Options.TargetGolis.IsValid())
					initialTarget = new GolisTarget(CubeBlock, m_weaponTarget.Options.TargetGolis);
				else if (m_weaponTarget.CurrentControl == WeaponTargeting.Control.Off)
					initialTarget = null;
				else
					initialTarget = m_weaponTarget.CurrentTarget;
				if (m_cluster.Count != 0)
				{
					Cluster cluster = new Cluster(m_cluster, CubeBlock);
					if (cluster.Master != null)
						new GuidedMissile(new Cluster(m_cluster, CubeBlock), this, ref initialTarget);
					else
						myLogger.alwaysLog("Failed to create cluster, all missiles closed", Logger.severity.WARNING);
					StartCooldown();
					m_cluster.Clear();
				}
				else
				{
					new GuidedMissile(missile, this, ref initialTarget);
					StartCooldown(true);
				}

				// display target in custom info
				if (m_weaponTarget.CurrentControl == WeaponTargeting.Control.Off)
					m_weaponTarget.CurrentTarget = initialTarget;
			}
			catch (Exception ex)
			{
				myLogger.alwaysLog("failed to create GuidedMissile", Logger.severity.ERROR);
				myLogger.alwaysLog("Exception: " + ex, Logger.severity.ERROR);
			}

			return true;
		}

		private void StartCooldown(bool gameCooldown = false)
		{
			if (gameCooldown)
			{
				m_onGameCooldown = true;
				m_weaponTarget.SuppressTargeting = true;
				cooldownUntil = Globals.ElapsedTime + m_gameCooldownTime;
				myLogger.debugLog("started game cooldown, suppressing targeting until " + cooldownUntil);
			}
			else
			{
				m_onCooldown = true;
				FuncBlock.RequestEnable(false);
				FuncBlock.ApplyAction("Shoot_Off");
				cooldownUntil = Globals.ElapsedTime + TimeSpan.FromSeconds(loadedAmmo.Description.ClusterCooldown);
			}
		}

		private void CheckCooldown()
		{
			if (!m_onCooldown && !m_onGameCooldown)
				return;

			if (cooldownUntil > Globals.ElapsedTime)
			{
				if (m_onCooldown && FuncBlock.Enabled)
				{
					myLogger.debugLog("deactivating");
					FuncBlock.RequestEnable(false);
					FuncBlock.ApplyAction("Shoot_Off");
				}
			}
			else
			{
				myLogger.debugLog("off cooldown");
				if (m_onCooldown)
					// do not restore shooting toggle, makes it difficult to turn the thing off
					FuncBlock.RequestEnable(true);
				if (m_onGameCooldown)
					m_weaponTarget.SuppressTargeting = false;
				m_onCooldown = false;
				m_onGameCooldown = false;
			}
		}

	}
}
