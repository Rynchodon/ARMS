using System;
using System.Collections.Generic;
using Sandbox.Definitions;
using Sandbox.Game.Entities;
using VRage.Game;

namespace Rynchodon.Weapons
{
	public class WeaponDefinitionExpanded
	{

		private static Dictionary<MyWeaponDefinition, WeaponDefinitionExpanded> known = new Dictionary<MyWeaponDefinition, WeaponDefinitionExpanded>();

		static WeaponDefinitionExpanded()
		{
			MyEntities.OnCloseAll += MyEntities_OnCloseAll;
		}

		private static void MyEntities_OnCloseAll()
		{
			MyEntities.OnCloseAll -= MyEntities_OnCloseAll;
			known = null;
		}

		public static implicit operator WeaponDefinitionExpanded(MyWeaponDefinition weaponDefn)
		{
			if (known == null)
				return null;

			WeaponDefinitionExpanded result;
			if (known.TryGetValue(weaponDefn, out result))
				return result;

			result = new WeaponDefinitionExpanded(weaponDefn);
			known.Add(weaponDefn, result);
			return result;
		}

		public readonly MyWeaponDefinition WeaponDefinition;
		public readonly MyAmmoType AmmoType;
		public readonly float RequiredAccuracy;
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
					RequiredAccuracy = (float)Math.Cos(0.02f + Math.Min(weaponDefn.WeaponAmmoDatas[i].RateOfFire, 1000) / 72000f);
					return;
				}
			}

			AmmoType = MyAmmoType.Unknown;
			RequiredAccuracy = 2f;
		}

	}
}
