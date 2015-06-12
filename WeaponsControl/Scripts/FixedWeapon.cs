using System;
using System.Collections.Generic;
using Sandbox.Common.ObjectBuilders;
using Sandbox.ModAPI;
using VRage.ModAPI;
using VRage.ObjectBuilders;
using VRageMath;

namespace Rynchodon.Weapons
{
	/// <summary>
	/// For rotor-turrets and Autopilot-usable weapons.
	/// </summary>
	public class FixedWeapon : WeaponTargeting
	{
		private IMyMotorStator StatorA, StatorB;
		private bool StatorReversed_Elevation = false, StatorReversed_Azimuth = false;

		//private CubeGridCache myCache;

		private Logger myLogger;

		public FixedWeapon(IMyCubeBlock block)
			: base(block)
		{
			myLogger = new Logger("FixedWeapon", block);
			myLogger.debugLog("Initialized!", "FixedWeapon()");
		}

		protected override bool CanRotateTo(VRageMath.Vector3D targetPoint)
		{
			return false;
		}

		protected override void Update_Options(TargetingOptions current)
		{ }

		protected override void Update()
		{
			if (weapon.CubeGrid.IsStatic)
				return;

			SearchForRotors();
		}

		private void SearchForRotors()
		{
			
		}

		private readonly MyObjectBuilderType[] types_Rotor = new MyObjectBuilderType[] { typeof(MyObjectBuilder_MotorRotor), typeof(MyObjectBuilder_MotorAdvancedRotor), typeof(MyObjectBuilder_MotorStator), typeof(MyObjectBuilder_MotorAdvancedStator) };

		/// <param name="grid">where to search</param>
		/// <param name="ignore">block to ignore</param>
		/// <returns>stator found on grid or stator attached to rotor on grid</returns>
		private bool GetStatorRotor(IMyCubeGrid grid, out IMyMotorStator Stator, out IMyCubeBlock Rotor, IMyMotorStator IgnoreStator = null, IMyCubeBlock IgnoreRotor = null)
		{
			CubeGridCache cache = CubeGridCache.GetFor(grid);
			foreach (MyObjectBuilderType type in types_Rotor)
			{
				ReadOnlyList<IMyCubeBlock> AllMotorParts = cache.GetBlocksOfType(type);

				if (AllMotorParts != null && AllMotorParts.Count > 0)
				{
					foreach (IMyCubeBlock MotorPart in AllMotorParts)
					{
						myLogger.debugLog("Weapon Forward = " + weapon.WorldMatrix.Forward + ", Left = " + weapon.WorldMatrix.Left + ", Up = " + weapon.WorldMatrix.Up, "SearchForRotors()");

						Stator = MotorPart as IMyMotorStator;
						if (Stator != null)
						{
							if (Stator == IgnoreStator)
								continue;
							if (!TryGetRotor(Stator, out Rotor))
								continue;
							return true;
						}
						else
						{
							Rotor = MotorPart;
							if (Rotor == IgnoreRotor)
								continue;
							if (!TryGetStator(Rotor, out Stator))
								continue;
							return true;
						}
					}
				}
			}
			Stator = null;
			Rotor = null;
			return false;
		}

		/// <summary>
		/// Tries to get a rotor from a stator. Will fail if stator is not attached or attached rotor cannot be retreived from MyAPIGateway.Entities.
		/// </summary>
		/// <param name="stator">stator attached to the rotor</param>
		/// <param name="rotor">rotor attached to the stator</param>
		/// <returns>true iff successful</returns>
		private bool TryGetRotor(IMyMotorStator stator, out IMyCubeBlock rotor)
		{
			if (!stator.IsAttached)
			{
				myLogger.debugLog("stator is not attached " + stator.DisplayNameText, "TryGetRotor()", Logger.severity.DEBUG);
				rotor = null;
				return false;
			}

			MyObjectBuilder_MotorStator statorBuilder = (stator as IMyCubeBlock).GetSlimObjectBuilder_Safe() as MyObjectBuilder_MotorStator; // could this ever be null?
			IMyEntity rotorE;
			if (!MyAPIGateway.Entities.TryGetEntityById(statorBuilder.RotorEntityId, out rotorE))
			{
				myLogger.debugLog("Entities does not contain an entity with ID " + statorBuilder.RotorEntityId, "TryGetRotor()", Logger.severity.DEBUG);
				rotor = null;
				return false;
			}

			rotor = rotorE as IMyCubeBlock;
			return true;
		}

		/// <summary>
		/// Tries to get a stator from a rotor. Will fail if supplied block is not a rotor, rotor is not attached, 
		/// </summary>
		/// <param name="rotor">rotor attached to the stator</param>
		/// <param name="stator">stator attached to the rotor</param>
		/// <returns>true iff successful</returns>
		/// <exception cref="ArgumentException">If rotor is not an actual rotor.</exception>
		private bool TryGetStator(IMyCubeBlock rotor, out IMyMotorStator stator)
		{
			var TypeId = rotor.BlockDefinition.TypeId;
			if (TypeId != typeof(MyObjectBuilder_MotorRotor) && TypeId != typeof(MyObjectBuilder_MotorAdvancedRotor))
				throw new ArgumentException("value is not a rotor", "rotor");

			BoundingBoxD AABB = rotor.WorldAABB;
			List<IMyEntity> Entities = MyAPIGateway.Entities.GetEntitiesInAABB_Safe(ref AABB);
			foreach (IMyEntity entity in Entities)
			{
				IMyMotorStator entityAsStator = entity as IMyMotorStator;
				if (entityAsStator == null || !entityAsStator.IsAttached)
					continue;

				if (!entityAsStator.IsAttached)
				{
					myLogger.debugLog("found unattached stator: " + entityAsStator.DisplayNameText, "TryGetStator()", Logger.severity.DEBUG);
					continue;
				}

				MyObjectBuilder_MotorStator statorBuilder = (entityAsStator as IMyCubeBlock).GetSlimObjectBuilder_Safe() as MyObjectBuilder_MotorStator; // could this ever be null?
				if (statorBuilder.RotorEntityId == rotor.EntityId)
				{
					stator = entityAsStator;
					return true;
				}
			}

			myLogger.debugLog("did not find any attached stator", "TryGetStator()", Logger.severity.DEBUG);
			stator = null;
			return false;
		}
	}
}
