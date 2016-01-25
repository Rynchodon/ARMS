using System.Collections.Generic;
using Rynchodon.GUI.Control;
using Rynchodon.Settings;
using Sandbox.Game.Entities.Cube;
using Sandbox.Game.Gui;
using Sandbox.Game.Weapons;
using Sandbox.ModAPI;
using VRage.Game.Components;
using VRage.ModAPI;
using VRage.Utils;
using VRageMath;
using Ingame = Sandbox.ModAPI.Ingame;

namespace Rynchodon.GUI
{
	[MySessionComponentDescriptor(MyUpdateOrder.AfterSimulation)]
	public class Initializer : MySessionComponentBase
	{

		private enum Stage : byte { None, Running, World_Closed, Terminated }

		private readonly Logger m_logger;
		private byte index; // if we go over max, need to add indecies for different types
		private Stage m_stage;
		private GuiStateSaver saver;

		private float minRange = 0f;
		private float maxRange = 1f;

		public Initializer()
		{
			m_logger = new Logger(GetType().Name);
		}

		public override void UpdateAfterSimulation()
		{
			if (m_stage == Stage.None && WorldReady())
			{
				if (IsLoaded())
					m_stage = Stage.Terminated;
				else
				{
					m_logger.debugLog("loading", "UpdateAfterSimulation()");
					maxRange = ServerSettings.GetSetting<float>(ServerSettings.SettingName.fMaxWeaponRange);
					saver = new GuiStateSaver();
					LoadWeapons();
					OnWorldLoad();
					MyAPIGateway.Entities.OnCloseAll += Entities_OnCloseAll;
				}
			}
		}

		private bool WorldReady()
		{
			return MyAPIGateway.Entities != null && MyAPIGateway.Multiplayer != null && MyAPIGateway.Utilities != null;
		}

		private void OnWorldLoad()
		{
			AddEntities();
			saver.Load();
			m_stage = Stage.Running;
		}

		private void Entities_OnCloseAll()
		{
			MyAPIGateway.Entities.OnEntityAdd -= OnEntityAdd;
			m_stage = Stage.World_Closed;
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
			MyTerminalControlFactory.GetControls(typeof(MyUserControllableGun), controls);

			foreach (var cont in controls)
				if (cont.Id == "ARMS_Control")
					return true;

			return false;
		}

		private void LoadWeapons()
		{
			m_logger.debugLog("loading weapon controls", "LoadWeapons()", Logger.severity.DEBUG);

			AddCheckbox<MyUserControllableGun>("ARMS Control", "For turret, uses ARMS targeting. Otherwise, makes weapon a rotor-turret.");

			AddCheckbox<MyUserControllableGun>("Interior Turret", "Reduces obstruction tests");
			AddCheckbox<MyUserControllableGun>("Target Functional", "Turret will shoot all functional blocks");
			AddCheckbox<MyUserControllableGun>("Destroy Everything", "Turret will destroy every terminal block");

			// turrets already have these
			AddRangeSlider<MyUserControllableGun>("Aiming Radius");
			AddCheckbox<MyUserControllableGun>("Target Missiles");
			AddCheckbox<MyUserControllableGun>("Target Meteors");
			AddCheckbox<MyUserControllableGun>("Target Characters");
			AddCheckbox<MyUserControllableGun>("Target Moving");
			AddCheckbox<MyUserControllableGun>("Target Large Ships");
			AddCheckbox<MyUserControllableGun>("Target Small Ships");
			AddCheckbox<MyUserControllableGun>("Target Stations");
		}

		private void AddCheckbox<T>(string name, string tooltip = null) where T : MyTerminalBlock
		{
			byte index = this.index++;
			if (tooltip == null)
				tooltip = name;

			m_logger.debugLog("adding checkbox for " + typeof(T) + ", index: " + index + ", name: " + name + ", tooltip: " + tooltip, "AddCheckbox()");

			TerminalControlCheckbox<T> checkbox = new TerminalControlCheckbox<T>(name.Replace(' ', '_'), MyStringId.GetOrCompute(name), MyStringId.GetOrCompute(tooltip));
			checkbox.Getter = block => GetTbsValue<bool>(block, index);
			checkbox.Setter = (block, value) => SetTbsValue(block, index, value);
			checkbox.EnableAction();
			MyTerminalControlFactory.AddControl<T>(checkbox);
		}

		private void AddOnOff<T>(string name) where T : MyTerminalBlock
		{
			byte index = this.index++;

			m_logger.debugLog("adding on off switch for " + typeof(T) + ", index: " + index + ", name: " + name, "AddOnOff()");

			TerminalControlOnOffSwitch<T> onOff = new TerminalControlOnOffSwitch<T>(name.Replace(' ', '_'), MyStringId.GetOrCompute(name));
			onOff.Getter = block => GetTbsValue<bool>(block, index);
			onOff.Setter = (block, value) => SetTbsValue(block, index, value);
			onOff.EnableToggleAction();
			onOff.EnableOnOffActions();
			MyTerminalControlFactory.AddControl<T>(onOff);
		}

		private void AddRangeSlider<T>(string name, string tooltip = null) where T : MyTerminalBlock
		{
			byte index = this.index++;
			if (tooltip == null)
				tooltip = name;

			m_logger.debugLog("adding slider for " + typeof(T) + ", index: " + index + ", name: " + name, "AddRangeSlider()");

			TerminalControlSlider<T> slider = new TerminalControlSlider<T>(name.Replace(' ', '_'), MyStringId.GetOrCompute(name), MyStringId.GetOrCompute(tooltip));
			slider.Normalizer = (block, value) => NormalizeRange(value);
			slider.Denormalizer = (block, value) => DenormalizeRange(value);
			slider.DefaultValue = 0f;
			slider.Getter = (block) => GetTbsValue<float>(block, index);
			slider.Setter = (block, value) => SetTbsValue(block, index, value);
			slider.Writer = (block, result) => result.Append((int)GetTbsValue<float>(block, index)).Append(" m");
			MyTerminalControlFactory.AddControl(slider);
		}

		private T GetTbsValue<T>(IMyEntity block, byte index)
		{
			saver.CheckTime();

			TerminalBlockSync tbs;
			if (Registrar_GUI.TryGetValue(block, out tbs))
				return tbs.GetValue<T>(index);

			if (m_stage == Stage.World_Closed && WorldReady())
			{
				OnWorldLoad();
				return GetTbsValue<T>(block, index);
			}

			m_logger.alwaysLog("TerminalBlockSync not in Registrar, id: " + block.EntityId, "GetTbsValue<T>()", Logger.severity.ERROR);
			return default(T);
		}

		private void SetTbsValue<T>(IMyEntity block, byte index, T value)
		{
			saver.CheckTime();

			TerminalBlockSync tbs;
			if (Registrar_GUI.TryGetValue(block, out tbs))
			{
				tbs.SetValue(index, value);
				return;
			}

			if (m_stage == Stage.World_Closed && WorldReady())
			{
				OnWorldLoad();
				SetTbsValue<T>(block, index, value);
				return;
			}

			m_logger.alwaysLog("TerminalBlockSync not in Registrar, id: " + block.EntityId, "SetTbsValue<T>()", Logger.severity.ERROR);
		}

		private float NormalizeRange(float value)
		{
			if (value == 0)
				return 0;
			else
				return MathHelper.Clamp((value - minRange) / (maxRange - minRange), 0, 1);
		}

		private float DenormalizeRange(float value)
		{
			if (value == 0)
				return 0;
			else
				return MathHelper.Clamp(minRange + value * (maxRange - minRange), minRange, maxRange);
		}

	}
}
