using System;
using System.Collections.Generic;
using System.Text;
using System.Xml.Serialization;
using Rynchodon.AntennaRelay;
using Rynchodon.Threading;
using Rynchodon.Utility;
using Rynchodon.Utility.Network;
using Sandbox.Common.ObjectBuilders;
using Sandbox.Definitions;
using Sandbox.Game.Entities;
using Sandbox.Game.Gui;
using Sandbox.Game.Weapons;
using Sandbox.ModAPI;
using Sandbox.ModAPI.Interfaces;
using Sandbox.ModAPI.Interfaces.Terminal;
using VRage;
using VRage.Game;
using VRage.Game.Entity;
using VRage.Game.ModAPI;
using VRage.ModAPI;
using VRage.Utils;
using VRageMath;
using Ingame = Sandbox.ModAPI.Ingame;

namespace Rynchodon.Weapons
{
	/// <summary>
	/// Contains functions that are common to turrets and fixed weapons
	/// </summary>
	public abstract class WeaponTargeting : TargetingBase
	{

		[Serializable]
		public class Builder_WeaponTargeting
		{
			[XmlAttribute]
			public long WeaponId;
			public TargetType TargetTypeFlags;
			public TargetingFlags TargetOptFlags;
			public float Range;
			public string TargetBlockList;
			public string TargetEntityId;
		}

		public enum Control : byte { Off, On, Engager }

		private enum WeaponFlags : byte { None = 0, EntityId = 1, Golis = 2, Laser = 4 }

		#region Static

		private class StaticVariables
		{
			public Logger logger = new Logger("WeaponTargeting");
			/// <remarks>
			/// <para>Increasing the number of threads would require locks to be added in many areas.</para>
			/// <para>One thread has no trouble putting enough projectiles into play to slow the game to a crawl.</para>
			/// </remarks>
			public ThreadManager Thread = new ThreadManager(threadName: "WeaponTargeting");

			public ITerminalProperty<bool> TPro_Shoot;

			public int indexShoot;
			public MyTerminalControlOnOffSwitch<MyUserControllableGun> armsTargeting;
			public MyTerminalControlOnOffSwitch<MyUserControllableGun> motorTurret;
			public List<MyTerminalControl<MyUserControllableGun>> sharedControls = new List<MyTerminalControl<MyUserControllableGun>>();
			public List<MyTerminalControl<MyUserControllableGun>> fixedControls = new List<MyTerminalControl<MyUserControllableGun>>();
		}

		private static StaticVariables Static = new StaticVariables();

