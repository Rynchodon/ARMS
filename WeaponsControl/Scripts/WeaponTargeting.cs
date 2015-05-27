#define LOG_ENABLED //remove on build

using System;
using System.Collections.Generic;
using Sandbox.Common.ObjectBuilders;
using Sandbox.ModAPI;
using VRageMath;
using Ingame = Sandbox.ModAPI.Ingame;

namespace Rynchodon.Autopilot.Weapons
{
	/// <summary>
	/// Contains functions that are common to turrets and fixed weapons
	/// </summary>
	public class WeaponTargeting
	{
		#region Targeting Options

		[Flags]
		public enum TargetType : byte
		{
			None = 0x0,
			Missile = 0x1,
			Meteor = 0x2,
			Character = 0x4,
			Moving = 0x8,
			LargeGrid = 0x10,
			SmallGrid = 0x20,
			Station = 0x40
		}

		public float Range;
		public TargetType CanTarget = TargetType.None;

		public bool CanTargetType(TargetType type)
		{ return (CanTarget & type) > 0; }

		#endregion

		private readonly IMyCubeBlock weapon;
		private readonly Ingame.IMyLargeTurretBase myTurret;

		private Logger myLogger;

		public WeaponTargeting(IMyCubeBlock weapon)
		{
			this.weapon = weapon;
			this.myTurret = weapon as Ingame.IMyLargeTurretBase;
			this.myLogger = new Logger("WeaponTargeting", () => weapon.CubeGrid.DisplayName, () => weapon.DisplayNameText);
		}

		/// <summary>
		/// <para>Test line segment between weapon and target for obstructing entities.</para>
		/// <para>Tests for obstructing voxel map, non-hostile character, or non-hostile grid.</para>
		/// </summary>
		/// <param name="targetPos">entity to shoot</param>
		/// <param name="ignoreSourceGrid">ignore intersections with grid that weapon is part of</param>
		public bool CanShootAt(Vector3D targetPos, bool ignoreSourceGrid = false)
		{
			if (weapon == null)
				throw new ArgumentNullException("weapon");

			Vector3D weaponPos = weapon.GetPosition();

			// Voxel Test
			Vector3 boundary;
			if (MyAPIGateway.Entities.RayCastVoxel(weaponPos, targetPos, out boundary))
				return false;

			// Get entities in AABB
			BoundingBoxD AABB = BoundingBoxD.CreateFromPoints(new Vector3D[] { weaponPos, targetPos });
			HashSet<IMyEntity> entitiesInAABB = new HashSet<IMyEntity>();
			MyAPIGateway.Entities.GetEntitiesInAABB_Safe_NoBlock(AABB, entitiesInAABB,
				(entity) => { return entity is IMyCubeGrid || entity is IMyCharacter; });
			if (entitiesInAABB.Count == 0)
				return true;

			LineD laser = new LineD(weaponPos, targetPos);
			Vector3I position = new Vector3I();
			double distance = 0;

			// Test each entity
			foreach (IMyEntity entity in entitiesInAABB)
			{
				if (weapon.canConsiderHostile(entity))
					continue;

				IMyCharacter asChar = entity as IMyCharacter;
				if (asChar != null)
				{
					if (entity.WorldAABB.Intersects(laser, out distance))
						return false;
					continue;
				}

				IMyCubeGrid asGrid = entity as IMyCubeGrid;
				if (asGrid != null)
				{
					if (asGrid.GetLineIntersectionExactGrid(ref laser, ref position, ref distance))
						return false;
					continue;
				}
			}

			// no obstruction found
			return true;
		}

		#region Target Prediction

		/// <summary>
		/// Determines the direction a projectile must be fired in to hit the target.
		/// </summary>
		/// <param name="target">target</param>
		/// <param name="projectileSpeed">the speed of the projectile</param>
		/// <returns>the required direction</returns>
		/// Do we also need point of intersection? - This test might preceed CanShootAt, then we could test the trajectory not sight-to-target
		public Vector3? FiringDirection(IMyEntity target, float projectileSpeed)
		{
			if (weapon == null)
				throw new ArgumentNullException("attacker");
			if (target == null)
				throw new ArgumentNullException("target");

			Vector3D relativePos = target.GetPosition() - weapon.GetPosition();
			Vector3 relativeVelocity = target.GetLinearVelocity() - weapon.GetLinearVelocity();

			return FiringDirection(relativePos, relativeVelocity, projectileSpeed);
		}

		/// <summary>
		/// Gets the smallest positive root of the quadratic equation.
		/// </summary>
		private static float? SmallPositiveRoot(float a, float b, float c)
		{
			float sqrt = b * b - 4 * a * c;
			if (sqrt < 0)
				return null;
			sqrt = (float)Math.Sqrt(sqrt);

			float root = -b - sqrt / (2 * a);
			if (root > 0)
				return root;
			root = -b + sqrt / (2 * a);
			if (root > 0)
				return root;

			return null;
		}

		/// <remarks>
		/// Based on http://stackoverflow.com/a/17749335
		/// </remarks>
		private static Vector3? FiringDirection(Vector3 relativePosition, Vector3 relativeVelocity, float projectileSpeed)
		{
			float a = projectileSpeed * projectileSpeed - relativeVelocity.LengthSquared();
			float b = -2 * relativePosition.Dot(relativeVelocity);
			float c = -relativePosition.LengthSquared();

			float? time = SmallPositiveRoot(a, b, c);
			if (time == null)
				return null;

			return relativeVelocity + relativePosition / time.Value;
		}

		#endregion
	}
}
