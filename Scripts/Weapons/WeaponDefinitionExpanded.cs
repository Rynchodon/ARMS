
using System;
using Sandbox.Definitions;
using VRage.Game;

namespace Rynchodon.Weapons
{
	public class WeaponDefinitionExpanded
	{

		public static implicit operator WeaponDefinitionExpanded(MyWeaponDefinition weaponDefn)
		{
			return new WeaponDefinitionExpanded(weaponDefn);
		}

		public readonly MyWeaponDefinition WeaponDefinition;
		public readonly MyAmmoType AmmoType;
		public readonly float RequiredAccuracy;
		public readonly Ammo FirstAmmo;

		public MyWeaponDefinition.MyWeaponAmmoData WeaponAmmoData
		{
			get { return WeaponDefinition.WeaponAmmoDatas[(int)AmmoType]; }
		}

		public WeaponDefinitionExpanded(MyWeaponDefinition weaponDefn)
		{
			this.WeaponDefinition = weaponDefn;

			for (int i = 0; i < weaponDefn.WeaponAmmoDatas.Length; i++)
			{
				if (weaponDefn.WeaponAmmoDatas[i] != null)
				{
					AmmoType = (MyAmmoType)i;
					FirstAmmo = Ammo.GetAmmo(WeaponDefinition.AmmoMagazinesId[0]);
					RequiredAccuracy = (float)Math.Cos(0.02f + Math.Min(weaponDefn.WeaponAmmoDatas[i].RateOfFire, 1000) / 72000f);
					return;
				}
			}

			AmmoType = MyAmmoType.Unknown;
			RequiredAccuracy = 2f;
		}

	}
}
