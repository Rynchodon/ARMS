using System.Collections.Generic;
using Rynchodon.GUI.Control;
using Sandbox.Common;
using Sandbox.Game.Entities.Cube;
using Sandbox.Game.Gui;
using Sandbox.Game.Weapons;
using Sandbox.ModAPI;
using VRage.ModAPI;
using VRage.Utils;
using Ingame = Sandbox.ModAPI.Ingame;

namespace Rynchodon.GUI
{
	[MySessionComponentDescriptor(MyUpdateOrder.AfterSimulation)]
	public class Initializer : MySessionComponentBase
	{

		private readonly Logger m_logger;
		private MyGuiScreenTextPanel m_screenText;
		private byte index; // if we go over max, need to add indecies for different types
		private bool initialized;

		public Initializer()
		{
			m_logger = new Logger(GetType().Name);
		}

		public override void UpdateAfterSimulation()
		{
			if (initialized || MyAPIGateway.Entities == null || MyAPIGateway.Multiplayer == null || MyAPIGateway.Utilities == null)
				return;

			if (IsLoaded())
				m_logger.debugLog("already loaded", "UpdateAfterSimulation()");
			else
			{
				m_logger.debugLog("loading", "UpdateAfterSimulation()");

				LoadTurret();

				AddEntities();
			}

			initialized = true;
		}

		private void AddEntities()
		{
			m_logger.debugLog("adding entities", "AddEntities()", Logger.severity.INFO);

			HashSet<IMyEntity> entities = new HashSet<IMyEntity>();
			MyAPIGateway.Entities.GetEntities(entities);
			foreach (IMyEntity ent in entities)
				OnEntityAdd(ent);
			MyAPIGateway.Entities.OnEntityAdd += OnEntityAdd;
		}

		private void OnEntityAdd(IMyEntity obj)
		{
			IMyCubeGrid grid = obj as IMyCubeGrid;
			if (grid == null)
				return;

			List<IMySlimBlock> blocks = new List<IMySlimBlock>();
			grid.GetBlocks(blocks);
			foreach (IMySlimBlock blo in blocks)
				OnBlockAdded(blo);
			grid.OnBlockAdded += OnBlockAdded;

		}

		private void OnBlockAdded(IMySlimBlock block)
		{
			if (block.FatBlock is Ingame.IMyLargeTurretBase)
			{
				m_logger.debugLog("turret: " + block.FatBlock.DisplayNameText + '/' + block.FatBlock.DefinitionDisplayNameText, "Entities_OnEntityAdd()");
				new TerminalBlockSync(block.FatBlock);
			}
		}

		private bool IsLoaded()
		{
			List<ITerminalControl> controls = new List<ITerminalControl>();
			MyTerminalControlFactory.GetControls(typeof(MyLargeTurretBase), controls);

			foreach (var cont in controls)
				if (cont.Id == "ARMS_Control")
					return true;

			return false;
		}

		private void LoadTurret()
		{
			m_logger.debugLog("loading turret controls", "LoadTurret()", Logger.severity.DEBUG);

			AddCheckbox<MyLargeTurretBase>("ARMS Control", "Lets ARMS control this turret");
			AddCheckbox<MyLargeTurretBase>("Interior Turret", "Reduces obstruction tests");
			AddCheckbox<MyLargeTurretBase>("Target Functional", "Turret will shoot all functional blocks");
			AddCheckbox<MyLargeTurretBase>("Destroy Everything", "Turret will destroy every terminal block");
		}

		private void AddCheckbox<T>(string name, string tooltip = null) where T : MyTerminalBlock
		{
			byte index = this.index++;
			if (tooltip == null)
				tooltip = name;

			m_logger.debugLog("adding checkbox for " + typeof(T) + ", index: " + index + ", name: " + name + ", tooltip: " + tooltip, "AddOnOff()");

			TerminalControlCheckbox<T> checkbox = new TerminalControlCheckbox<T>(name.Replace(' ', '_'), MyStringId.GetOrCompute(name), MyStringId.GetOrCompute(tooltip));
			checkbox.Getter = block => {
				TerminalBlockSync tbs;
				if (Registrar_GUI.TryGetValue(block, out tbs))
					return tbs.GetValue<bool>(index);
				m_logger.alwaysLog("TerminalBlockSync not in Registrar, id: " + block.EntityId, "AddOnOff<T>()", Logger.severity.ERROR);
				return false;
			};
			checkbox.Setter = (block, value) => {
				TerminalBlockSync tbs;
				if (Registrar_GUI.TryGetValue(block, out tbs))
				{
					tbs.SetValue<bool>(index, value);
					return;
				}
				m_logger.alwaysLog("TerminalBlockSync not in Registrar, id: " + block.EntityId, "AddOnOff<T>()", Logger.severity.ERROR);
			};

			checkbox.EnableAction();
			MyTerminalControlFactory.AddControl<T>(checkbox);
		}

		private void AddOnOff<T>(string name) where T : MyTerminalBlock
		{
			byte index = this.index++;

			m_logger.debugLog("adding on off switch for " + typeof(T) + ", index: " + index + ", name: " + name, "AddOnOff()");

			TerminalControlOnOffSwitch<T> onOff = new TerminalControlOnOffSwitch<T>(name.Replace(' ', '_'), MyStringId.GetOrCompute(name));
			onOff.Getter = block => {
				TerminalBlockSync tbs;
				if (Registrar_GUI.TryGetValue(block, out tbs))
					return tbs.GetValue<bool>(index);
				m_logger.alwaysLog("TerminalBlockSync not in Registrar, id: " + block.EntityId, "AddOnOff<T>()", Logger.severity.ERROR);
				return false;
			};
			onOff.Setter = (block, value) => {
				TerminalBlockSync tbs;
				if (Registrar_GUI.TryGetValue(block, out tbs))
				{
					tbs.SetValue<bool>(index, value);
					return;
				}
				m_logger.alwaysLog("TerminalBlockSync not in Registrar, id: " + block.EntityId, "AddOnOff<T>()", Logger.severity.ERROR);
			};

			onOff.EnableToggleAction();
			onOff.EnableOnOffActions();
			MyTerminalControlFactory.AddControl<T>(onOff);
		}

	}
}
