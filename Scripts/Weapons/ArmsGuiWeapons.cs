
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

		private static ITerminalProperty<bool> TP_ARMS_Control, TP_Interior_Turret, TP_Target_Functional, TP_Destroy_Everything,
			TP_Target_Missiles, TP_Target_Meteors, TP_Target_Characters, TP_Target_Moving, TP_Target_Large_Ships, TP_Target_Small_Ships, TP_Target_Stations;
		private static ITerminalProperty<float> TP_Aiming_Radius;

		private static Logger s_logger = new Logger("ArmsGuiWeapons");
		private static bool intialized;

		//public static bool ARMS_Control(Ingame.IMyTerminalBlock term)
		//{
		//	if (!intialized)
		//		Init(term);

		//	if (TP_ARMS_Control == null)
		//	{
		//		Globals.GUI_NotLoaded();
		//		return false;
		//	}

		//	return TP_ARMS_Control.GetValue(term);
		//}

		public static bool Interior_Turret(Ingame.IMyTerminalBlock term)
		{
			if (!intialized)
				Init(term);

			if (TP_ARMS_Control == null)
			{
				Globals.GUI_NotLoaded();
				return false;
			}

			return TP_Interior_Turret.GetValue(term);
		}

		public static void UpdateTerm(Ingame.IMyTerminalBlock term, TargetingOptions options)
		{
			if (!intialized)
				Init(term);

			if (TP_ARMS_Control == null)
			{
				Globals.GUI_NotLoaded();
				return;
			}

			SetTargetFlag(TP_ARMS_Control.GetValue(term), options, TargetingFlags.Turret);
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

		private static void Init(Ingame.IMyTerminalBlock term)
		{
			TP_ARMS_Control = term.GetProperty("ARMS_Control").AsBool();
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

		private static void SetTargetFlag(bool setUnset, TargetingOptions options, TargetingFlags flag)
		{
			if (setUnset)
				options.Flags |= flag;
			else
				options.Flags &= ~flag;
		}

		private static void SetTypeFlag(bool setUnset, TargetingOptions options, TargetType type)
		{
			if (setUnset)
				options.CanTarget |= type;
			else
				options.CanTarget &= ~type;
		}
			
	}
}