		static WeaponTargeting()
		{
			// When we get controls later, SE will create a list if there is none, which will prevent MyLargeTurretBase from creating the controls.
			if (!MyTerminalControlFactory.AreControlsCreated<MyLargeTurretBase>())
			{
				Static.logger.debugLog("forcing creation of turret controls");

				MyObjectBuilder_CubeGrid gridBuilder = new MyObjectBuilder_CubeGrid();
				gridBuilder.CubeBlocks.Add(new MyObjectBuilder_InteriorTurret());
				MyEntity grid = MyEntities.CreateFromObjectBuilder(gridBuilder);
			}

			var controls = MyTerminalControlFactory.GetControls(typeof(MyUserControllableGun));

			//Static.logger.debugLog("controls: " + controls);
			//Static.logger.debugLog("control count: " + controls.Count);

			// find the current position of shoot On/Off
			int currentIndex = 0;
			foreach (ITerminalControl control in MyTerminalControlFactory.GetControls(typeof(MyUserControllableGun)))
			{
				if (control.Id == "Shoot")
				{
					Static.indexShoot = currentIndex;
					break;
				}
				currentIndex++;
			}

			//Static.logger.debugLog("shoot index: " + Static.indexShoot);

			Static.sharedControls.Add(new MyTerminalControlSeparator<MyUserControllableGun>());

			Static.armsTargeting = new MyTerminalControlOnOffSwitch<MyUserControllableGun>("ArmsTargeting", MyStringId.GetOrCompute("ARMS Targeting"), MyStringId.GetOrCompute("ARMS will control this turret"));
			AddGetSet(Static.armsTargeting, TargetingFlags.ArmsEnabled);

			Static.motorTurret = new MyTerminalControlOnOffSwitch<MyUserControllableGun>("RotorTurret", MyStringId.GetOrCompute("Rotor-Turret"), MyStringId.GetOrCompute("ARMS will treat the weapon as part of a rotor-turret"));
			AddGetSet(Static.motorTurret, TargetingFlags.Turret);

			MyTerminalControlCheckbox<MyUserControllableGun> functional = new MyTerminalControlCheckbox<MyUserControllableGun>("TargetFunctional", MyStringId.GetOrCompute("Target Functional"),
				MyStringId.GetOrCompute("ARMS will target blocks that are functional, not just blocks that are working"));
			AddGetSet(functional, TargetingFlags.Functional);
			Static.sharedControls.Add(functional);

			MyTerminalControlCheckbox<MyUserControllableGun> preserve = new MyTerminalControlCheckbox<MyUserControllableGun>("PreserveEnemy", MyStringId.GetOrCompute("Preserve Enemy"),
				MyStringId.GetOrCompute("ARMS will not shoot through hostile blocks to destroy targets"));
			AddGetSet(preserve, TargetingFlags.Preserve);
			Static.sharedControls.Add(preserve);

			MyTerminalControlCheckbox<MyUserControllableGun> destroy = new MyTerminalControlCheckbox<MyUserControllableGun>("DestroyBlocks", MyStringId.GetOrCompute("Destroy Blocks"),
				MyStringId.GetOrCompute("ARMS will destroy every terminal block"));
			AddGetSet(destroy, TargetType.Destroy);
			Static.sharedControls.Add(destroy);

			MyTerminalControlCheckbox<MyUserControllableGun> laser = new MyTerminalControlCheckbox<MyUserControllableGun>("ShowLaser", MyStringId.GetOrCompute("Show Laser"),
				MyStringId.GetOrCompute("Everything is better with lasers!"));
			AddGetSet(laser, WeaponFlags.Laser);
			Static.sharedControls.Add(laser);

			Static.sharedControls.Add(new MyTerminalControlSeparator<MyUserControllableGun>());

			MyTerminalControlTextbox<MyUserControllableGun> textBox = new MyTerminalControlTextbox<MyUserControllableGun>("TargetBlocks", MyStringId.GetOrCompute("Target Blocks"),
				MyStringId.GetOrCompute("Comma separated list of blocks to target"));
			IMyTerminalValueControl<StringBuilder> valueControl = textBox;
			valueControl.Getter = GetBlockList;
			valueControl.Setter = SetBlockList;
			Static.sharedControls.Add(textBox);

			MyTerminalControlCheckbox<MyUserControllableGun> targetById = new MyTerminalControlCheckbox<MyUserControllableGun>("TargetByEntityId", MyStringId.GetOrCompute("Target by Entity ID"),
				MyStringId.GetOrCompute("Use ID of an entity for targeting"));
			AddGetSet(targetById, WeaponFlags.EntityId);
			Static.sharedControls.Add(targetById);

			textBox = new MyTerminalControlTextbox<MyUserControllableGun>("EntityId", MyStringId.GetOrCompute("Target Entity ID"),
				MyStringId.GetOrCompute("ID of entity to target"));
			textBox.Visible = block => GetEnum(block, WeaponFlags.EntityId);
			valueControl = textBox;
			valueControl.Getter = GetTargetEntity;
			valueControl.Setter = SetTargetEntity;
			Static.sharedControls.Add(textBox);

			//MyTerminalControlCheckbox<MyUserControllableGun> targetGolis = new MyTerminalControlCheckbox<MyUserControllableGun>("TargetByGps", MyStringId.GetOrCompute("Target by GPS"),
			//MyStringId.GetOrCompute("Use GPS for targeting"));
			//AddGetSet(targetGolis, SpecialTarget.Golis);
			//Static.sharedControls.Add(targetGolis);

			// GPS list

			Static.fixedControls.Add(new MyTerminalControlSeparator<MyUserControllableGun>());

			MyTerminalControlSlider<MyUserControllableGun> rangeSlider = CloneTurretControl_Slider("Range");
			rangeSlider.DefaultValue = 0f;
			rangeSlider.Normalizer = NormalizeRange;
			rangeSlider.Denormalizer = DenormalizeRange;
			rangeSlider.Writer = (x, result) => result.Append(PrettySI.makePretty(GetRange(x))).Append('m');
			rangeSlider.Visible = RangeSliderVisible;
			IMyTerminalValueControl<float> asInter = (IMyTerminalValueControl<float>)rangeSlider;
			asInter.Getter = GetRange;
			asInter.Setter = SetRange;
			Static.fixedControls.Add(rangeSlider);

			CloneTurretControl_OnOff("TargetMeteors", TargetType.Meteor);
			CloneTurretControl_OnOff("TargetMoving", TargetType.Moving);
			CloneTurretControl_OnOff("TargetMissiles", TargetType.Missile);
			CloneTurretControl_OnOff("TargetSmallShips", TargetType.SmallGrid);
			CloneTurretControl_OnOff("TargetLargeShips", TargetType.LargeGrid);
			CloneTurretControl_OnOff("TargetCharacters", TargetType.Character);
			CloneTurretControl_OnOff("TargetStations", TargetType.Station);

			foreach (IMyTerminalControl control in MyTerminalControlFactory.GetControls(typeof(MyLargeTurretBase)))
			{
				MyTerminalControlOnOffSwitch<MyLargeTurretBase> onOff = control as MyTerminalControlOnOffSwitch<MyLargeTurretBase>;
				if (onOff != null && onOff.Id == "TargetNeutrals")
				{
					MyTerminalControlOnOffSwitch<MyUserControllableGun> newControl = new MyTerminalControlOnOffSwitch<MyUserControllableGun>(onOff.Id, onOff.Title, onOff.Tooltip);
					IMyTerminalValueControl<bool> valueControlB = newControl;
					valueControlB.Getter = block => !GetEnum(block, TargetingFlags.IgnoreOwnerless);
					valueControlB.Setter = (block, value) => SetEnum(block, TargetingFlags.IgnoreOwnerless, !value);
					Static.fixedControls.Add(newControl);
					break;
				}
			}

			MyAPIGateway.Entities.OnCloseAll += Entities_OnCloseAll;
			MyTerminalControls.Static.CustomControlGetter += CustomControlGetter;
		}

		private static void Entities_OnCloseAll()
		{
			MyAPIGateway.Entities.OnCloseAll -= Entities_OnCloseAll;
			Static = null;
			MyTerminalControls.Static.CustomControlGetter -= CustomControlGetter;
		}

