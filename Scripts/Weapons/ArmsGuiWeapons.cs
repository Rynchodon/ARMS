
using System;
using Sandbox.ModAPI;
using Sandbox.ModAPI.Interfaces;
using Ingame = Sandbox.ModAPI.Ingame;

namespace Rynchodon.Weapons
{

	/// <summary>
	/// Loads options from ARMS GUI
	/// </summary>
	public class ArmsGuiWeapons
	{

		/// <summary>ARMS added property</summary>
		public static ITerminalProperty<bool> TP_ARMS_Control, TP_Rotor_Turret, TP_Interior_Turret, TP_Target_Functional, TP_Destroy_Everything,
			TP_Target_Missiles, TP_Target_Meteors, TP_Target_Characters, TP_Target_Moving, TP_Target_Large_Ships, TP_Target_Small_Ships, TP_Target_Stations;
		/// <summary>ARMS added property</summary>
		public static ITerminalProperty<float> TP_Aiming_Radius;

		private static Logger s_logger = new Logger("ArmsGuiWeapons");
		private static bool intialized;

		/// <summary>
		/// Gets the value of a terminal property.
		/// </summary>
		public static T GetPropertyValue<T>(Ingame.IMyCubeBlock block, ref ITerminalProperty<T> prop)
		{
			if (!intialized)
				Init(block as Ingame.IMyTerminalBlock);

			if (TP_ARMS_Control == null)
			{
				Globals.GUI_NotLoaded();
				return default(T);
			}
			s_logger.debugLog(prop == null, "prop == null", "GetPropertyValue<T>()", Logger.severity.ERROR);

			return prop.GetValue(block);
		}

		/// <summary>
		/// Disables the ARMS control for a block.
		/// </summary>
		/// <param name="term">Block to disable control for.</param>
		public static void DisableArmsControl(Ingame.IMyTerminalBlock term)
		{
			if (!intialized)
				Init(term);

			if (TP_ARMS_Control == null)
			{
				Globals.GUI_NotLoaded();
				return;
			}

			TP_ARMS_Control.SetValue(term, false);
		}

		/// <summary>
		/// Updates options with the ARMS Control property only.
		/// </summary>
		/// <param name="term">The terminal block to get the ARMS control value from.</param>
		/// <param name="options">Options to assign the value to.</param>
		public static void UpdateArmsEnabled(Ingame.IMyTerminalBlock term, TargetingOptions options)
		{
			if (!intialized)
				Init(term);

			if (TP_ARMS_Control == null)
			{
				Globals.GUI_NotLoaded();
				return;
			}

			SetTargetFlag(TP_ARMS_Control.GetValue(term), options, TargetingFlags.ArmsEnabled);
			SetTargetFlag(TP_Rotor_Turret.GetValue(term), options, TargetingFlags.Rotor_Turret);
		}

		/// <summary>
		/// Updates all the terminal properties of a block.
		/// </summary>
		/// <param name="term">The block to get the property values for.</param>
		/// <param name="options">Options to assign the values to.</param>
		public static void UpdateTerm(Ingame.IMyTerminalBlock term, TargetingOptions options)
		{
			if (!intialized)
				Init(term);

			if (TP_ARMS_Control == null)
			{
				Globals.GUI_NotLoaded();
				return;
			}

			SetTargetFlag(TP_ARMS_Control.GetValue(term), options, TargetingFlags.ArmsEnabled);
			SetTargetFlag(TP_Rotor_Turret.GetValue(term), options, TargetingFlags.Rotor_Turret);
			SetTargetFlag(TP_Interior_Turret.GetValue(term), options, TargetingFlags.Interior);
			SetTargetFlag(TP_Target_Functional.GetValue(term), options, TargetingFlags.Functional);
			SetTypeFlag(TP_Destroy_Everything.GetValue(term), options, TargetType.Destroy);

			SetTypeFlag(TP_Target_Missiles.GetValue(term), options, TargetType.Missile);
			SetTypeFlag(TP_Target_Meteors.GetValue(term), options, TargetType.Meteor);
			SetTypeFlag(TP_Target_Characters.GetValue(term), options, TargetType.Character);
			SetTypeFlag(TP_Target_Moving.GetValue(term), options, TargetType.Moving);
			SetTypeFlag(TP_Target_Large_Ships.GetValue(term), options, TargetType.LargeGrid);
			SetTypeFlag(TP_Target_Small_Ships.GetValue(term), options, TargetType.SmallGrid);
			SetTypeFlag(TP_Target_Stations.GetValue(term), options, TargetType.Station);

			options.TargetingRange = TP_Aiming_Radius.GetValue(term);
		}

