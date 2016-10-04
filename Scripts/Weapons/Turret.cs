using System;
using System.Collections.Generic;
using Sandbox.Definitions;
using Sandbox.Game.Entities;
using Sandbox.ModAPI;
using Sandbox.ModAPI.Interfaces;
using VRage.Game.Entity;
using VRage.Game.ModAPI;
using VRageMath;
using Ingame = SpaceEngineers.Game.ModAPI.Ingame;

namespace Rynchodon.Weapons
{
	public class Turret : WeaponTargeting
	{

		/// <summary>vanilla property</summary>
		private static ITerminalProperty<bool> TP_TargetMissiles, TP_TargetMeteors, TP_TargetCharacters, TP_TargetMoving, TP_TargetLargeGrids, TP_TargetSmallGrids, TP_TargetStations, TP_TargetNeutrals;

		private readonly MyEntitySubpart m_barrel;
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
			myLogger = new Logger(block);
			Registrar.Add(CubeBlock, this);

			if (TP_TargetMissiles == null)
			{
				myLogger.debugLog("Filling Terminal Properties", Logger.severity.INFO);
				IMyTerminalBlock term = CubeBlock as IMyTerminalBlock;
				TP_TargetMissiles = term.GetProperty("TargetMissiles").AsBool();
				TP_TargetMeteors = term.GetProperty("TargetMeteors").AsBool();
				TP_TargetCharacters = term.GetProperty("TargetCharacters").AsBool();
				TP_TargetMoving = term.GetProperty("TargetMoving").AsBool();
				TP_TargetLargeGrids = term.GetProperty("TargetLargeShips").AsBool();
				TP_TargetSmallGrids = term.GetProperty("TargetSmallShips").AsBool();
				TP_TargetStations = term.GetProperty("TargetStations").AsBool();
				TP_TargetNeutrals = term.GetProperty("TargetNeutrals").AsBool();
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

			// subparts for turrets form a chain
			var subparts = ((MyCubeBlock)CubeBlock).Subparts;
			while (subparts.Count != 0)
			{
				m_barrel = subparts.FirstPair().Value;
				subparts = m_barrel.Subparts;
			}

			//myLogger.debugLog("definition limits = " + definition.MinElevationDegrees + ", " + definition.MaxElevationDegrees + ", " + definition.MinAzimuthDegrees + ", " + definition.MaxAzimuthDegrees, "Turret()");
			//myLogger.debugLog("radian limits = " + minElevation + ", " + maxElevation + ", " + minAzimuth + ", " + maxAzimuth, "Turret()");
		}

		/// <summary>
		/// Fill CanTarget from turret
		/// </summary>
		protected override void Update100_Options_TargetingThread(TargetingOptions Options)
		{
			SetFlag(TP_TargetMissiles, TargetType.Missile);
			SetFlag(TP_TargetMeteors, TargetType.Meteor);
			SetFlag(TP_TargetCharacters, TargetType.Character);
			SetFlag(TP_TargetMoving, TargetType.Moving);
			SetFlag(TP_TargetLargeGrids, TargetType.LargeGrid);
			SetFlag(TP_TargetSmallGrids, TargetType.SmallGrid);
			SetFlag(TP_TargetStations, TargetType.Station);
			if (TP_TargetNeutrals.GetValue(CubeBlock))
				Options.Flags &= ~TargetingFlags.IgnoreOwnerless;
			else
				Options.Flags |= TargetingFlags.IgnoreOwnerless;

			Options.TargetingRange = myTurret.Range;

			//myLogger.debugLog("CanTarget = " + Options.CanTarget, "TargetOptionsFromTurret()");
		}

		private void SetFlag(ITerminalProperty<bool> prop, TargetType typeFlag)
		{
			if (prop.GetValue(CubeBlock))
				Options.CanTarget |= typeFlag;
			else
				Options.CanTarget &= ~typeFlag;
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
				//myLogger.debugLog("Cannot rotate to " + targetPoint + ", local: " + localTarget + ", elevation: " + elevation + " below min: " + minElevation, "CanRotateTo()");
				return false;
			}
			if (elevation > maxElevation)
			{
				//myLogger.debugLog("Cannot rotate to " + targetPoint + ", local: " + localTarget + ", elevation: " + elevation + " above max: " + maxElevation, "CanRotateTo()");
				return false;
			}

			if (Can360)
				return true;
			if (azimuth < minAzimuth)
			{
				//myLogger.debugLog("Cannot rotate to " + targetPoint + ", local: " + localTarget + ", azimuth: " + azimuth + " below min: " + minAzimuth, "CanRotateTo()");
				return false;
			}
			if (azimuth > maxAzimuth)
			{
				//myLogger.debugLog("Cannot rotate to " + targetPoint + ", local: " + localTarget + ", azimuth: " + azimuth + " above max: " + maxAzimuth, "CanRotateTo()");
				return false;
			}
			return true;
		}

		protected override Vector3D ProjectilePosition()
		{
			return m_barrel.PositionComp.GetPosition();
		}

		protected override Vector3 Facing()
		{
			return m_barrel.PositionComp.WorldMatrix.Forward;
		}

		/// <remarks>
		/// Must execute regularly on game thread.
		/// </remarks>
		protected override void Update1_GameThread()
		{
			if (!MyAPIGateway.Multiplayer.IsServer)
				return;

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
				//FireWeapon = false;
				return;
			}
			if (!GotTarget.FiringDirection.HasValue || !GotTarget.ContactPoint.HasValue) // happens alot
				return;

			// as of SE 01.130.010 SetTarget does not work
			//if (myTurret.AIEnabled)
			//{
			//	myTurret.SetTarget(ProjectilePosition() + GotTarget.FiringDirection.Value * 100f);
			//	return;
			//}

			//Vector3 RotateTo = RelativeVector3F.createFromWorld(GotTarget.FiringDirection.Value, weapon.CubeGrid).getBlock(weapon);
			Vector3 RotateToDirection = RelativeDirection3F.FromWorld(CubeBlock.CubeGrid, GotTarget.FiringDirection.Value).ToBlockNormalized(CubeBlock);
			//myLogger.debugLog("FiringDirection = " + GotTarget.FiringDirection.Value + ", RotateToDirection = " + RotateToDirection, "Update()");

			float targetElevation, targetAzimuth; // the position of the target
			Vector3.GetAzimuthAndElevation(RotateToDirection, out targetAzimuth, out targetElevation);
			if (!targetElevation.IsValid() || !targetAzimuth.IsValid())
			{
				//myLogger.debugLog("cannot rotate, invalid el(" + targetElevation + ") or az(" + targetAzimuth + ")", "RotateAndFire()");
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