		private static void AddGetSet(IMyTerminalValueControl<bool> valueControl, TargetType flag)
		{
			valueControl.Getter = block => GetEnum(block, flag);
			valueControl.Setter = (block, value) => SetEnum(block, flag, value);
		}

		private static void AddGetSet(IMyTerminalValueControl<bool> valueControl, TargetingFlags flag)
		{
			valueControl.Getter = block => GetEnum(block, flag);
			valueControl.Setter = (block, value) => SetEnum(block, flag, value);
		}

		private static void AddGetSet(IMyTerminalValueControl<bool> valueControl, WeaponFlags which)
		{
			valueControl.Getter = block => GetEnum(block, which);
			valueControl.Setter = (block, value) => SetEnum(block, which, value);
		}

		private static void CloneTurretControl_OnOff(string id, TargetType flag)
		{
			foreach (IMyTerminalControl control in MyTerminalControlFactory.GetControls(typeof(MyLargeTurretBase)))
			{
				MyTerminalControlOnOffSwitch<MyLargeTurretBase> onOff = control as MyTerminalControlOnOffSwitch<MyLargeTurretBase>;
				if (onOff != null && onOff.Id == id)
				{
					MyTerminalControlOnOffSwitch<MyUserControllableGun> newControl = new MyTerminalControlOnOffSwitch<MyUserControllableGun>(id, onOff.Title, onOff.Tooltip);
					AddGetSet(newControl, flag);
					Static.fixedControls.Add(newControl);
					return;
				}
			}
			throw new ArgumentException("id: " + id + " does not have a control");
		}

		private static MyTerminalControlSlider<MyUserControllableGun> CloneTurretControl_Slider(string id)
		{
			foreach (IMyTerminalControl control in MyTerminalControlFactory.GetControls(typeof(MyLargeTurretBase)))
			{
				MyTerminalControlSlider<MyLargeTurretBase> slider = control as MyTerminalControlSlider<MyLargeTurretBase>;
				if (slider != null && slider.Id == id)
					return new MyTerminalControlSlider<MyUserControllableGun>(id, slider.Title, slider.Tooltip);
			}
			throw new ArgumentException("id: " + id + " does not have a control");
		}

		private static void CustomControlGetter(IMyTerminalBlock block, List<IMyTerminalControl> controlList)
		{
			if (!(block is MyUserControllableGun))
				return;

			int index = Static.indexShoot + 1;
			int sharedIndex = 0;

			controlList.Insert(index++, Static.sharedControls[sharedIndex++]);
			if (block is MyLargeTurretBase)
				controlList.Insert(index++, Static.armsTargeting);
			else
				controlList.Insert(index++, Static.motorTurret);

			for (; sharedIndex < Static.sharedControls.Count; sharedIndex++)
				controlList.Insert(index++, Static.sharedControls[sharedIndex]);

			if (!(block is MyLargeTurretBase))
				foreach (var control in Static.fixedControls)
					controlList.Insert(index++, control);
		}

		/// <summary>
		/// FixedWeapons and Turrets are stored separately in Registrar, this makes it simpler to retreive one when only base class is needed.
		/// </summary>
		public static bool TryGetWeaponTargeting(long blockId, out WeaponTargeting result)
		{
			FixedWeapon fixedWpn;
			if (Registrar.TryGetValue(blockId, out fixedWpn))
			{
				result = fixedWpn;
				return true;
			}

			Turret turretWpn;
			if (Registrar.TryGetValue(blockId, out turretWpn))
			{
				result = turretWpn;
				return true;
			}

			if (Static != null)
				Static.logger.alwaysLog("block: " + blockId + " not found in registrar", Logger.severity.ERROR);

			result = null;
			return false;
		}

		public static bool TryGetWeaponTargeting(IMyEntity block, out WeaponTargeting result)
		{
			return TryGetWeaponTargeting(block.EntityId, out result);
		}

		/// <summary>
		/// Checks that the weapon does damage and can be used by ARMS targeting.
		/// </summary>
		/// <param name="weapon">The weapon block to check.</param>
		/// <returns>True iff the weapon can be used by ARMS targeting.</returns>
		public static bool ValidWeaponBlock(IMyCubeBlock weapon)
		{
			MyWeaponDefinition defn = MyDefinitionManager.Static.GetWeaponDefinition(((MyWeaponBlockDefinition)weapon.GetCubeBlockDefinition()).WeaponDefinitionId);
			MyAmmoMagazineDefinition magDef = MyDefinitionManager.Static.GetAmmoMagazineDefinition(defn.AmmoMagazinesId[0]);
			MyAmmoDefinition ammoDef = MyDefinitionManager.Static.GetAmmoDefinition(magDef.AmmoDefinitionId);
			return ammoDef.GetDamageForMechanicalObjects() > 0f;
		}

		private static float GetRange(IMyTerminalBlock block)
		{
			WeaponTargeting instance;
			if (TryGetWeaponTargeting(block, out instance))
				return instance.m_termControl_range_ev.Value;
			return 0f;
		}

		private static void SetRange(IMyTerminalBlock block, float value)
		{
			WeaponTargeting instance;
			if (TryGetWeaponTargeting(block, out instance))
				instance.m_termControl_range_ev.Value = value;
		}

		private static float NormalizeRange(IMyTerminalBlock block, float value)
		{
			WeaponTargeting instance;
			if (TryGetWeaponTargeting(block, out instance))
				return value / instance.MaxRange;
			return 0f;
		}

