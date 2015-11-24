using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Sandbox.Common;
using Sandbox.Common.Components;
using Sandbox.Common.ObjectBuilders;
using Sandbox.Common.ObjectBuilders.Definitions;
using Sandbox.Common.ObjectBuilders.Voxels;
using Sandbox.Common.ObjectBuilders.VRageData;
using Sandbox.Definitions;
using Sandbox.Game;
using Sandbox.Game.Entities;
using Sandbox.Game.Entities.Cube;
using Sandbox.Game.Gui;
using Sandbox.Game.Weapons;
using Sandbox.ModAPI;
using Sandbox.ModAPI.Interfaces;
using VRage;
using VRage.Collections;
using VRage.Components;
using VRage.Game.ObjectBuilders;
using VRage.Library.Utils;
using VRage.ModAPI;
using VRage.ObjectBuilders;
using VRage.Utils;
using VRage.Voxels;
using VRageMath;
using Ingame = Sandbox.ModAPI.Ingame;

namespace Rynchodon.Utility.GUI
{
	public class TerminalControlPanel
	{

		private static Logger s_logger = new Logger("TurretControlPanel");

		static TerminalControlPanel()
		{
			


		}

		private static void MyLargeTurretBase()
		{
			List<ITerminalControl> otherControls = new List<ITerminalControl>();
			MyTerminalControlFactory.GetControls(typeof(MyLargeTurretBase), otherControls);

			bool alreadyLoaded = false;
			foreach (var control in otherControls)
			{
				s_logger.debugLog("control: " + control.Id + ", multi: " + control.SupportsMultipleBlocks, "MyLargeTurretBase()");
				if (control.Id == "ARMS_Control")
					alreadyLoaded = true;
			}

			if (alreadyLoaded)
			{
				s_logger.debugLog("Already loaded", "MyLargeTurretBase()", Logger.severity.INFO);
				return;
			}

			StructReference<bool> srb = new StructReference<bool>();
			var ARMS_Control = new TerminalControlOnOffSwitch<MyLargeTurretBase>("ARMS_Control", MyStringId.GetOrCompute("ARMS Control"));
			ARMS_Control.Getter = (x) => {
				return srb.Item;
			};
			ARMS_Control.Setter = (x, v) => {
				srb.Item = v;
			};
			ARMS_Control.EnableToggleAction();
			ARMS_Control.EnableOnOffActions();
			MyTerminalControlFactory.AddControl<MyLargeTurretBase>(ARMS_Control);

			srb = new StructReference<bool>();
			var Target_Functional = new TerminalControlOnOffSwitch<MyLargeTurretBase>("Target_Functional", MyStringId.GetOrCompute("Target Functional"));
			Target_Functional.Getter = (x) => {
				return srb.Item;
			};
			Target_Functional.Setter = (x, v) => {
				srb.Item = v;
			};
			Target_Functional.EnableToggleAction();
			Target_Functional.EnableOnOffActions();
			MyTerminalControlFactory.AddControl<MyLargeTurretBase>(Target_Functional);

			srb = new StructReference<bool>();
			var Interior = new TerminalControlOnOffSwitch<MyLargeTurretBase>("Interior", MyStringId.GetOrCompute("Interior"));
			Interior.Getter = (x) => {
				return srb.Item;
			};
			Interior.Setter = (x, v) => {
				srb.Item = v;
			};
			Interior.EnableToggleAction();
			Interior.EnableOnOffActions();
			MyTerminalControlFactory.AddControl<MyLargeTurretBase>( Interior);

			srb = new StructReference<bool>();
			var Destroy = new TerminalControlOnOffSwitch<MyLargeTurretBase>("Destroy", MyStringId.GetOrCompute("Destroy"));
			Destroy.Getter = (x) => {
				return srb.Item;
			};
			Destroy.Setter = (x, v) => {
				srb.Item = v;
			};
			Destroy.EnableToggleAction();
			Destroy.EnableOnOffActions();
			MyTerminalControlFactory.AddControl<MyLargeTurretBase>( Destroy);

			//var showButton = new MyTerminalControlButton<MyLargeTurretBase>("ShowTextPanel", MySpaceTexts.BlockPropertyTitle_TextPanelShowTextPanel, MySpaceTexts.Blank, (x) => x.OpenWindow(true, true, false));
			//showButton.Enabled = (x) => !x.IsOpen;
			//showButton.SupportsMultipleBlocks = false;
			//MyTerminalControlFactory.AddControl(showButton);

		}

		private static void AddOnOffSwitch<T>(string id) where T : MyTerminalBlock
		{
			StructReference<bool> srb = new StructReference<bool>();
			var onOffSwitch = new TerminalControlOnOffSwitch<T>(id, MyStringId.GetOrCompute(id));
			onOffSwitch.Getter = x => { return srb.Item;};
			onOffSwitch.Setter = x,v => { srb.Item = v;};
		}

	}
}
