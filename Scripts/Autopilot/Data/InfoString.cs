using System;
using System.Collections.Generic;
using Sandbox.Game.Localization;
using VRage;

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
			WelderNotFinished = 128
		}

		public enum StringId_Jump : byte
		{
			None, NotCharged, InGravity, ClosingGrid, StaticGrid, AlreadyJumping, DestOutsideWorld, CannotJumpMin, Obstructed, Failed, Jumping
		}

		private static Dictionary<StringId, string> m_strings = new Dictionary<StringId, string>();
		private static Dictionary<StringId_Jump, string> m_jumpStrings = new Dictionary<StringId_Jump, string>();

		static InfoString()
		{
			m_strings.Add(StringId.None, string.Empty);
			m_strings.Add(StringId.ReturnCause_Full, "Drills are full, need to unload");
			m_strings.Add(StringId.ReturnCause_Heavy, "Ship mass is too great for thrusters");
			m_strings.Add(StringId.ReturnCause_OverWorked, "Thrusters overworked, need to unload");
			m_strings.Add(StringId.NoOreFound, "No ore found");
			m_strings.Add(StringId.FighterUnarmed, "Fighter is unarmed");
			m_strings.Add(StringId.FighterNoPrimary, "Fighter has no weapon to aim");
			m_strings.Add(StringId.FighterNoWeapons, "Fighter has no usable weapons");
			m_strings.Add(StringId.WelderNotFinished, "Welder not able to finish");

			m_jumpStrings.Add(StringId_Jump.NotCharged, "No charged jump drives");
			m_jumpStrings.Add(StringId_Jump.InGravity, MyTexts.GetString(MySpaceTexts.NotificationCannotJumpFromGravity.String));
			m_jumpStrings.Add(StringId_Jump.ClosingGrid, "Destruction preventing jump");
			m_jumpStrings.Add(StringId_Jump.StaticGrid, "Stations cannot jump");
			m_jumpStrings.Add(StringId_Jump.AlreadyJumping, "Jump in progress");
			m_jumpStrings.Add(StringId_Jump.DestOutsideWorld, MyTexts.GetString(MySpaceTexts.NotificationCannotJumpOutsideWorld.String));
			m_jumpStrings.Add(StringId_Jump.CannotJumpMin, "Jump drive cannot jump minimum distance");
			m_jumpStrings.Add(StringId_Jump.Obstructed, "Jump is obstructed");
			m_jumpStrings.Add(StringId_Jump.Failed, MyTexts.GetString(MySpaceTexts.NotificationJumpAborted.String));
			m_jumpStrings.Add(StringId_Jump.Jumping, "Jumping");
		}

		public static string GetString(StringId f)
		{
			return m_strings[f];
		}

		public static ICollection<StringId> AllStringIds()
		{
			return m_strings.Keys;
		}

		public static string GetString(StringId_Jump j)
		{
			return m_jumpStrings[j];
		}

	}
}