		private static float DenormalizeRange(IMyTerminalBlock block, float value)
		{
			WeaponTargeting instance;
			if (TryGetWeaponTargeting(block, out instance))
				return value * instance.MaxRange;
			return 0f;
		}

		private static bool GetEnum(IMyTerminalBlock block, TargetType flag)
		{
			WeaponTargeting instance;
			if (TryGetWeaponTargeting(block, out instance))
				return (instance.m_termControl_targetType_ev.Value & flag) != 0;
			return false;
		}

		private static void SetEnum(IMyTerminalBlock block, TargetType flag, bool value)
		{
			WeaponTargeting instance;
			if (!TryGetWeaponTargeting(block, out instance))
				return;
			if (value)
				instance.m_termControl_targetType_ev.Value |= flag;
			else
				instance.m_termControl_targetType_ev.Value &= ~flag;
		}

		private static bool GetEnum(IMyTerminalBlock block, TargetingFlags flag)
		{
			WeaponTargeting instance;
			if (TryGetWeaponTargeting(block, out instance))
				return (instance.m_termControl_targetFlag_ev.Value & flag) != 0;
			return false;
		}

		private static void SetEnum(IMyTerminalBlock block, TargetingFlags flag, bool value)
		{
			WeaponTargeting instance;
			if (!TryGetWeaponTargeting(block, out instance))
				return;
			if (value)
				instance.m_termControl_targetFlag_ev.Value |= flag;
			else
				instance.m_termControl_targetFlag_ev.Value &= ~flag;
		}

		private static bool GetEnum(IMyTerminalBlock block, WeaponFlags flag)
		{
			WeaponTargeting instance;
			if (!TryGetWeaponTargeting(block, out instance))
				return false;
			return (instance.m_termControl_weaponFlags_ev.Value & flag) != 0;
		}

		private static void SetEnum(IMyTerminalBlock block, WeaponFlags flag, bool value)
		{
			WeaponTargeting instance;
			if (!TryGetWeaponTargeting(block, out instance))
				return;
			if (value)
			{
				instance.m_termControl_weaponFlags_ev.Value |= flag;
				if (flag == WeaponFlags.EntityId)
					instance.m_termControl_weaponFlags_ev.Value &= ~WeaponFlags.Golis;
				else if (flag == WeaponFlags.Golis)
					instance.m_termControl_weaponFlags_ev.Value &= ~WeaponFlags.EntityId;
			}
			else
				instance.m_termControl_weaponFlags_ev.Value &= ~flag;
		}

		private static StringBuilder GetBlockList(IMyTerminalBlock block)
		{
			WeaponTargeting instance;
			if (!TryGetWeaponTargeting(block, out instance))
				return new StringBuilder();

			return instance.m_termControl_blockList_ev.Value;
		}

		private static void SetBlockList(IMyTerminalBlock block, StringBuilder value)
		{
			WeaponTargeting instance;
			if (!TryGetWeaponTargeting(block, out instance))
				return;

			instance.m_termControl_blockList_ev.Value = value;
		}

		private static StringBuilder GetTargetEntity(IMyTerminalBlock block)
		{
			WeaponTargeting instance;
			if (!TryGetWeaponTargeting(block, out instance))
				return new StringBuilder();

			return instance.m_termControl_targetEntity_ev.Value;
		}

		private static void SetTargetEntity(IMyTerminalBlock block, StringBuilder value)
		{
			WeaponTargeting instance;
			if (!TryGetWeaponTargeting(block, out instance))
				return;

			instance.m_termControl_targetEntity_ev.Value = value;
		}

		private static bool RangeSliderVisible(IMyTerminalBlock block)
		{
			WeaponTargeting instance;
			if (!TryGetWeaponTargeting(block, out instance))
				return false;

			return instance.IsNormalTurret || !instance.GuidedLauncher;
		}

		private static void UpdateVisual()
		{
			Static.armsTargeting.UpdateVisual();
			Static.motorTurret.UpdateVisual();
			foreach (var control in Static.sharedControls)
				control.UpdateVisual();
			foreach (var control in Static.fixedControls)
				control.UpdateVisual();
		}

		#endregion Static

		public readonly Ingame.IMyLargeTurretBase myTurret;

		/// <remarks>Simple turrets can potentially shoot their own grids so they must be treated differently</remarks>
		public readonly bool IsNormalTurret;
		/// <summary>Locked while an update on targeting thread is queued but not while it is running.</summary>
		private readonly FastResourceLock lock_Queued = new FastResourceLock();

		private Logger myLogger;
		public Ammo LoadedAmmo { get; private set; }
		private long UpdateNumber = 0;

		private InterpreterWeapon Interpreter;

		private bool FireWeapon;
		private bool IsFiringWeapon;
		private Control value_currentControl;

		/// <summary>First item is target, second is the weapon, followed by custom items.</summary>
		private IMyEntity[] m_ignoreList = new IMyEntity[2];

		private LockedQueue<Action> GameThreadActions = new LockedQueue<Action>(1);
		private readonly IRelayPart m_relayPart;

		public readonly WeaponDefinitionExpanded WeaponDefinition;

		private string[] m_termControl_blockList;
		private Vector3D? m_termControl_targetGolis;
		private long? m_termControl_targetEntityId;

