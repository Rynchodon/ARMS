using System;
using System.Collections.Generic;
using Rynchodon.Utility.GUI;
using Rynchodon.Utility.Network;
using Sandbox.Game.Entities.Cube;
using Sandbox.Game.Gui;
using Sandbox.Game.Weapons;
using Sandbox.ModAPI;
using VRage.ModAPI;
using VRage.Utils;

namespace Rynchodon.Utility.GUI
{
	public class TurretControlPanel : SyncBlock
	{

		private class StructReference<T> where T : struct { public T Item; }

		private static Logger s_logger = new Logger("TurretControlPanel");

		static TurretControlPanel()
		{
			List<ITerminalControl> otherControls = new List<ITerminalControl>();
			MyTerminalControlFactory.GetControls(typeof(MyLargeTurretBase), otherControls);

			bool alreadyLoaded = false;
			foreach (var control in otherControls)
			{
				s_logger.debugLog("control: " + control.Id + ", multi: " + control.SupportsMultipleBlocks, "TurretControlPanel()");
				if (control.Id == "ARMS_Control")
					alreadyLoaded = true;
			}

			if (alreadyLoaded)
			{
				s_logger.debugLog("Already loaded", "TurretControlPanel()", Logger.severity.INFO);
				return;
			}

			StructReference<bool> srb = new StructReference<bool>();
			var ARMS_Control = new TerminalControlOnOffSwitch<MyLargeTurretBase>("ARMS_Control", MyStringId.GetOrCompute("ARMS Control"));
			ARMS_Control.Getter = (x) => {
				//s_logger.debugLog("entered", "ARMS_Control.Getter()");
				return srb.Item;
			};
			ARMS_Control.Setter = (x, v) => {
				//s_logger.debugLog("entered, v: " + v, "ARMS_Control.Setter()");
				srb.Item = v;
			};
			ARMS_Control.EnableToggleAction();
			ARMS_Control.EnableOnOffActions();
			MyTerminalControlFactory.AddControl<MyLargeTurretBase>(ARMS_Control);

			srb = new StructReference<bool>();
			var Target_Functional = new TerminalControlOnOffSwitch<MyLargeTurretBase>("Target_Functional", MyStringId.GetOrCompute("Target Functional"));
			Target_Functional.Getter = (x) => {
				//s_logger.debugLog("entered", "Target_Functional.Getter()");
				return srb.Item;
			};
			Target_Functional.Setter = (x, v) => {
				//s_logger.debugLog("entered, v: " + v, "Target_Functional.Setter()");
				srb.Item = v;
			};
			Target_Functional.EnableToggleAction();
			Target_Functional.EnableOnOffActions();
			MyTerminalControlFactory.AddControl<MyLargeTurretBase>(Target_Functional);

			srb = new StructReference<bool>();
			var Interior = new TerminalControlOnOffSwitch<MyLargeTurretBase>("Interior", MyStringId.GetOrCompute("Interior"));
			Interior.Getter = (x) => {
				//s_logger.debugLog("entered", "Interior.Getter()");
				return srb.Item;
			};
			Interior.Setter = (x, v) => {
				//s_logger.debugLog("entered, v: " + v, "Interior.Setter()");
				srb.Item = v;
			};
			Interior.EnableToggleAction();
			Interior.EnableOnOffActions();
			MyTerminalControlFactory.AddControl<MyLargeTurretBase>( Interior);

			srb = new StructReference<bool>();
			var Destroy = new TerminalControlOnOffSwitch<MyLargeTurretBase>("Destroy", MyStringId.GetOrCompute("Destroy"));
			Destroy.Getter = (x) => {
				//s_logger.debugLog("entered", "Destroy.Getter()");
				return srb.Item;
			};
			Destroy.Setter = (x, v) => {
				//s_logger.debugLog("entered, v: " + v, "Destroy.Setter()");
				srb.Item = v;
			};
			Destroy.EnableToggleAction();
			Destroy.EnableOnOffActions();
			MyTerminalControlFactory.AddControl<MyLargeTurretBase>( Destroy);

			//var showButton = new MyTerminalControlButton<MyLargeTurretBase>("ShowTextPanel", MySpaceTexts.BlockPropertyTitle_TextPanelShowTextPanel, MySpaceTexts.Blank, (x) => x.OpenWindow(true, true, false));
			//showButton.Enabled = (x) => !x.IsOpen;
			//showButton.SupportsMultipleBlocks = false;
			//MyTerminalControlFactory.AddControl(showButton);

			MyAPIGateway.Entities.OnCloseAll += Entities_OnCloseAll;
		}

		private static void Entities_OnCloseAll()
		{
			MyAPIGateway.Entities.OnCloseAll -= Entities_OnCloseAll;
			s_logger = null;
		}

		//private static TurretControlPanel GetFromId(IMyEntity entity)
		//{
		//	SyncBlock sb;
		//	if (!Registrar.TryGetValue(entity.EntityId, out sb))
		//		throw new NullReferenceException("Turret does not exist: " + entity.EntityId);

		//	TurretControlPanel t = sb as TurretControlPanel;
		//	if (t == null)
		//		throw new NullReferenceException("SyncBlock is not a TurretControlPanel");

		//	return t;
		//}

		private readonly Logger m_logger;
		private readonly List<bool> m_bool_list;

		public bool ARMS_Control
		{
			get { return m_bool_list[0]; }
			set
			{
				m_bool_list[0] = value;
				SendToServer<bool>(0);
			}
		}

		public bool Target_Functional
		{
			get { return m_bool_list[1]; }
			set
			{
				m_bool_list[1] = value;
				SendToServer<bool>(1);
			}
		}

		public bool Interior
		{
			get { return m_bool_list[2]; }
			set
			{
				m_bool_list[2] = value;
				SendToServer<bool>(2);
			}
		}

		public bool Destroy
		{
			get { return m_bool_list[3]; }
			set
			{
				m_bool_list[3] = value;
				SendToServer<bool>(3);
			}
		}

		public TurretControlPanel(IMyCubeBlock turret)
			: base(turret)
		{
			m_logger = new Logger(GetType().Name, turret);
			m_bool_list = GetList<bool>();
			while (m_bool_list.Count < 4)
				m_bool_list.Add(false);

			m_logger.debugLog("Initialized", "TurretControlPanel()");


			List<ITerminalControl> allControls = new List<ITerminalControl>();
			MyTerminalControlFactory.GetControls(typeof(MyLargeTurretBase), allControls);
			foreach (var control in allControls)
				m_logger.debugLog("control: " + control.Id + ", multi: " + control.SupportsMultipleBlocks + ", visible: " + control.IsVisible(turret as MyTerminalBlock), "TurretControlPanel()");

		}

	}
}
