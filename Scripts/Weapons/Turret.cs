using System;
using System.Collections.Generic;
using Sandbox.Common.ObjectBuilders;
using Sandbox.Definitions;
using Sandbox.Game.Gui;
using Sandbox.ModAPI;
using Sandbox.ModAPI.Interfaces;
using VRage.ModAPI;
using VRage.Utils;
using VRageMath;
using Ingame = Sandbox.ModAPI.Ingame;

namespace Rynchodon.Weapons
{
	public class Turret : WeaponTargeting
	{

		private static bool s_npcHasOpts;

		static Turret()
		{
			s_npcHasOpts = !string.IsNullOrWhiteSpace(Settings.ServerSettings.GetSettingString(Settings.ServerSettings.SettingName.sWeaponCommandsNPC));
		}

		/// <summary>vanilla property</summary>
		private static ITerminalProperty<bool> TP_TargetMissiles, TP_TargetMeteors, TP_TargetCharacters, TP_TargetMoving, TP_TargetLargeGrids, TP_TargetSmallGrids, TP_TargetStations;

		/// <summary>limits to determine whether or not a turret can face a target</summary>
		private readonly float minElevation, maxElevation, minAzimuth, maxAzimuth;
		/// <summary>speeds are in rads per update</summary>
		private readonly float speedElevation, speedAzimuth;
		/// <summary>the turret is capable of rotating past ±180° (azimuth)</summary>
		private readonly bool Can360;

		/// <summary>value set by Turret, updated when not controlling</summary>
		private float setElevation, setAzimuth;

		private Logger myLogger;

		public Turret(IMyCubeBlock block)
			: base(block)
		{
			myLogger = new Logger("Turret", block);
			Registrar.Add(CubeBlock, this);

			if (TP_TargetMissiles == null)
			{
				myLogger.debugLog("Filling Terminal Properties", "Turret()", Logger.severity.INFO);
				IMyTerminalBlock term = CubeBlock as IMyTerminalBlock;
				TP_TargetMissiles = term.GetProperty("TargetMissiles").AsBool();
				TP_TargetMeteors = term.GetProperty("TargetMeteors").AsBool();
				TP_TargetCharacters = term.GetProperty("TargetCharacters").AsBool();
				TP_TargetMoving = term.GetProperty("TargetMoving").AsBool();
				TP_TargetLargeGrids = term.GetProperty("TargetLargeShips").AsBool();
				TP_TargetSmallGrids = term.GetProperty("TargetSmallShips").AsBool();
				TP_TargetStations = term.GetProperty("TargetStations").AsBool();
			}

			// definition limits
			MyLargeTurretBaseDefinition definition = CubeBlock.GetCubeBlockDefinition() as MyLargeTurretBaseDefinition;

			if (definition == null)
				throw new NullReferenceException("definition");

			minElevation = Math.Max(MathHelper.ToRadians(definition.MinElevationDegrees), -0.6f);
			maxElevation = MathHelper.ToRadians(definition.MaxElevationDegrees);
			minAzimuth = MathHelper.ToRadians(definition.MinAzimuthDegrees);
			maxAzimuth = MathHelper.ToRadians(definition.MaxAzimuthDegrees);

			Can360 = Math.Abs(definition.MaxAzimuthDegrees - definition.MinAzimuthDegrees) >= 360;

			// speeds are in rads per ms (from S.E. source)
			speedElevation = definition.ElevationSpeed * 100f / 6f;
			speedAzimuth = definition.RotationSpeed * 100f / 6f;

			setElevation = myTurret.Elevation;
			setAzimuth = myTurret.Azimuth;

			//if (CubeBlock.OwnedNPC() && s_npcHasOpts && ArmsGuiWeapons.GetPropertyValue(myTurret, ref ArmsGuiWeapons.TP_ARMS_Control))
			//	myTurret.ApplyAction("ARMS_Control");
			//if (myTurret is Ingame.IMyLargeInteriorTurret && myTurret.BlockDefinition.SubtypeName == "LargeInteriorTurret" && !ArmsGuiWeapons.GetPropertyValue(myTurret, ref ArmsGuiWeapons.TP_Interior_Turret))
			//	myTurret.ApplyAction("Interior_Turret");

			myLogger.debugLog("definition limits = " + definition.MinElevationDegrees + ", " + definition.MaxElevationDegrees + ", " + definition.MinAzimuthDegrees + ", " + definition.MaxAzimuthDegrees, "Turret()");
			myLogger.debugLog("radian limits = " + minElevation + ", " + maxElevation + ", " + minAzimuth + ", " + maxAzimuth, "Turret()");
		}