		private EntityValue<TargetType> m_termControl_targetType_ev;
		private EntityValue<TargetingFlags> m_termControl_targetFlag_ev;
		private EntityValue<WeaponFlags> m_termControl_weaponFlags_ev;
		private EntityValue<float> m_termControl_range_ev;
		private EntityStringBuilder m_termControl_blockList_ev, m_termControl_targetEntity_ev;

		private bool value_suppressTargeting;

		public bool SuppressTargeting
		{
			get { return value_suppressTargeting; }
			set
			{
				if (value)
					myTarget = CurrentTarget = NoTarget.Instance;
				value_suppressTargeting = value;
			}
		}

		public Control CurrentControl
		{
			get { return value_currentControl; }
			set
			{
				if (value_currentControl == value || Static == null)
					return;

				//myLogger.debugLog("Control changed from " + value_currentControl + " to " + value, "get_CurrentControl()");

				if (MyAPIGateway.Multiplayer.IsServer)
				{
					if (IsNormalTurret)
					{
						if (value == Control.Off)
							GameThreadActions.Enqueue(() => myTurret.ResetTargetingToDefault());
						else
							GameThreadActions.Enqueue(() => myTurret.SetTarget(ProjectilePosition() + (CubeBlock.WorldMatrix.Backward + CubeBlock.WorldMatrix.Up) * 10));
					}
					if (value == Control.Off)
						GameThreadActions.Enqueue(() => ((IMyFunctionalBlock)CubeBlock).GetActionWithName("Shoot_Off").Apply(CubeBlock));
				}

				if (value == Control.Engager)
					UpdateAmmo();

				value_currentControl = value;
				FireWeapon = false;
			}
		}

		/// <summary>Checks that it is possible to control the weapon: working, not in use, etc.</summary>
		public bool CanControl
		{
			get { return CubeBlock.IsWorking && (!IsNormalTurret || !myTurret.IsUnderControl) && CubeBlock.OwnerId != 0; }
		}

		public bool HasAmmo
		{
			get { return LoadedAmmo != null; }
		}

		public float MaxRange
		{
			get { return LoadedAmmo == null ? 800f : LoadedAmmo.AmmoDefinition.MaxTrajectory; }
		}

		public WeaponTargeting(IMyCubeBlock weapon)
			: base(weapon)
		{
			if (weapon == null)
				throw new ArgumentNullException("weapon");
			if (!(weapon is IMyTerminalBlock) || !(weapon is IMyFunctionalBlock) || !((MyEntity)weapon).HasInventory || !(weapon is Ingame.IMyUserControllableGun))
				throw new ArgumentException("weapon(" + weapon.DefinitionDisplayNameText + ") is not of correct type");

			this.myTurret = weapon as Ingame.IMyLargeTurretBase;
			this.myLogger = new Logger("WeaponTargeting", weapon);

			this.Interpreter = new InterpreterWeapon(weapon);
			this.IsNormalTurret = myTurret != null;
			this.CubeBlock.OnClose += weapon_OnClose;
			this.FuncBlock.AppendingCustomInfo += FuncBlock_AppendingCustomInfo;

			byte index = 0;
			// do not assign default values, unless interpreter is removed
			this.m_termControl_targetType_ev = new EntityValue<TargetType>(weapon, index++, UpdateVisual);
			this.m_termControl_targetFlag_ev = new EntityValue<TargetingFlags>(weapon, index++, UpdateVisual);
			this.m_termControl_range_ev = new EntityValue<float>(weapon, index++, UpdateVisual);
			this.m_termControl_blockList_ev = new EntityStringBuilder(weapon, index++, () => {
				UpdateVisual();
				m_termControl_blockList = m_termControl_blockList_ev.Value.ToString().LowerRemoveWhitespace().Split(',');
			});
			this.m_termControl_targetEntity_ev = new EntityStringBuilder(weapon, index++, () => {
				UpdateVisual();
				long entityId;
				if (long.TryParse(m_termControl_targetEntity_ev.Value.ToString().RemoveWhitespace(), out entityId))
					m_termControl_targetEntityId = entityId;
				else
					m_termControl_targetEntityId = null;
				myLogger.debugLog("target entity id: " + m_termControl_targetEntityId + ", string value: " + m_termControl_targetEntity_ev.Value.ToString());
			});
			this.m_termControl_weaponFlags_ev = new EntityValue<WeaponFlags>(weapon, index++, UpdateVisual, WeaponFlags.EntityId);

			if (Static.TPro_Shoot == null)
				Static.TPro_Shoot = (weapon as IMyTerminalBlock).GetProperty("Shoot").AsBool();

			if (WeaponDescription.GetFor(weapon).LastSeenTargeting)
				m_relayPart = RelayClient.GetOrCreateRelayPart(weapon);

			WeaponDefinition = MyDefinitionManager.Static.GetWeaponDefinition(((MyWeaponBlockDefinition)weapon.GetCubeBlockDefinition()).WeaponDefinitionId);

			Ignore(new IMyEntity[] { });

			//myLogger.debugLog("initialized", "WeaponTargeting()", Logger.severity.INFO);
		}

		private void weapon_OnClose(IMyEntity obj)
		{
			//myLogger.debugLog("entered weapon_OnClose()", "weapon_OnClose()");

			CubeBlock.OnClose -= weapon_OnClose;
			if (Options != null)
				Options.Flags = TargetingFlags.None;

			//myLogger.debugLog("leaving weapon_OnClose()", "weapon_OnClose()");
		}

