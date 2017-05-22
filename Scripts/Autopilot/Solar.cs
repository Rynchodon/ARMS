using Rynchodon.Utility;
using Rynchodon.Utility.Network;
using Sandbox.Game.Entities.Cube;
using Sandbox.Game.Gui;
using Sandbox.ModAPI;
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

		[OnWorldLoad]
		private static void CreateTerminal()
		{
			Logger.DebugLog("entered", Logger.severity.TRACE);
			TerminalControlHelper.EnsureTerminalControlCreated<MySolarPanel>();
			TerminalControlHelper.EnsureTerminalControlCreated<MyOxygenFarm>();

			MyTerminalControlFactory.AddControl(new MyTerminalControlSeparator<MySolarPanel>());
			MyTerminalControlFactory.AddControl(new MyTerminalControlSeparator<MyOxygenFarm>());

			MyTerminalControlCheckbox<MyTerminalBlock> s_termControl_faceSun = new MyTerminalControlCheckbox<MyTerminalBlock>("FaceSun", MyStringId.GetOrCompute("Face Sun"), MyStringId.GetOrCompute("Face this block towards the sun"));
			new ValueSync<bool, Solar>(s_termControl_faceSun, (script) => script.m_termControl_faceSun, (script, value) => script.m_termControl_faceSun = value);

			MyTerminalControlFactory.AddControl<MyTerminalBlock, MySolarPanel>(s_termControl_faceSun);
			MyTerminalControlFactory.AddControl<MyTerminalBlock, MyOxygenFarm>(s_termControl_faceSun);
		}

		private readonly IMyCubeBlock myBlock;

		private MotorTurret myMotorTurret;
		private byte sinceNameChange = 0;

		private bool m_nameCommand_faceSun;
		private bool m_termControl_faceSun;

		private Logable Log { get { return new Logable(myBlock); } }

		/// <param name="block">Must be an IMyTerminalBlock</param>
		public Solar(IMyCubeBlock block)
		{
			myBlock = block;
			(myBlock as IMyTerminalBlock).CustomNameChanged += Solar_CustomNameChanged;
			myBlock.OnClose += myBlock_OnClose;

			Registrar.Add(block, this);
		}

		private void myBlock_OnClose(VRage.ModAPI.IMyEntity obj)
		{
			(myBlock as IMyTerminalBlock).CustomNameChanged -= Solar_CustomNameChanged;
			myBlock.OnClose -= myBlock_OnClose;
		}

		public void Update100()
		{
			if (m_termControl_faceSun)
			{
				FaceSun();
				return;
			}

			if (m_nameCommand_faceSun)
				FaceSun();
			else
			{
				Log.DebugLog("no longer facing the sun", Logger.severity.DEBUG, condition: myMotorTurret != null);
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
				Log.DebugLog("Now facing sun", Logger.severity.DEBUG);
				myMotorTurret = new MotorTurret(myBlock);
			}

			myMotorTurret.FaceTowards(SunProperties.SunDirection);
		}

		private void Solar_CustomNameChanged(IMyTerminalBlock obj)
		{ sinceNameChange = 0; }

	}
}
