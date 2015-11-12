#define LOG_ENABLED //remove on build

using System;
using System.Collections.Generic;
using Sandbox.Common.ObjectBuilders;
using Sandbox.Definitions;
using Sandbox.ModAPI;
using VRage.ModAPI;
using VRageMath;
using Ingame = Sandbox.ModAPI.Ingame;

namespace Rynchodon.Weapons
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

		private bool Initialized = false;

		private Logger myLogger;

		public Turret(IMyCubeBlock block)
			: base(block)
		{
			myLogger = new Logger("Turret", () => block.CubeGrid.DisplayName, () => block.DefinitionDisplayNameText, () => block.getNameOnly());
			Registrar.Add(CubeBlock, this);
			//myLogger.debugLog("definition limits = " + definition.MinElevationDegrees + ", " + definition.MaxElevationDegrees + ", " + definition.MinAzimuthDegrees + ", " + definition.MaxAzimuthDegrees, "Turret()");
			//myLogger.debugLog("radian limits = " + minElevation + ", " + maxElevation + ", " + minAzimuth + ", " + maxAzimuth, "Turret()");
		}

		private void Initialize()
		{
			// definition limits
			MyLargeTurretBaseDefinition definition = CubeBlock.GetCubeBlockDefinition() as MyLargeTurretBaseDefinition;

			if (definition == null)
				throw new NullReferenceException("definition");

			minElevation = (float)((float)definition.MinElevationDegrees / 180 * Math.PI); // Math.Max((float)definition.MinElevationDegrees / 180 * Math.PI, -0.6);
			maxElevation = (float)Math.Min((float)definition.MaxElevationDegrees / 180 * Math.PI, 0.6); // 0.6 was determined empirically
			minAzimuth = (float)((float)definition.MinAzimuthDegrees / 180 * Math.PI);
			maxAzimuth = (float)((float)definition.MaxAzimuthDegrees / 180 * Math.PI);

			Can360 = Math.Abs(definition.MaxAzimuthDegrees - definition.MinAzimuthDegrees) >= 360;

			// speeds are in rads per ms (from S.E. source)
			speedElevation = definition.ElevationSpeed * 100f / 6f;
			speedAzimuth = definition.RotationSpeed * 100f / 6f;

			setElevation = myTurret.Elevation;
			setAzimuth = myTurret.Azimuth;

			AllowedState = State.Targeting;
			Initialized = true;

			myLogger.debugLog("definition limits = " + definition.MinElevationDegrees + ", " + definition.MaxElevationDegrees + ", " + definition.MinAzimuthDegrees + ", " + definition.MaxAzimuthDegrees, "Turret()");
			myLogger.debugLog("radian limits = " + minElevation + ", " + maxElevation + ", " + minAzimuth + ", " + maxAzimuth, "Turret()");
		}

		/// <summary>
		/// Fill CanTarget from turret
		/// </summary>
		protected override void Update_Options(TargetingOptions Options)
		{
			//Options. CanTarget = TargetType.None;
			MyObjectBuilder_TurretBase builder = CubeBlock.GetObjectBuilder_Safe() as MyObjectBuilder_TurretBase;
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

			Options.TargetingRange = myTurret.Range;

			//myLogger.debugLog("CanTarget = " + Options.CanTarget, "TargetOptionsFromTurret()");
		}

		protected override bool CanRotateTo(Vector3D targetPoint)
		{
			//Vector3 RotateTo = Vector3.Normalize(RelativeVector3F.createFromWorld(targetPoint, weapon.CubeGrid).getBlock(weapon));
			Vector3 RotateToDirection = Vector3.Normalize(RelativeDirection3F.FromWorld(CubeBlock.CubeGrid, targetPoint).ToBlockNormalized(CubeBlock));

			float azimuth, elevation;
			Vector3.GetAzimuthAndElevation(RotateToDirection, out azimuth, out elevation);

			//myLogger.debugLog("target azimuth: " + azimuth + ", elevation: " + elevation, "CanRotateTo()");

			if (elevation < minElevation || elevation > maxElevation || azimuth < minAzimuth || azimuth > maxAzimuth)
			{
				myLogger.debugLog("cannot rotate to " + targetPoint + ", azimuth: " + azimuth + ", elevation: " + elevation, "CanRotateTo()");
				return false;
			}
			return true;
		}

		/// <remarks>
		/// Must execute regularly on game thread.
		/// </remarks>
		protected override void Update()
		{
			if (!Initialized)
				Initialize();

			if (CurrentState_NotFlag(State.Targeting))
			{
				setElevation = myTurret.Elevation;
				setAzimuth = myTurret.Azimuth;
				return;
			}

			// CurrentTarget may be changed by WeaponTargeting
			Target GotTarget = CurrentTarget;
			if (GotTarget.Entity == null)
			{
				FireWeapon = false;
				return;
			}
			if (!GotTarget.FiringDirection.HasValue || !GotTarget.InterceptionPoint.HasValue) // happens alot
				return;

			// check firing direction
			Vector3 directionBlock;
			Vector3.CreateFromAzimuthAndElevation(myTurret.Azimuth, myTurret.Elevation, out directionBlock);

			//Vector3 directionWorld = weapon.directionToWorld(directionBlock);
			Vector3 directionWorld = RelativeDirection3F.FromBlock(CubeBlock, directionBlock).ToWorldNormalized();

			//myLogger.debugLog("forward = " + WorldMatrix.Forward + ", Up = " + WorldMatrix.Up + ", right = " + WorldMatrix.Right, "RotateAndFire()");
			myLogger.debugLog("direction block = " + directionBlock + ", direction world = " + directionWorld, "RotateAndFire()");

			CheckFire(directionWorld);

			if (myTurret.AIEnabled)
			{
				myTurret.SetTarget(GotTarget.InterceptionPoint.Value);
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
