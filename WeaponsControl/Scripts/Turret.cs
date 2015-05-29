#define LOG_ENABLED //remove on build

using System;
using Sandbox.Common.ObjectBuilders;
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
		private Logger myLogger;

		public Turret(IMyCubeBlock block)
			: base(block)
		{
			myLogger = new Logger("Turret", () => block.CubeGrid.DisplayName, () => block.DefinitionDisplayNameText, () => block.getNameOnly());
			myTurret.SetTarget(Vector3D.Zero);
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

			RotateToTarget();
			// fire of course
		}

		public new void Update10()
		{ base.Update10(); }

		public new void Update100()
		{
			base.Update100();

			TargetOptionsFromTurret();
			myLogger.debugLog("CanTarget: " + CanTarget, "Update100()");
		}

		private void RotateToTarget()
		{
			Vector3? RotateTo = FiringDirection;
			if (!RotateTo.HasValue)
				return;

			myTurret.SetTarget(RotateTo.Value);
			return;

			float azimuth, elevation;
			Vector3.GetAzimuthAndElevation(RotateTo.Value, out azimuth, out elevation);
			if (azimuth == float.NaN || elevation == float.NaN)
			{
				myLogger.debugLog("cannot rotate, invalid az(" + azimuth + ") or el(" + elevation + ")", "RotateToTarget()");
				return;
			}

			myTurret.Azimuth = azimuth;
			myTurret.SyncAzimuth();
			myTurret.Elevation = elevation;
			myTurret.SyncElevation();

			myLogger.debugLog("Firing Direction = " + FiringDirection + ", Rotating to az = " + azimuth + ", and el = " + elevation, "RotateToTarget()");
		}
	}
}