		public Builder_WeaponTargeting GetBuilder()
		{
			return new Builder_WeaponTargeting()
			{
				WeaponId = CubeBlock.EntityId,
				TargetTypeFlags = m_termControl_targetType_ev.Value,
				TargetOptFlags = m_termControl_targetFlag_ev.Value,
				Range = m_termControl_range_ev.Value,
				TargetBlockList = m_termControl_blockList_ev.Value.ToString(),
				TargetEntityId = m_termControl_targetEntity_ev.Value.ToString()
			};
		}

		public void ResumeFromSave(Builder_WeaponTargeting builder)
		{
			GameThreadActions.Enqueue(() => {
				m_termControl_targetType_ev.Value = builder.TargetTypeFlags;
				m_termControl_targetFlag_ev.Value = builder.TargetOptFlags;
				m_termControl_range_ev.Value = builder.Range;
				m_termControl_blockList_ev.Value = new StringBuilder(builder.TargetBlockList);
				m_termControl_targetEntity_ev.Value = new StringBuilder(builder.TargetEntityId);
			});
		}

		/// <summary>
		/// UpdateManger invokes this every update.
		/// </summary>
		public void Update_Targeting()
		{
			if (!MyAPIGateway.Multiplayer.IsServer && !MyAPIGateway.Session.Player.IdentityId.canControlBlock(CubeBlock))
				return;

			try
			{
				GameThreadActions.DequeueAll(action => action.Invoke());
				if (CurrentControl != Control.Off && FireWeapon != IsFiringWeapon && MyAPIGateway.Multiplayer.IsServer)
				{
					IsFiringWeapon = FireWeapon;
					if (FireWeapon)
					{
						//myLogger.debugLog("Opening fire", "Update_Targeting()");
						(CubeBlock as IMyTerminalBlock).GetActionWithName("Shoot_On").Apply(CubeBlock);
					}
					else
					{
						//myLogger.debugLog("Holding fire", "Update_Targeting()");
						IMyFunctionalBlock func = CubeBlock as IMyFunctionalBlock;
						func.GetActionWithName("Shoot_Off").Apply(CubeBlock);

						// Shoot_Off is not working for gatling/interior turrets, this seems to do the trick
						if (myTurret != null)
							myTurret.SetTarget(ProjectilePosition() + (CubeBlock.WorldMatrix.Backward + CubeBlock.WorldMatrix.Up) * 10);
					}
				}

				if (CurrentControl != Control.Off && (m_termControl_weaponFlags_ev.Value & WeaponFlags.Laser) != 0 &&
					MyAPIGateway.Session.Player != null && MyAPIGateway.Session.Player.IdentityId.canControlBlock(CubeBlock) && Vector3D.DistanceSquared(MyAPIGateway.Session.Player.GetPosition(), ProjectilePosition()) < 1e8f)
				{
					Vector3D start = ProjectilePosition();
					float distance;
			
					Target target = CurrentTarget;
					if (target.Entity != null)
					{
						if (target.FiringDirection.HasValue && !FireWeapon)
						{
							Vector4 yellow = Color.Yellow.ToVector4();
							MySimpleObjectDraw.DrawLine(start + target.FiringDirection.Value, start + target.FiringDirection.Value * 11f, "WeaponLaser", ref yellow, 0.05f);
						}
						distance = (float)Vector3D.Distance(start, target.GetPosition());
					}
					else
						distance = MaxRange;

					Vector4 colour = FireWeapon ? Color.DarkRed.ToVector4() : Color.DarkGreen.ToVector4();
					Vector3 facing = Facing();
					Vector3D end = start + facing * distance;
					Vector3D contact = Vector3D.Zero;
					if (MyHudCrosshair.GetTarget(start + facing * 10f, end, ref contact))
						end = contact;
					MySimpleObjectDraw.DrawLine(start, end, "WeaponLaser", ref colour, 0.05f);
				}

				Update1_GameThread();

				if (lock_Queued.TryAcquireExclusive())
					Static.Thread.EnqueueAction(Update_Thread);
			}
			catch (Exception ex)
			{
				myLogger.alwaysLog("Exception: " + ex, Logger.severity.ERROR);
				if (MyAPIGateway.Multiplayer.IsServer)
					FuncBlock.RequestEnable(false);

				((IMyFunctionalBlock)CubeBlock).AppendCustomInfo("ARMS targeting crashed, see log for details");
			}
		}

		protected void Ignore(ICollection<IMyEntity> entities)
		{
			m_ignoreList = new IMyEntity[entities.Count + 2];
			m_ignoreList[1] = IsNormalTurret ? (IMyEntity)CubeBlock : (IMyEntity)CubeBlock.CubeGrid;
			int index = 2;
			foreach (IMyEntity entity in entities)
				m_ignoreList[index++] = entity;
		}

		/// <summary>Invoked on game thread, every updated, if targeting is permitted.</summary>
		protected abstract void Update1_GameThread();

		/// <summary>Invoked on targeting thread, every 100 updates, if targeting is permitted.</summary>
		protected virtual void Update100_Options_TargetingThread(TargetingOptions current) { }

		/// <summary>World direction that the weapon is facing.</summary>
		protected abstract Vector3 Facing();