		/// <summary>
		/// Initialize the terminal property fields.
		/// </summary>
		/// <param name="term">Block to get the terminal properties from.</param>
		private static void Init(Ingame.IMyTerminalBlock term)
		{
			TP_ARMS_Control = term.GetProperty("ARMS_Control").AsBool();
			TP_Rotor_Turret = term.GetProperty("Rotor-Turret").AsBool();
			TP_Interior_Turret = term.GetProperty("Interior_Turret").AsBool();
			TP_Target_Functional = term.GetProperty("Target_Functional").AsBool();
			TP_Destroy_Everything = term.GetProperty("Destroy_Everything").AsBool();

			TP_Target_Missiles = term.GetProperty("Target_Missiles").AsBool();
			TP_Target_Meteors = term.GetProperty("Target_Meteors").AsBool();
			TP_Target_Characters = term.GetProperty("Target_Characters").AsBool();
			TP_Target_Moving = term.GetProperty("Target_Moving").AsBool();
			TP_Target_Large_Ships = term.GetProperty("Target_Large_Ships").AsBool();
			TP_Target_Small_Ships = term.GetProperty("Target_Small_Ships").AsBool();
			TP_Target_Stations = term.GetProperty("Target_Stations").AsBool();

			TP_Aiming_Radius = term.GetProperty("Aiming_Radius").AsFloat();

			s_logger.debugLog(TP_ARMS_Control == null, "TP_ARMS_Control == null", "Update_Options()", Logger.severity.FATAL);
			s_logger.debugLog(TP_Rotor_Turret == null, "TP_Rotor_Turret == null", "Update_Options()", Logger.severity.FATAL);
			s_logger.debugLog(TP_Interior_Turret == null, "TP_Interior_Turret == null", "Update_Options()", Logger.severity.FATAL);
			s_logger.debugLog(TP_Target_Functional == null, "TP_Target_Functional == null", "Update_Options()", Logger.severity.FATAL);
			s_logger.debugLog(TP_Destroy_Everything == null, "TP_Destroy_Everything == null", "Update_Options()", Logger.severity.FATAL);

			s_logger.debugLog(TP_Target_Missiles == null, "TP_Target_Missiles == null", "Update_Options()", Logger.severity.FATAL);
			s_logger.debugLog(TP_Target_Meteors == null, "TP_Target_Meteors == null", "Update_Options()", Logger.severity.FATAL);
			s_logger.debugLog(TP_Target_Characters == null, "TP_Target_Characters == null", "Update_Options()", Logger.severity.FATAL);
			s_logger.debugLog(TP_Target_Moving == null, "TP_Target_Moving == null", "Update_Options()", Logger.severity.FATAL);
			s_logger.debugLog(TP_Target_Large_Ships == null, "TP_Target_Large_Ships == null", "Update_Options()", Logger.severity.FATAL);
			s_logger.debugLog(TP_Target_Small_Ships == null, "TP_Target_Small_Ships == null", "Update_Options()", Logger.severity.FATAL);
			s_logger.debugLog(TP_Target_Stations == null, "TP_Target_Stations == null", "Update_Options()", Logger.severity.FATAL);

			s_logger.debugLog(TP_Aiming_Radius == null, "TP_Aiming_Radius == null", "Update_Options()", Logger.severity.FATAL);

			intialized = true;
		}

		/// <summary>
		/// Sets a targeting flag of options.
		/// </summary>
		private static void SetTargetFlag(bool setUnset, TargetingOptions options, TargetingFlags flag)
		{
			if (setUnset)
				options.Flags |= flag;
			else
				options.Flags &= ~flag;
		}

		/// <summary>
		/// Sets a targeting type flag of options.
		/// </summary>
		private static void SetTypeFlag(bool setUnset, TargetingOptions options, TargetType type)
		{
			if (setUnset)
				options.CanTarget |= type;
			else
				options.CanTarget &= ~type;
		}

	}
}
