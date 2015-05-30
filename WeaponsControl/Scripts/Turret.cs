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
	public class Turret : WeaponTargeting
	{
		/// <summary>
		/// limits to determine whether or not a turret can face a target
		/// </summary>
		private float minElevation, maxElevation, minAzimuth, maxAzimuth;

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
			maxElevation = (float)Math.Min(definition.MaxElevationDegrees / 180 * Math.PI, Math.PI / 2);
			minAzimuth = (float)(definition.MinAzimuthDegrees / 180 * Math.PI);
			maxAzimuth = (float)(definition.MaxAzimuthDegrees / 180 * Math.PI);

			myLogger.debugLog("definition limits = " + definition.MinElevationDegrees + ", " + definition.MaxElevationDegrees + ", " + definition.MinAzimuthDegrees + ", " + definition.MaxAzimuthDegrees, "DelayedInit()");
			myLogger.debugLog("radian limits = " + minElevation + ", " + maxElevation + ", " + minAzimuth + ", " + maxAzimuth, "DelayedInit()");
		}

		/// <summary>
		/// Fill CanTarget from turret
		/// </summary>
		public void TargetOptionsFromTurret()
		{
			CanTarget = WeaponTargeting.TargetType.None;
			MyObjectBuilder_TurretBase builder = weapon.GetSlimObjectBuilder_Safe() as MyObjectBuilder_TurretBase;
			if (builder.TargetMissiles)
				CanTarget |= WeaponTargeting.TargetType.Missile;
			if (builder.TargetMeteors)
				CanTarget |= WeaponTargeting.TargetType.Meteor;
			if (builder.TargetCharacters)
				CanTarget |= WeaponTargeting.TargetType.Character;
			if (builder.TargetMoving)
				CanTarget |= WeaponTargeting.TargetType.Moving;
			if (builder.TargetLargeGrids)
				CanTarget |= WeaponTargeting.TargetType.LargeGrid;
			if (builder.TargetSmallGrids)
				CanTarget |= WeaponTargeting.TargetType.SmallGrid;
			if (builder.TargetStations)
				CanTarget |= WeaponTargeting.TargetType.Station;

			TargetingRange = myTurret.Range;
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
			base.Update100();

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

		private void RotateAndFire()
		{
			if (!CurrentTarget.FiringDirection.HasValue)
			{
				CheckFire(1f);
				return;
			}

			Vector3 RotateTo = RelativeVector3F.createFromWorld(CurrentTarget.FiringDirection.Value, weapon.CubeGrid).getBlock(weapon);
			float azimuth, elevation;
			Vector3.GetAzimuthAndElevation(RotateTo, out azimuth, out elevation);
			if (!azimuth.IsValid() || !elevation.IsValid())
			{
				myLogger.debugLog("cannot rotate, invalid az(" + azimuth + ") or el(" + elevation + ")", "RotateAndFire()");
				return;
			}

			//if (myTurret.AIEnabled)
				myTurret.SetTarget(CurrentTarget.InterceptionPoint.Value);
			//else
			//{
			//	myTurret.Azimuth = azimuth;
			//	myTurret.SyncAzimuth();
			//	myTurret.Elevation = elevation;
			//	myTurret.SyncElevation();
			//}

			//myLogger.debugLog("Firing Direction = " + FiringDirection + ", Rotating to az = " + azimuth + ", and el = " + elevation, "RotateAndFire()");

			Vector2 Difference = new Vector2(elevation - myTurret.Elevation, azimuth - myTurret.Azimuth);
			//myLogger.debugLog("Difference = " + Difference, "RotateAndFire()");
			CheckFire(Difference.LengthSquared());
		}
	}
}
