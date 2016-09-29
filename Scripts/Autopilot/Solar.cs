using Rynchodon.Utility.Network;
using Sandbox.Game.Entities.Cube;
using Sandbox.Game.Gui;
using Sandbox.ModAPI;
using Sandbox.ModAPI.Interfaces.Terminal;
using SpaceEngineers.Game.Entities.Blocks;
using VRage.Game.ModAPI;
using VRage.Utils;

namespace Rynchodon.Autopilot
{
	/// <summary>
	/// For any block that may be directed to point at the sun using motors.
	/// </summary>
	public class Solar
	{

		private static MyTerminalControlCheckbox<MyTerminalBlock> s_termControl_faceSun;

		static Solar()
		{
			MyTerminalControlFactory.AddControl(new MyTerminalControlSeparator<MySolarPanel>());
			MyTerminalControlFactory.AddControl(new MyTerminalControlSeparator<MyOxygenFarm>());

			s_termControl_faceSun = new MyTerminalControlCheckbox<MyTerminalBlock>("FaceSun", MyStringId.GetOrCompute("Face Sun"), MyStringId.GetOrCompute("Face this block towards the sun"));
			IMyTerminalValueControl<bool> valueControl = s_termControl_faceSun as IMyTerminalValueControl<bool>;
			valueControl.Getter = GetFaceSun;
			valueControl.Setter = SetFaceSun;

			MyTerminalControlFactory.AddControl<MyTerminalBlock, MySolarPanel>(s_termControl_faceSun);
			MyTerminalControlFactory.AddControl<MyTerminalBlock, MyOxygenFarm>(s_termControl_faceSun);

			MyAPIGateway.Entities.OnCloseAll += Entities_OnCloseAll;
		}

		private static void Entities_OnCloseAll()
		{
			MyAPIGateway.Entities.OnCloseAll -= Entities_OnCloseAll;
			s_termControl_faceSun = null;
		}

		private static bool GetFaceSun(IMyTerminalBlock block)
		{
			Solar instance;
			if (!Registrar.TryGetValue(block, out instance))
			{
				(new Logger()).alwaysLog("Failed to get instance from " + block.EntityId, Logger.severity.WARNING);
				return false;
			}

			return instance.m_termControl_faceSun.Value;
		}

		private static void SetFaceSun(IMyTerminalBlock block, bool value)
		{
			Solar instance;
			if (!Registrar.TryGetValue(block, out instance))
			{
				(new Logger()).alwaysLog("Failed to get instance from " + block.EntityId, Logger.severity.WARNING);
				return;
			}

			instance.m_termControl_faceSun.Value = value;
		}

		private readonly IMyCubeBlock myBlock;
		private readonly Logger myLogger;

		private MotorTurret myMotorTurret;
		private byte sinceNameChange = 0;

		private bool m_nameCommand_faceSun;
		private EntityValue<bool> m_termControl_faceSun;

		/// <param name="block">Must be an IMyTerminalBlock</param>
		public Solar(IMyCubeBlock block)
		{
			myBlock = block;
			myLogger = new Logger(block);
			(myBlock as IMyTerminalBlock).CustomNameChanged += Solar_CustomNameChanged;
			myBlock.OnClose += myBlock_OnClose;
			m_termControl_faceSun = new EntityValue<bool>(block, 0, () => s_termControl_faceSun.UpdateVisual());

			Registrar.Add(block, this);
		}

		private void myBlock_OnClose(VRage.ModAPI.IMyEntity obj)
		{
			(myBlock as IMyTerminalBlock).CustomNameChanged -= Solar_CustomNameChanged;
			myBlock.OnClose -= myBlock_OnClose;
		}

		public void Update100()
		{
			if (m_termControl_faceSun.Value)
			{
				FaceSun();
				return;
			}

			if (m_nameCommand_faceSun)
				FaceSun();
			else
			{
				myLogger.debugLog("no longer facing the sun", Logger.severity.DEBUG, condition: myMotorTurret != null);
				if (myMotorTurret != null)
				{
					myMotorTurret.Dispose();
					myMotorTurret = null;
				}
			}

			if (sinceNameChange > 2)
				return;

			if (sinceNameChange++ < 2)
				return;

			m_nameCommand_faceSun = myBlock.DisplayNameText.looseContains("[face sun]");
		}

		private void FaceSun()
		{
			if (myMotorTurret == null)
			{
				myLogger.debugLog("Now facing sun", Logger.severity.DEBUG);
				myMotorTurret = new MotorTurret(myBlock) { RotationSpeedMultiplier = 2f, SpeedLimit = 2f };
			}

			myMotorTurret.FaceTowards(SunProperties.SunDirection);
		}

		private void Solar_CustomNameChanged(IMyTerminalBlock obj)
		{ sinceNameChange = 0; }

	}
}
