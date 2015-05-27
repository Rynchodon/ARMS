// skip file on build
#define LOG_ENABLED //remove on build

using System;
using Sandbox.Common.ObjectBuilders;
using Sandbox.ModAPI;
using Ingame = Sandbox.ModAPI.Ingame;

namespace Rynchodon.Autopilot.Weapons
{
	/// <summary>
	/// Class is designed to replace / merge with TurretBase
	/// </summary>
	public class NewTurret
	{
		private IMyCubeBlock myBlock;
		private WeaponTargeting myTargeting;

		/// <summary>
		/// Fill CanTarget from turret
		/// </summary>
		public void TargetOptionsFromTurret()
		{
			myTargeting.CanTarget = WeaponTargeting.TargetType.None;
			MyObjectBuilder_TurretBase builder = myBlock.GetSlimObjectBuilder_Safe() as MyObjectBuilder_TurretBase;
			if (builder.TargetMissiles)
				myTargeting.CanTarget |= WeaponTargeting.TargetType.Missile;
			if (builder.TargetMeteors)
				myTargeting.CanTarget |= WeaponTargeting.TargetType.Meteor;
			if (builder.TargetCharacters)
				myTargeting.CanTarget |= WeaponTargeting.TargetType.Character;
			if (builder.TargetMoving)
				myTargeting.CanTarget |= WeaponTargeting.TargetType.Moving;
			if (builder.TargetLargeGrids)
				myTargeting.CanTarget |= WeaponTargeting.TargetType.LargeGrid;
			if (builder.TargetSmallGrids)
				myTargeting.CanTarget |= WeaponTargeting.TargetType.SmallGrid;
			if (builder.TargetStations)
				myTargeting.CanTarget |= WeaponTargeting.TargetType.Station;
		}
	}
}
