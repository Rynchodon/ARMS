#define LOG_ENABLED //remove on build

using System;
using Sandbox.Common.ObjectBuilders;
using Sandbox.Definitions;
using Sandbox.ModAPI;
using VRageMath;
using Ingame = Sandbox.ModAPI.Ingame;
using VRage;

namespace Rynchodon.Autopilot.Weapons
{
	/// <summary>
	/// Class is designed to replace / merge with TurretBase
	/// </summary>
	public class Turret : WeaponTargeting
	{
		/// <summary>limits to determine whether or not a turret can face a target</summary>
		private float minElevation, maxElevation, minAzimuth, maxAzimuth;
		/// <summary>speeds are in rads per update</summary>
		private float speedElevation, speedAzimuth;
		/// <summary>the turret is capable of rotating past ±180° (azimuth)</summary>
		private bool Can360;
		/// <summary>value set by Turret, updated when not controlling</summary>
		private float setElevation, setAzimuth;

		private long UpdateNumber = 0;
		private bool Initialized = false;
		private bool UpdateQueued = false;

		private Logger myLogger;

		public Turret(IMyCubeBlock block)
			: base(block)
		{
			myLogger = new Logger("Turret", () => block.CubeGrid.DisplayName, () => block.DefinitionDisplayNameText, () => block.getNameOnly());

			//myLogger.debugLog("definition limits = " + definition.MinElevationDegrees + ", " + definition.MaxElevationDegrees + ", " + definition.MinAzimuthDegrees + ", " + definition.MaxAzimuthDegrees, "Turret()");
			//myLogger.debugLog("radian limits = " + minElevation + ", " + maxElevation + ", " + minAzimuth + ", " + maxAzimuth, "Turret()");
		}

		private void Initialize()
		{
			// definition limits
			MyLargeTurretBaseDefinition definition = MyDefinitionManager.Static.GetCubeBlockDefinition(weapon.GetSlimObjectBuilder_Safe()) as MyLargeTurretBaseDefinition;

			if (definition == null)
				throw new NullReferenceException("definition");

			minElevation = (float)Math.Max(definition.MinElevationDegrees / 180 * Math.PI, -0.6); // -0.6 was determined empirically
			maxElevation = (float)(definition.MaxElevationDegrees / 180 * Math.PI);
			minAzimuth = (float)(definition.MinAzimuthDegrees / 180 * Math.PI);
			maxAzimuth = (float)(definition.MaxAzimuthDegrees / 180 * Math.PI);

			Can360 = definition.MinAzimuthDegrees <= -180 && definition.MaxAzimuthDegrees >= 180;

			// speeds are in rads per ms (from S.E. source)
			speedElevation = definition.ElevationSpeed * 100f / 6f;
			speedAzimuth = definition.RotationSpeed * 100f / 6f;

			setElevation = myTurret.Elevation;
			setAzimuth = myTurret.Azimuth;

			//myLogger.debugLog("assessment: " + (!weapon.OwnedNPC()) + ", " + (!weapon.DisplayNameText.Contains("[")) + ", " + (!weapon.DisplayNameText.Contains("]")), "Initialize()");
			if (!weapon.OwnedNPC() && !weapon.DisplayNameText.Contains("[") && !weapon.DisplayNameText.Contains("]"))
			{
				myLogger.debugLog("writing defaults", "Turret()");
				// write defaults
				(weapon as IMyTerminalBlock).SetCustomName(weapon.DisplayNameText + Settings.GetSettingString(Settings.SettingName.sSmartTurretDefaultPlayer));
			}
			//else
			//{
			//	myLogger.debugLog("not writing defaults: "+weapon.DisplayNameText, "Turret()");
			//}

			// upgrade
			if (Settings.fileVersion > 0 && Settings.fileVersion < 34)
			{
				string instructions = weapon.DisplayNameText.getInstructions();
				if (instructions != null && !instructions.Contains("(") && !instructions.Contains(")"))
				{
					string name = weapon.DisplayNameText.Replace("[", "[(");
					name = name.Replace("]", ")]");
					(weapon as IMyTerminalBlock).SetCustomName(name);
					myLogger.debugLog("upgraded from previous version, brackets added", "Turret()");
				}
			}

			Initialized = true;
		}

