using System;
using System.Collections.Generic;
using Sandbox.ModAPI;

namespace Rynchodon.Autopilot.Data
{
	/// <summary>
	/// Strings for appending to custom info.
	/// </summary>
	public static class InfoString
	{

		[Flags]
		public enum StringId : ushort
		{
			None = 0,
			ReturnCause_Full = 1,
			ReturnCause_Heavy = 2,
			ReturnCause_OverWorked = 4,
			NoOreFound = 8,
			FighterUnarmed = 16,
			FighterNoPrimary = 32,
			FighterNoWeapons = 64,
			WelderNotFinished = 128,
		}

		private static Dictionary<StringId, string> m_strings = new Dictionary<StringId, string>();

		static InfoString()
		{
			MyAPIGateway.Entities.OnCloseAll += Entities_OnCloseAll;

			m_strings.Add(StringId.None, string.Empty);
			m_strings.Add(StringId.ReturnCause_Full, "Drills are full, need to unload");
			m_strings.Add(StringId.ReturnCause_Heavy, "Ship mass is too great for thrusters");
			m_strings.Add(StringId.ReturnCause_OverWorked, "Thrusters overworked, need to unload");
			m_strings.Add(StringId.NoOreFound, "No ore found");
			m_strings.Add(StringId.FighterUnarmed, "Fighter is unarmed");
			m_strings.Add(StringId.FighterNoPrimary, "Fighter has no weapon to aim");
			m_strings.Add(StringId.FighterNoWeapons, "Fighter has no usable weapons");
			m_strings.Add(StringId.WelderNotFinished, "Welder not able to finish");
		}

		private static void Entities_OnCloseAll()
		{
			MyAPIGateway.Entities.OnCloseAll -= Entities_OnCloseAll;
			m_strings = null;
		}

		public static string GetString(StringId f)
		{
			return m_strings[f];
		}

		public static ICollection<StringId> AllStringIds()
		{
			return m_strings.Keys;
		}

	}
}
