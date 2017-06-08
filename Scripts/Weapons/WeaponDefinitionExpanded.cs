using System;
using System.Collections.Generic;
using Sandbox.Definitions;
using VRage.Game;

namespace Rynchodon.Weapons
{
	public class WeaponDefinitionExpanded
	{

		private static Dictionary<MyWeaponDefinition, WeaponDefinitionExpanded> known = new Dictionary<MyWeaponDefinition, WeaponDefinitionExpanded>();

		public static implicit operator WeaponDefinitionExpanded(MyWeaponDefinition weaponDefn)
		{
			WeaponDefinitionExpanded result;
			if (known.TryGetValue(weaponDefn, out result))
				return result;

			result = new WeaponDefinitionExpanded(weaponDefn);
			known.Add(weaponDefn, result);
			return result;
		}

		public readonly MyWeaponDefinition WeaponDefinition;
		public readonly MyAmmoType AmmoType;
		public readonly float RequiredAccuracyRadians;
		public readonly float RequiredAccuracyCos;
		public readonly Ammo FirstAmmo;

		public MyWeaponDefinition.MyWeaponAmmoData WeaponAmmoData
		{
			get { return WeaponDefinition.WeaponAmmoDatas[(int)AmmoType]; }
		}

		private WeaponDefinitionExpanded(MyWeaponDefinition weaponDefn)
		{
			this.WeaponDefinition = weaponDefn;

			for (int i = 0; i < weaponDefn.WeaponAmmoDatas.Length; i++)
			{
				if (weaponDefn.WeaponAmmoDatas[i] != null)
				{
					AmmoType = (MyAmmoType)i;
					FirstAmmo = Ammo.GetAmmo(WeaponDefinition.AmmoMagazinesId[0]);

					RequiredAccuracyRadians = FirstAmmo.Description != null ? 
						0.15f : // fire guided missile with less accuracy, despite slower RoF
						0.02f + Math.Min(weaponDefn.WeaponAmmoDatas[i].RateOfFire, 1000) / 72000f;
					RequiredAccuracyCos = (float)Math.Cos(RequiredAccuracyRadians);
					return;
				}
			}

			AmmoType = MyAmmoType.Unknown;
			RequiredAccuracyCos = 2f;
		}

	}
}