		protected override float ProjectileSpeed(ref Vector3D targetPos)
		{
			if (LoadedAmmo == null)
				return 1f;

			if (LoadedAmmo.DistanceToMaxSpeed < 1)
			{
				//myLogger.debugLog("DesiredSpeed = " + LoadedAmmo.AmmoDefinition.DesiredSpeed, "LoadedAmmoSpeed()");
				return LoadedAmmo.AmmoDefinition.DesiredSpeed;
			}

			if (LoadedAmmo.MissileDefinition == null)
			{
				myLogger.alwaysLog("Missile Ammo expected: " + LoadedAmmo.AmmoDefinition.DisplayNameText, Logger.severity.ERROR);
				return LoadedAmmo.AmmoDefinition.DesiredSpeed;
			}

			float distance = Vector3.Distance(ProjectilePosition(), targetPos);
			return LoadedAmmo.MissileSpeed(distance);
		}

		/// <summary>
		/// Invoked on targeting thread
		/// </summary>
		private void Update_Thread()
		{
			try
			{
				lock_Queued.ReleaseExclusive();
				if (UpdateNumber % 10 == 0)
				{
					if (UpdateNumber % 100 == 0)
						Profiler.Profile(Update100);
					Profiler.Profile(Update10);
				}
				Profiler.Profile(Update1);

				UpdateNumber++;
			}
			catch (Exception ex)
			{ myLogger.alwaysLog("Exception: " + ex, Logger.severity.WARNING); }
		}

		/// <summary>
		/// Determines firing direction & intersection point.
		/// </summary>
		private void Update1()
		{
			if (CurrentControl == Control.Off || LoadedAmmo == null || CurrentTarget == null || CurrentTarget.Entity == null || CurrentTarget.Entity.Closed)
			{
				FireWeapon = false;
				return;
			}

			SetFiringDirection();
			CheckFire();
		}

		/// <summary>
		/// Checks for ammo and chooses a target (if necessary).
		/// </summary>
		private void Update10()
		{
			if (GuidedLauncher)
				UpdateAmmo();

			if (CurrentControl == Control.Off || SuppressTargeting)
				return;

			if (!GuidedLauncher)
				UpdateAmmo();
			if (LoadedAmmo == null)
			{
				//myLogger.debugLog("No ammo loaded", "Update10()");
				CurrentTarget = NoTarget.Instance;
				return;
			}

			UpdateTarget();

			if ((CurrentTarget.TType == TargetType.None || CurrentTarget is LastSeenTarget) && m_relayPart != null)
				GetLastSeenTarget(m_relayPart.GetStorage(), LoadedAmmo.MissileDefinition.MaxTrajectory);
		}

		private void Update100()
		{
			CheckCustomInfo();

			if (!CanControl)
			{
				//myLogger.debugLog("cannot control", "Update100()");
				CurrentControl = Control.Off;
				Options.Flags = TargetingFlags.None;
				return;
			}

			IsFiringWeapon = Static.TPro_Shoot.GetValue(CubeBlock);
			//myLogger.debugLog("fire: " + FireWeapon + ", isFiring: " + IsFiringWeapon, "Update100()");
			ClearBlacklist();

			Interpreter.UpdateInstruction();
			Options.Assimilate(Interpreter.Options, m_termControl_targetType_ev.Value, m_termControl_targetFlag_ev.Value, m_termControl_range_ev.Value, m_termControl_targetGolis, m_termControl_targetEntityId, m_termControl_blockList);
			Update100_Options_TargetingThread(Options);

			if (CurrentControl == Control.Engager)
				return;

			if (IsNormalTurret ?
				Interpreter.HasInstructions || Options.FlagSet(TargetingFlags.ArmsEnabled) :
				Options.FlagSet(TargetingFlags.Turret))
			{
				CurrentControl = Control.On;
				return;
			}

			//myLogger.debugLog("Not running targeting");
			CurrentControl = Control.Off;
		}

		private void UpdateAmmo()
		{
			LoadedAmmo = MyAPIGateway.Session.CreativeMode ? WeaponDefinition.FirstAmmo : Ammo.GetLoadedAmmo(CubeBlock);
		}

		private Vector3 previousFiringDirection;
		private byte facingWrongWayFor;

		private void CheckFire()
		{
			Target target = CurrentTarget;

			if (!target.FiringDirection.HasValue || !target.ContactPoint.HasValue)
			{
				//myLogger.debugLog("no firing direction");
				FireWeapon = false;
				return;
			}

			Vector3 CurrentDirection = Facing();
			float directionChange;
			Vector3.DistanceSquared(ref CurrentDirection, ref previousFiringDirection, out directionChange);
			previousFiringDirection = CurrentDirection;

			if (directionChange > 0.01f)
			{
				// weapon is still being aimed
				//myLogger.debugLog("still turning, change: " + directionChange);
				if (++facingWrongWayFor > 9)
					FireWeapon = false;
				return;
			}

			Vector3 firingDirection = target.FiringDirection.Value;
			float accuracy;
			Vector3.Dot(ref CurrentDirection, ref firingDirection, out accuracy);

			if (accuracy < WeaponDefinition.RequiredAccuracy)
			{
				// not facing target
				//myLogger.debugLog("not facing, accuracy: " + accuracy + ", required: " + WeaponDefinition.RequiredAccuracy);
				if (++facingWrongWayFor > 9)
					FireWeapon = false;
				return;
			}

			if (Obstructed(target.ContactPoint.Value, target.Entity))
			{
				//myLogger.debugLog("target is obstructed");
				//myLogger.debugLog("blacklisting: " + target.Entity.getBestName());
				BlacklistTarget();
				if (++facingWrongWayFor > 9)
					FireWeapon = false;
				return;
			}

			//myLogger.debugLog("firing");
			facingWrongWayFor = 0;
			FireWeapon = true;
		}