		/// <summary>
		/// Fill CanTarget from turret
		/// </summary>
		public void TargetOptionsFromTurret()
		{
			//Options. CanTarget = TargetType.None;
			MyObjectBuilder_TurretBase builder = weapon.GetSlimObjectBuilder_Safe() as MyObjectBuilder_TurretBase;
			if (builder.TargetMissiles)
				Options.CanTarget |= TargetType.Missile;
			if (builder.TargetMeteors)
				Options.CanTarget |= TargetType.Meteor;
			if (builder.TargetCharacters)
				Options.CanTarget |= TargetType.Character;
			if (builder.TargetMoving)
				Options.CanTarget |= TargetType.Moving;
			if (builder.TargetLargeGrids)
				Options.CanTarget |= TargetType.LargeGrid;
			if (builder.TargetSmallGrids)
				Options.CanTarget |= TargetType.SmallGrid;
			if (builder.TargetStations)
				Options.CanTarget |= TargetType.Station;

			if (myTurret is Ingame.IMyLargeInteriorTurret && myTurret.BlockDefinition.SubtypeName == "LargeInteriorTurret")
				Options.Flags |= TargetingFlags.Interior;

			TargetingRange = myTurret.Range;

			myLogger.debugLog("CanTarget = " + Options.CanTarget, "TargetOptionsFromTurret()");
		}

		/// <summary>
		/// UpdateManager invokes this every update
		/// </summary>
		public void Update()
		{
			try
			{
				if (!UpdateQueued)
				{
					UpdateQueued = true;
					Thread.EnqueueAction(Update_Thread);
				}

				FireAndRotate();
			}
			catch (Exception ex)
			{
				myLogger.alwaysLog("Exception: " + ex, "Update()", Logger.severity.ERROR);
				// TODO: if turret breaks, restore default targeting, apply BROKEN state
				StopFiring();
			}
		}

		/// <summary>
		/// Invoked on targeting thread
		/// </summary>
		private void Update_Thread()
		{
			try
			{
				UpdateQueued = false;
				if (UpdateNumber % 10 == 0)
				{
					if (UpdateNumber % 100 == 0)
					{
						if (UpdateNumber % 1000 == 0)
							Update1000();
						Update100();
					}
					Update10();
				}
				Update1();

				UpdateNumber++;
			}
			catch (Exception ex)
			{
				myLogger.alwaysLog("Exception: " + ex, "Update_Thread()", Logger.severity.ERROR);
				// TODO: if turret breaks, restore default targeting, apply BROKEN state
				StopFiring();
			}
		}

		private bool Queued_Update1, Queued_Update10, Queue_Update100;

		private new void Update1()
		{
			base.Update1();
		}

		private new void Update100()
		{
			if (!Initialized)
				Initialize();

			base.Update100();

			if (!IsControllingWeapon)
				return;

			TargetOptionsFromTurret();
			//myLogger.debugLog("CanTarget: " + CanTarget, "Update100()");
		}

		protected override bool CanRotateTo(Vector3D targetPoint)
		{
			Vector3 RotateTo = Vector3.Normalize(RelativeVector3F.createFromWorld(targetPoint, weapon.CubeGrid).getBlock(weapon));
			float azimuth, elevation;
			Vector3.GetAzimuthAndElevation(RotateTo, out azimuth, out elevation);

			return elevation >= minElevation && elevation <= maxElevation && azimuth >= minAzimuth && azimuth <= maxAzimuth;

			//if (elevation >= minElevation && elevation <= maxElevation && azimuth >= minAzimuth && azimuth <= maxAzimuth)
			//{
			//	myLogger.debugLog("allowing target " + targetPoint + " elevation = " + elevation + ", azimuth = " + azimuth, "CanRotateTo()");
			//	return true;
			//}

			//myLogger.debugLog("denying target " + targetPoint + " elevation = " + elevation + ", azimuth = " + azimuth, "CanRotateTo()");
			//return false;
		}

