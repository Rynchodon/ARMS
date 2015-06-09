#define LOG_ENABLED //remove on build

using System;
using Sandbox.Common.ObjectBuilders;
using Sandbox.Definitions;
using Sandbox.ModAPI;
using VRageMath;
using Ingame = Sandbox.ModAPI.Ingame;

namespace Rynchodon.Autopilot.Weapons
{
	/// <summary>
	/// Class is designed to replace / merge with TurretBase
	/// </summary>
	/// TODO: if turret breaks, restore default targeting, apply BROKEN state
	public class Turret : WeaponTargeting
	{
		/// <summary>limits to determine whether or not a turret can face a target</summary>
		private readonly float minElevation, maxElevation, minAzimuth, maxAzimuth;
		/// <summary>speeds are in rads per update</summary>
		private readonly float speedElevation, speedAzimuth;
		/// <summary>the turret is capable of rotating past ±180° (azimuth)</summary>
		private readonly bool Can360;
		/// <summary>value set by Turret, updated when not controlling</summary>
		private float setElevation, setAzimuth;

		private bool ran_SetDefault = false;

		private Logger myLogger;

		public Turret(IMyCubeBlock block)
			: base(block)
		{
			myLogger = new Logger("Turret", () => block.CubeGrid.DisplayName, () => block.DefinitionDisplayNameText, () => block.getNameOnly());

			// definition limits
			MyLargeTurretBaseDefinition definition = MyDefinitionManager.Static.GetCubeBlockDefinition(weapon.getSlimObjectBuilder()) as MyLargeTurretBaseDefinition;

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

			//myLogger.debugLog("definition limits = " + definition.MinElevationDegrees + ", " + definition.MaxElevationDegrees + ", " + definition.MinAzimuthDegrees + ", " + definition.MaxAzimuthDegrees, "Turret()");
			//myLogger.debugLog("radian limits = " + minElevation + ", " + maxElevation + ", " + minAzimuth + ", " + maxAzimuth, "Turret()");
		}

		private void SetDefault()
		{
			if (string.IsNullOrWhiteSpace(weapon.DisplayNameText))
				return;

			myLogger.debugLog("assessment: " + (!weapon.OwnedNPC()) + ", " + (!weapon.DisplayNameText.Contains("[")) + ", " + (!weapon.DisplayNameText.Contains("]")), "SetDefault()");
			if (!weapon.OwnedNPC() && !weapon.DisplayNameText.Contains("[") && !weapon.DisplayNameText.Contains("]"))
			{
				myLogger.debugLog("writing defaults", "Turret()");
				// write defaults
				(weapon as IMyTerminalBlock).SetCustomName(weapon.DisplayNameText + Settings.GetSettingString(Settings.SettingName.sSmartTurretDefaultPlayer));
			}
			else
			{
				myLogger.debugLog("not writing defaults: "+weapon.DisplayNameText, "Turret()");
			}

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

			ran_SetDefault = true;
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

			//myLogger.debugLog("CanTarget = " + Options.CanTarget, "TargetOptionsFromTurret()");
		}

		public new void Update1()
		{
			base.Update1();

			RotateAndFire();
		}

		//public new void Update10()
		//{
		//	base.Update10();
		//}

		public new void Update100()
		{
			if (!ran_SetDefault)
				SetDefault();

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

		/// <summary>
		/// Handles rotating the turret and firing the weapon. Must be called every update to keep setElevation & setAzimuth up-to-date.
		/// </summary>
		private void RotateAndFire()
		{
			if (!IsControllingWeapon)
			{
				setElevation = myTurret.Elevation;
				setAzimuth = myTurret.Azimuth;
				return;
			}

			if (!CurrentTarget.FiringDirection.HasValue)
			{
				StopFiring();
				return;
			}

			// check firing direction
			Vector3 direction;
			Vector3.CreateFromAzimuthAndElevation(myTurret.Azimuth, myTurret.Elevation, out direction);

			Vector3 directionBlock = direction;
			direction = RelativeVector3F.createFromBlock(direction, weapon, false).getWorld();
			//myLogger.debugLog("direction block = " + directionBlock + ", direction world = " + direction, "RotateAndFire()");

			CheckFire(direction);


			if (myTurret.AIEnabled)
			{
				myTurret.SetTarget(CurrentTarget.InterceptionPoint.Value);
				return;
			}


			// no-A.I. turret

			Vector3 RotateTo = RelativeVector3F.createFromWorld(CurrentTarget.FiringDirection.Value, weapon.CubeGrid).getBlock(weapon);
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