		/// <summary>
		/// <para>Test line segment between weapon and target for obstructing entities.</para>
		/// <para>Tests for obstructing voxel map, non-hostile character, or non-hostile grid.</para>
		/// </summary>
		/// <param name="contactPosition">position of entity to shoot</param>
		/// Not going to add a ready-to-fire bypass for ignoring source grid it would only protect against suicidal designs
		protected override bool Obstructed(Vector3D contactPosition, IMyEntity target)
		{
			myLogger.debugLog(CubeBlock == null, "CubeBlock == null", Logger.severity.FATAL);
			m_ignoreList[0] = target;
			return RayCast.Obstructed(new LineD(ProjectilePosition(), contactPosition), PotentialObstruction, m_ignoreList, true);
		}

		private bool condition_changed;
		private bool prev_notWorking, prev_playerControl, prev_noOwn, prev_ammo, prev_range, prev_noGrids, prev_noStorage;
		private int prev_errors;
		private long prev_target;
		private Control prev_control;

		/// <summary>
		/// Look for changes that would affect custom info.
		/// </summary>
		private void CheckCustomInfo()
		{
			condition_changed = false;

			ConditionChange(!CubeBlock.IsWorking, ref prev_notWorking);
			ConditionChange(IsNormalTurret && myTurret.IsUnderControl, ref prev_playerControl);
			ConditionChange(CubeBlock.OwnerId == 0, ref prev_noOwn);
			ConditionChange(Options.TargetingRange < 1f, ref prev_range);
			ConditionChange(!Options.CanTargetType(TargetType.AllGrid | TargetType.Destroy), ref prev_noGrids);
			ConditionChange(m_relayPart != null && m_relayPart.GetStorage() == null, ref prev_noStorage);

			ConditionChange(Interpreter.Errors.Count, ref prev_errors);

			ConditionChange(CurrentControl, ref prev_control);
			ConditionChange(LoadedAmmo == null, ref prev_ammo);

			long target = CurrentTarget != null && CurrentTarget.Entity != null ? CurrentTarget.Entity.EntityId : 0L;
			ConditionChange(target, ref prev_target);

			if (condition_changed)
				MyAPIGateway.Utilities.InvokeOnGameThread(FuncBlock.RefreshCustomInfo);
		}

		private void ConditionChange<T>(T condition, ref T previous) where T : struct
		{
			if (!condition.Equals(previous))
			{
				condition_changed = true;
				previous = condition;
			}
		}

		private void FuncBlock_AppendingCustomInfo(IMyTerminalBlock block, StringBuilder customInfo)
		{
			if (block == null || block.Closed)
				return;

			if (Interpreter.Errors.Count != 0)
			{
				customInfo.AppendLine("Syntax Errors: ");
				customInfo.AppendLine(string.Join("\n", Interpreter.Errors));
				customInfo.AppendLine();
			}

			if (GuidedLauncher)
			{
				Target t = CurrentTarget;
				if (t.Entity != null)
				{
					Ammo la = LoadedAmmo;
					if (la != null && !string.IsNullOrEmpty(la.AmmoDefinition.DisplayNameString))
						customInfo.Append(la.AmmoDefinition.DisplayNameString);
					else
						customInfo.Append("Guided Missile");
					customInfo.Append(" fired at ");

					LastSeenTarget lst = t as LastSeenTarget;
					if (lst != null)
					{
						if (lst.Block != null)
						{
							customInfo.Append(lst.Block.DefinitionDisplayNameText);
							customInfo.Append(" on ");
						}
						customInfo.AppendLine(lst.LastSeen.HostileName());
					}
					else
						customInfo.AppendLine(t.Entity.GetNameForDisplay(CubeBlock.OwnerId));
				}
				// else, guided missile has no initial target though it may acquire one
			}

			if (prev_notWorking)
			{
				customInfo.AppendLine("Off");
				return;
			}
			if (prev_playerControl)
			{
				customInfo.AppendLine("Being controlled by player");
				return;
			}
			if (prev_noOwn)
				customInfo.AppendLine("No owner");
			if (prev_noStorage)
				customInfo.AppendLine("No network connection");

			switch (CurrentControl)
			{
				case Control.Off:
					if (IsNormalTurret)
						customInfo.AppendLine("Vanilla targeting enabled");
					return;
				case Control.On:
					if (IsNormalTurret)
						customInfo.AppendLine("ARMS controlling");
					else
						customInfo.AppendLine("ARMS rotor-turret");
					break;
				case Control.Engager:
					customInfo.AppendLine("Engager controlling");
					break;
			}

			if (LoadedAmmo == null)
				customInfo.AppendLine("No ammo");
			if (prev_range)
				customInfo.AppendLine("Range is zero");
			if (prev_noGrids)
				customInfo.AppendLine("Not targeting ships");
			Target target = CurrentTarget;
			if (target.Entity == null)
				customInfo.AppendLine("No target");
			else
			{
				customInfo.Append("Has target: ");
				customInfo.AppendLine(target.Entity.GetNameForDisplay(CubeBlock.OwnerId));
			}
		}

	}
}