		private void FireAndRotate()
		{
			if (!IsControllingWeapon)
			{
				setElevation = myTurret.Elevation;
				setAzimuth = myTurret.Azimuth;
				return;
			}

			// CurrentTarget may be removed by WeaponTargeting
			Target GotTarget = CurrentTarget;
			if (!GotTarget.FiringDirection.HasValue || !GotTarget.InterceptionPoint.HasValue)
			{
				StopFiring();
				return;
			}

			// check firing direction
			Vector3 directionBlock;
			Vector3.CreateFromAzimuthAndElevation(myTurret.Azimuth, myTurret.Elevation, out directionBlock);

			//Vector3 directionBlock = direction;
			//direction = RelativeVector3F.createFromBlock(direction, weapon, false).getWorld();
			//myLogger.debugLog("direction block = " + directionBlock + ", direction world = " + direction, "RotateAndFire()");

			Vector3 directionWorld = weapon.directionToWorld(directionBlock);

			//myLogger.debugLog("forward = " + WorldMatrix.Forward + ", Up = " + WorldMatrix.Up + ", right = " + WorldMatrix.Right, "RotateAndFire()");
			myLogger.debugLog("direction block = " + directionBlock + ", direction world = " + directionWorld, "RotateAndFire()");

			CheckFire(directionWorld);

			if (myTurret.AIEnabled)
			{
				myTurret.SetTarget(GotTarget.InterceptionPoint.Value);
				return;
			}

			Vector3 RotateTo = RelativeVector3F.createFromWorld(GotTarget.FiringDirection.Value, weapon.CubeGrid).getBlock(weapon);
			float targetElevation, targetAzimuth; // the position of the target
			Vector3.GetAzimuthAndElevation(RotateTo, out targetAzimuth, out targetElevation);
			if (!targetElevation.IsValid() || !targetAzimuth.IsValid())
			{
				myLogger.debugLog("cannot rotate, invalid el(" + targetElevation + ") or az(" + targetAzimuth + ")", "RotateAndFire()");
				return;
			}

			// should azimuth rotate past 180°?
			if (Can360 && Math.Abs(setAzimuth - targetAzimuth) > Math.PI)
			{
				if (targetAzimuth < 0)
					targetAzimuth += MathHelper.TwoPi;
				else
					targetAzimuth -= MathHelper.TwoPi;
			}

			// add amount based on speed
			float nextElevation, nextAzimuth;
			if (targetElevation > setElevation)
				nextElevation = Math.Min(targetElevation, setElevation + speedElevation);
			else if (targetElevation < setElevation)
				nextElevation = Math.Max(targetElevation, setElevation - speedElevation);
			else
				nextElevation = setElevation;

			if (targetAzimuth > setAzimuth)
				nextAzimuth = Math.Min(targetAzimuth, setAzimuth + speedAzimuth);
			else if (targetAzimuth < setAzimuth)
				nextAzimuth = Math.Max(targetAzimuth, setAzimuth - speedAzimuth);
			else
				nextAzimuth = setAzimuth;

			// impose limits
			if (nextElevation < minElevation)
				nextElevation = minElevation;
			else if (nextElevation > maxElevation)
				nextElevation = maxElevation;

			if (!Can360)
			{
				if (nextAzimuth < minAzimuth)
					nextAzimuth = minAzimuth;
				else if (nextAzimuth > maxAzimuth)
					nextAzimuth = maxAzimuth;
			}

			//myLogger.debugLog("next elevation = " + nextElevation + ", next azimuth = " + nextAzimuth, "RotateAndFire()");

			// apply changes
			if (nextElevation != myTurret.Elevation)
			{
				myTurret.Elevation = nextElevation;
				myTurret.SyncElevation();
			}
			//else
			//	myLogger.debugLog("not setting elevation", "RotateAndFire()");
			if (nextAzimuth != myTurret.Azimuth)
			{
				myTurret.Azimuth = nextAzimuth;
				myTurret.SyncAzimuth();
			}
			//else
			//	myLogger.debugLog("not setting azimuth", "RotateAndFire()");

			setElevation = nextElevation;
			setAzimuth = nextAzimuth;
		}
	}
}