		/// <summary>
		/// Fill CanTarget from turret
		/// </summary>
		protected override void Update100_Options_TargetingThread(TargetingOptions Options)
		{
			if (TP_TargetMissiles.GetValue(CubeBlock))
				Options.CanTarget |= TargetType.Missile;
			if (TP_TargetMeteors.GetValue(CubeBlock))
				Options.CanTarget |= TargetType.Meteor;
			if (TP_TargetCharacters.GetValue(CubeBlock))
				Options.CanTarget |= TargetType.Character;
			if (TP_TargetMoving.GetValue(CubeBlock))
				Options.CanTarget |= TargetType.Moving;
			if (TP_TargetLargeGrids.GetValue(CubeBlock))
				Options.CanTarget |= TargetType.LargeGrid;
			if (TP_TargetSmallGrids.GetValue(CubeBlock))
				Options.CanTarget |= TargetType.SmallGrid;
			if (TP_TargetStations.GetValue(CubeBlock))
				Options.CanTarget |= TargetType.Station;

			Options.TargetingRange = myTurret.Range;

			if (myTurret is Ingame.IMyLargeInteriorTurret && myTurret.BlockDefinition.SubtypeName == "LargeInteriorTurret")
				Options.Flags |= TargetingFlags.Interior;

			//myLogger.debugLog("CanTarget = " + Options.CanTarget, "TargetOptionsFromTurret()");
		}

		protected override bool CanRotateTo(Vector3D targetPoint)
		{
			Vector3 localTarget = Vector3.Transform(targetPoint, CubeBlock.WorldMatrixNormalizedInv);
			localTarget.Normalize();

			float azimuth, elevation;
			Vector3.GetAzimuthAndElevation(localTarget, out azimuth, out elevation);

			//myLogger.debugLog("target azimuth: " + azimuth + ", elevation: " + elevation, "CanRotateTo()");

			if (elevation < minElevation)
			{
				myLogger.debugLog("Cannot rotate to " + targetPoint + ", local: " + localTarget + ", elevation: " + elevation + " below min: " + minElevation, "CanRotateTo()");
				return false;
			}
			if (elevation > maxElevation)
			{
				myLogger.debugLog("Cannot rotate to " + targetPoint + ", local: " + localTarget + ", elevation: " + elevation + " above max: " + maxElevation, "CanRotateTo()");
				return false;
			}

			if (Can360)
				return true;
			if (azimuth < minAzimuth)
			{
				myLogger.debugLog("Cannot rotate to " + targetPoint + ", local: " + localTarget + ", azimuth: " + azimuth + " below min: " + minAzimuth, "CanRotateTo()");
				return false;
			}
			if (azimuth > maxAzimuth)
			{
				myLogger.debugLog("Cannot rotate to " + targetPoint + ", local: " + localTarget + ", azimuth: " + azimuth + " above max: " + maxAzimuth, "CanRotateTo()");
				return false;
			}
			return true;
		}

		protected override Vector3 Facing()
		{
			Vector3 directionBlock;
			Vector3.CreateFromAzimuthAndElevation(myTurret.Azimuth, myTurret.Elevation, out directionBlock);

			myLogger.debugLog("azimuth: " + myTurret.Azimuth + " elevation: " + myTurret.Elevation + ", direction block: " + directionBlock + ", direction world: " + RelativeDirection3F.FromBlock(CubeBlock, directionBlock).ToWorldNormalized(), "Facing()");

			return RelativeDirection3F.FromBlock(CubeBlock, directionBlock).ToWorldNormalized();
		}

		/// <remarks>
		/// Must execute regularly on game thread.
		/// </remarks>
		protected override void Update1_GameThread()
		{
			if (CurrentControl == Control.Off)
			{
				if (!myTurret.AIEnabled)
				{
					setElevation = myTurret.Elevation;
					setAzimuth = myTurret.Azimuth;
				}
				return;
			}

			// CurrentTarget may be changed by WeaponTargeting
			Target GotTarget = CurrentTarget;
			if (GotTarget.Entity == null)
			{
				FireWeapon = false;
				return;
			}
			if (!GotTarget.FiringDirection.HasValue || !GotTarget.ContactPoint.HasValue) // happens alot
				return;

			//// check firing direction
			//Vector3 directionBlock;
			//Vector3.CreateFromAzimuthAndElevation(myTurret.Azimuth, myTurret.Elevation, out directionBlock);

			////Vector3 directionWorld = weapon.directionToWorld(directionBlock);
			//Vector3 directionWorld = RelativeDirection3F.FromBlock(CubeBlock, directionBlock).ToWorldNormalized();

			////myLogger.debugLog("forward = " + WorldMatrix.Forward + ", Up = " + WorldMatrix.Up + ", right = " + WorldMatrix.Right, "RotateAndFire()");
			//myLogger.debugLog("direction block = " + directionBlock + ", direction world = " + directionWorld, "RotateAndFire()");

			//CheckFire(directionWorld);

			if (myTurret.AIEnabled)
			{
				// need to find out if SE adjusts for our velocity
				myTurret.SetTarget(GotTarget.ContactPoint.Value);
				return;
			}

			//Vector3 RotateTo = RelativeVector3F.createFromWorld(GotTarget.FiringDirection.Value, weapon.CubeGrid).getBlock(weapon);
			Vector3 RotateToDirection = RelativeDirection3F.FromWorld(CubeBlock.CubeGrid, GotTarget.FiringDirection.Value).ToBlockNormalized(CubeBlock);
			myLogger.debugLog("FiringDirection = " + GotTarget.FiringDirection.Value + ", RotateToDirection = " + RotateToDirection, "Update()");

			float targetElevation, targetAzimuth; // the position of the target
			Vector3.GetAzimuthAndElevation(RotateToDirection, out targetAzimuth, out targetElevation);
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
