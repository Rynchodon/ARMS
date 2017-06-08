#if DEBUG
//#define TRACE
#endif

using System;
using System.Collections.Generic;
using System.Text;
using System.Xml.Serialization;
using Rynchodon.AntennaRelay;
using Rynchodon.Threading;
using Rynchodon.Utility;
using Rynchodon.Utility.Collections;
using Rynchodon.Utility.Network;
using Rynchodon.Utility.Network.Sync;
using Rynchodon.Weapons.Guided;
using Sandbox.Definitions;
using Sandbox.Game.Gui;
using Sandbox.Game.Weapons;
using Sandbox.Graphics.GUI;
using Sandbox.ModAPI;
using Sandbox.ModAPI.Interfaces;
using Sandbox.ModAPI.Interfaces.Terminal;
using SpaceEngineers.Game.Weapons.Guns;
using VRage.Game;
using VRage.Game.Entity;
using VRage.Game.ModAPI;
using VRage.ModAPI;
using VRage.Utils;
using VRageMath;

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

		private enum WeaponFlags : byte { None = 0, EntityId = 1, Golis = 2, Laser = 4, ShootWithoutLock = 8 }

		private const byte valueId_entityId = 4;

		#region Static

		private class StaticVariables
		{
			private enum Visibility : byte { All, Fixed, Turret, Guided }

			/// <remarks>
			/// <para>Increasing the number of threads would require locks to be added in many areas.</para>
			/// <para>One thread has no trouble putting enough projectiles into play to slow the game to a crawl.</para>
			/// </remarks>
			public ThreadManager Thread = new ThreadManager(threadName: "WeaponTargeting");

			public FlagsValueSync<TargetingFlags, WeaponTargeting> termControl_targetFlag;
			public FlagsValueSync<TargetType, WeaponTargeting> termControl_targetType;
			public FlagsValueSync<WeaponFlags, WeaponTargeting> termControl_weaponFlags;

			public TypedValueSync<long, WeaponTargeting> termControl_targetEntityId;
			public ValueSync<float, WeaponTargeting> termControl_range;
			public StringBuilderSync<WeaponTargeting> termControl_blockList;
			public ValueSync<Vector3D, WeaponTargeting> termControl_targetGolis;

			public StaticVariables()
			{
				Logger.DebugLog("entered", Logger.severity.TRACE);
				TerminalControlHelper.EnsureTerminalControlCreated<MyLargeGatlingTurret>();
				TerminalControlHelper.EnsureTerminalControlCreated<MyLargeInteriorTurret>();
				TerminalControlHelper.EnsureTerminalControlCreated<MyLargeMissileTurret>();
				TerminalControlHelper.EnsureTerminalControlCreated<MySmallGatlingGun>();
				TerminalControlHelper.EnsureTerminalControlCreated<MySmallMissileLauncher>();
				TerminalControlHelper.EnsureTerminalControlCreated<MySmallMissileLauncherReload>();

				termControl_targetFlag = new FlagsValueSync<TargetingFlags, WeaponTargeting>("TargetFlag", "value_termControl_targetFlag");
				termControl_targetType = new FlagsValueSync<TargetType, WeaponTargeting>("TargetType", "value_termControl_targetType");
				termControl_weaponFlags = new FlagsValueSync<WeaponFlags, WeaponTargeting>("WeaponFlags", "value_termControl_weaponFlags");

				{
					MyTerminalControlOnOffSwitch<MyUserControllableGun> targetMoving = new MyTerminalControlOnOffSwitch<MyUserControllableGun>("ArmsTargetMoving", MyStringId.GetOrCompute("Target moving"), MyStringId.GetOrCompute("ARMS will target fast approaching objects"));
					termControl_targetType.AddControl(targetMoving, TargetType.Moving);
					AddControl(targetMoving, Visibility.Turret);
				}

				AddControl(new MyTerminalControlSeparator<MyUserControllableGun>());

				{
					MyTerminalControlOnOffSwitch<MyUserControllableGun> armsTargeting = new MyTerminalControlOnOffSwitch<MyUserControllableGun>("ArmsTargeting", MyStringId.GetOrCompute("ARMS Targeting"), MyStringId.GetOrCompute("ARMS will control this turret"));
					termControl_targetFlag.AddControl(armsTargeting, TargetingFlags.ArmsEnabled);
					AddControl(armsTargeting, Visibility.Turret);
				}

				{
					MyTerminalControlOnOffSwitch<MyUserControllableGun> motorTurret = new MyTerminalControlOnOffSwitch<MyUserControllableGun>("RotorTurret", MyStringId.GetOrCompute("Rotor-Turret"), MyStringId.GetOrCompute("ARMS will treat the weapon as part of a rotor-turret"));
					termControl_targetFlag.AddControl(motorTurret, TargetingFlags.Turret);
					AddControl(motorTurret, Visibility.Fixed);
				}

				{
					MyTerminalControlCheckbox<MyUserControllableGun> functional = new MyTerminalControlCheckbox<MyUserControllableGun>("TargetFunctional", MyStringId.GetOrCompute("Target Functional"),
						MyStringId.GetOrCompute("ARMS will target blocks that are functional, not just blocks that are working"));
					termControl_targetFlag.AddControl(functional, TargetingFlags.Functional);
					AddControl(functional);
				}

				{
					MyTerminalControlCheckbox<MyUserControllableGun> preserve = new MyTerminalControlCheckbox<MyUserControllableGun>("PreserveEnemy", MyStringId.GetOrCompute("Preserve Enemy"),
						MyStringId.GetOrCompute("ARMS will not shoot through hostile blocks to destroy targets"));
					termControl_targetFlag.AddControl(preserve, TargetingFlags.Preserve);
					AddControl(preserve);
				}

				{
					MyTerminalControlCheckbox<MyUserControllableGun> destroy = new MyTerminalControlCheckbox<MyUserControllableGun>("DestroyBlocks", MyStringId.GetOrCompute("Destroy Blocks"),
						MyStringId.GetOrCompute("ARMS will destroy every terminal block"));
					termControl_targetType.AddControl(destroy, TargetType.Destroy);
					AddControl(destroy);
				}

				{
					MyTerminalControlCheckbox<MyUserControllableGun> laser = new MyTerminalControlCheckbox<MyUserControllableGun>("ShowLaser", MyStringId.GetOrCompute("Show Laser"),
						MyStringId.GetOrCompute("Everything is better with lasers!"));
					termControl_weaponFlags.AddControl(laser, WeaponFlags.Laser);
					AddControl(laser);
				}

				{
					MyTerminalControlCheckbox<MyUserControllableGun> fwol = new MyTerminalControlCheckbox<MyUserControllableGun>("ShootWithoutLock", MyStringId.GetOrCompute("Shoot without lock"), MyStringId.GetOrCompute("Shoot guided missiles even if there are no valid targets"));
					termControl_weaponFlags.AddControl(fwol, WeaponFlags.ShootWithoutLock);
					AddControl(fwol, Visibility.Guided);
				}

				AddControl(new MyTerminalControlSeparator<MyUserControllableGun>());

				{
					MyTerminalControlTextbox<MyUserControllableGun> textBox = new MyTerminalControlTextbox<MyUserControllableGun>("TargetBlocks", MyStringId.GetOrCompute("Target Blocks"),
						MyStringId.GetOrCompute("Comma separated list of blocks to target"));
					termControl_blockList = new StringBuilderSync<WeaponTargeting>(textBox, (block) => block.value_termControl_blockList, (block, value) => {
						block.value_termControl_blockList = value;
						block.m_termControl_blockList = block.termControl_blockList.ToString().LowerRemoveWhitespace().Split(',');
					});
					AddControl(textBox);
				}

				{
					MyTerminalControlCheckbox<MyUserControllableGun> targetById = new MyTerminalControlCheckbox<MyUserControllableGun>("TargetByEntityId", MyStringId.GetOrCompute("Target by Entity ID"),
						MyStringId.GetOrCompute("Use ID of an entity for targeting"));
					termControl_weaponFlags.AddControl(targetById, WeaponFlags.EntityId);
					AddControl(targetById);
				}

				{
					MyTerminalControlTextbox<MyUserControllableGun> textBox = new MyTerminalControlTextbox<MyUserControllableGun>("EntityId", MyStringId.GetOrCompute("Target Entity ID"),
						MyStringId.GetOrCompute("ID of entity to target"));
					termControl_targetEntityId = new TypedValueSync<long, WeaponTargeting>(textBox, "value_termControl_targetEntityId");
					AddControl(textBox);
				}

				{
					MyTerminalControlCheckbox<MyUserControllableGun> targetGolis = new MyTerminalControlCheckbox<MyUserControllableGun>("TargetByGps", MyStringId.GetOrCompute("Target by GPS"),
					MyStringId.GetOrCompute("Use GPS for targeting"));
					termControl_weaponFlags.AddControl(targetGolis, WeaponFlags.Golis);
					AddControl(targetGolis, Visibility.Guided);
				}

				{
					MyTerminalControlListbox<MyUserControllableGun> gpsList = new MyTerminalControlListbox<MyUserControllableGun>("GpsList", MyStringId.GetOrCompute("GPS List"), MyStringId.NullOrEmpty, false, 4);
					gpsList.ListContent = FillGpsList;
					gpsList.ItemSelected = OnGpsListItemSelected;
					termControl_targetGolis = new ValueSync<Vector3D, WeaponTargeting>(gpsList.Id, "value_termControl_targetGolis");
					AddControl(gpsList, Visibility.Guided);
				}

				AddControl(new MyTerminalControlSeparator<MyUserControllableGun>());

				{
					MyTerminalControlSlider<MyUserControllableGun> rangeSlider = CloneTurretControl_Slider("Range");
					rangeSlider.DefaultValue = 0f;
					rangeSlider.Normalizer = NormalizeRange;
					rangeSlider.Denormalizer = DenormalizeRange;
					rangeSlider.Writer = (x, result) => result.Append(PrettySI.makePretty(termControl_range.GetValue(x))).Append('m');
					termControl_range = new ValueSync<float, WeaponTargeting>(rangeSlider, "value_termControl_range");
					AddControl(rangeSlider, Visibility.Fixed);
				}

				CloneTurretControl_OnOff("TargetMeteors", TargetType.Meteor);
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
						termControl_targetFlag.AddInverseControl(newControl, TargetingFlags.IgnoreOwnerless);
						AddControl(newControl, Visibility.Fixed);
						break;
					}
				}

				{
					MyTerminalControlOnOffSwitch<MyUserControllableGun> targetMoving = new MyTerminalControlOnOffSwitch<MyUserControllableGun>("ArmsTargetMoving", MyStringId.GetOrCompute("Target moving"), MyStringId.GetOrCompute("ARMS will target fast approaching objects"));
					termControl_targetType.AddControl(targetMoving, TargetType.Moving);
					AddControl(targetMoving, Visibility.Fixed);
				}

				Logger.TraceLog("initialized");
				//Controls.TrimExcess();
				//MyAPIGateway.TerminalControls.CustomControlGetter += CustomControlGetter;
			}

			private void AddControl(MyTerminalControlCheckbox<MyUserControllableGun> control, Visibility visibleTo = Visibility.All)
			{
				if (control.Actions == null)
					control.EnableAction();
				AddControl((MyTerminalControl<MyUserControllableGun>)control, visibleTo);
			}

			private void AddControl(MyTerminalControlOnOffSwitch<MyUserControllableGun> control, Visibility visibleTo = Visibility.All)
			{
				if (control.Actions == null)
				{
					control.EnableOnOffActions();
					control.EnableToggleAction();
				}
				AddControl((MyTerminalControl<MyUserControllableGun>)control, visibleTo);
			}

			private void AddControl(MyTerminalControlSlider<MyUserControllableGun> control, Visibility visibleTo = Visibility.All)
			{
				if (control.Actions == null)
					control.EnableActionsWithReset();
				AddControl((MyTerminalControl<MyUserControllableGun>)control, visibleTo);
			}

			private void AddControl(MyTerminalControl<MyUserControllableGun> control, Visibility visibleTo = Visibility.All)
			{
				Func<MyUserControllableGun, bool> enabled;

				switch (visibleTo)
				{
					case Visibility.All:
						enabled = True;
						break;
					case Visibility.Fixed:
						enabled = IsFixed;
						break;
					case Visibility.Turret:
						enabled = IsTurret;
						break;
					case Visibility.Guided:
						enabled = GuidedMissileLauncher.IsGuidedMissileLauncher;
						break;
					default:
						throw new NotImplementedException("Not implemented: " + visibleTo);
				}

				control.Enabled = control.Visible = enabled;

				if (control.Actions != null)
					AddActions(control, enabled, visibleTo);
				if (!(visibleTo == Visibility.Turret))
				{
					MyTerminalControlFactory.AddControl<MyUserControllableGun, MySmallGatlingGun>(control);
					MyTerminalControlFactory.AddControl<MyUserControllableGun, MySmallMissileLauncher>(control);
					MyTerminalControlFactory.AddControl<MyUserControllableGun, MySmallMissileLauncherReload>(control);
				}
				if (!(visibleTo == Visibility.Fixed))
				{
					MyTerminalControlFactory.AddControl<MyUserControllableGun, MyLargeGatlingTurret>(control);
					MyTerminalControlFactory.AddControl<MyUserControllableGun, MyLargeInteriorTurret>(control);
					MyTerminalControlFactory.AddControl<MyUserControllableGun, MyLargeMissileTurret>(control);
				}
				//Controls.Add(control);
			}

			private void AddActions(MyTerminalControl<MyUserControllableGun> control, Func<MyUserControllableGun, bool> enabled, Visibility visibleTo = Visibility.All)
			{
				foreach (var action in control.Actions)
				{
					action.Enabled = enabled;
					if (!(visibleTo == Visibility.Turret))
					{
						MyTerminalControlFactory.AddAction<MyUserControllableGun, MySmallGatlingGun>(action);
						MyTerminalControlFactory.AddAction<MyUserControllableGun, MySmallMissileLauncher>(action);
						MyTerminalControlFactory.AddAction<MyUserControllableGun, MySmallMissileLauncherReload>(action);
					}
					if (!(visibleTo == Visibility.Fixed))
					{
						MyTerminalControlFactory.AddAction<MyUserControllableGun, MyLargeGatlingTurret>(action);
						MyTerminalControlFactory.AddAction<MyUserControllableGun, MyLargeInteriorTurret>(action);
						MyTerminalControlFactory.AddAction<MyUserControllableGun, MyLargeMissileTurret>(action);
					}
				}
			}

			private static bool True(MyUserControllableGun gun)
			{
				return true;
			}

			private static bool IsFixed(MyUserControllableGun gun)
			{
				return !(gun is MyLargeTurretBase);
			}

			private static bool IsTurret(MyUserControllableGun gun)
			{
				return gun is MyLargeTurretBase;
			}

			private void CloneTurretControl_OnOff(string id, TargetType flag)
			{
				foreach (IMyTerminalControl control in MyTerminalControlFactory.GetControls(typeof(MyLargeTurretBase)))
				{
					MyTerminalControlOnOffSwitch<MyLargeTurretBase> onOff = control as MyTerminalControlOnOffSwitch<MyLargeTurretBase>;
					if (onOff != null && onOff.Id == id)
					{
						Logger.TraceLog("Cloning: " + onOff.Id);
						MyTerminalControlOnOffSwitch<MyUserControllableGun> newControl = new MyTerminalControlOnOffSwitch<MyUserControllableGun>(id, onOff.Title, onOff.Tooltip);
						termControl_targetType.AddControl(newControl, flag);
						AddControl(newControl, Visibility.Fixed);
						return;
					}
				}
				{
					Logger.AlwaysLog("Failed to get turret control for " + id + ", using default text", Logger.severity.INFO);
					MyTerminalControlOnOffSwitch<MyUserControllableGun> newControl = new MyTerminalControlOnOffSwitch<MyUserControllableGun>(id, MyStringId.GetOrCompute(id), MyStringId.NullOrEmpty);
					termControl_targetType.AddControl(newControl, flag);
					AddControl(newControl, Visibility.Fixed);
				}
			}

			private static MyTerminalControlSlider<MyUserControllableGun> CloneTurretControl_Slider(string id)
			{
				foreach (IMyTerminalControl control in MyTerminalControlFactory.GetControls(typeof(MyLargeTurretBase)))
				{
					MyTerminalControlSlider<MyLargeTurretBase> slider = control as MyTerminalControlSlider<MyLargeTurretBase>;
					if (slider != null && slider.Id == id)
					{
						Logger.TraceLog("Cloning: " + control.Id);
						return new MyTerminalControlSlider<MyUserControllableGun>(id, slider.Title, slider.Tooltip);
					}
				}
				Logger.AlwaysLog("Failed to get turret control for " + id + ", using default text", Logger.severity.INFO);
				return new MyTerminalControlSlider<MyUserControllableGun>(id, MyStringId.GetOrCompute(id), MyStringId.NullOrEmpty);
			}
		}

		private static StaticVariables Static;

		[OnWorldLoad]
		private static void Init()
		{
			Static = new StaticVariables();
		}

		[OnWorldClose]
		private static void Unload()
		{
			Static = null;
		}

		//private static void CustomControlGetter(IMyTerminalBlock block, List<IMyTerminalControl> controlList)
		//{
		//	if (!(block is MyUserControllableGun))
		//		return;

		//	for (int index = 0; index < Static.Controls.Count; ++index)
		//		controlList.Insert(Static.ControlsIndex + index, (IMyTerminalControl)Static.Controls[index]);
		//}

		public static bool TryGetWeaponTargeting(long blockId, out WeaponTargeting result)
		{
			return Registrar.TryGetValue(blockId, out result);
		}

		public static bool TryGetWeaponTargeting(IMyEntity block, out WeaponTargeting result)
		{
			return Registrar.TryGetValue(block, out result);
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

		private static void FillGpsList(IMyTerminalBlock block, ICollection<MyGuiControlListbox.Item> allItems, ICollection<MyGuiControlListbox.Item> selected)
		{
			WeaponTargeting targeting;
			if (!TryGetWeaponTargeting(block, out targeting))
				return;

			List<IMyGps> gpsList = MyAPIGateway.Session.GPS.GetGpsList(MyAPIGateway.Session.Player.IdentityId);
			Vector3D target = targeting.termControl_targetGolis;
			bool select = target.IsValid();
			foreach (IMyGps gps in gpsList)
			{
				MyGuiControlListbox.Item item = new MyGuiControlListbox.Item(new StringBuilder(gps.Name), gps.Description, null, gps);
				allItems.Add(item);

				if (select && selected.Count == 0 && gps.Coords == target)
					selected.Add(item);
			}
		}

		private static void OnGpsListItemSelected(IMyTerminalBlock block, List<MyGuiControlListbox.Item> selected)
		{
			WeaponTargeting targeting;
			if (!TryGetWeaponTargeting(block, out targeting))
				return;

			Logger.DebugLog("selected.Count: " + selected.Count, Logger.severity.ERROR, condition: selected.Count > 1);

			if (selected.Count == 0)
				targeting.termControl_targetGolis = Vector3.Invalid;
			else
				targeting.termControl_targetGolis = ((IMyGps)selected[0].UserData).Coords;
		}

		#endregion Static

		public readonly IMyLargeTurretBase myTurret;

		/// <remarks>Simple turrets can potentially shoot their own grids so they must be treated differently</remarks>
		public bool IsNormalTurret { get { return myTurret != null; } }
		/// <summary>Locked while an update on targeting thread is queued but not while it is running.</summary>
		private readonly FastResourceLock lock_Queued = new FastResourceLock();

		public Ammo LoadedAmmo { get; private set; }
		private long UpdateNumber = 0;

		private InterpreterWeapon Interpreter;

		private bool FireWeapon;
		//private bool IsFiringWeapon { get { return CubeBlock.IsShooting; } }
		private Control value_currentControl;

		/// <summary>First item is target, second is the weapon, followed by custom items.</summary>
		private IMyEntity[] m_ignoreList = new IMyEntity[2];

		private LockedDeque<Action> GameThreadActions = new LockedDeque<Action>(1);
		private readonly IRelayPart m_relayPart;

		public readonly WeaponDefinitionExpanded WeaponDefinition;

		private string[] m_termControl_blockList;

		private long value_termControl_targetEntityId;
		private long termControl_targetEntityId
		{
			get { return value_termControl_targetEntityId; }
			set { Static.termControl_targetEntityId.SetValue(MyEntity.EntityId, value); }
		}

		private TargetType value_termControl_targetType;
		private TargetType termControl_targetType
		{
			get { return value_termControl_targetType; }
			set { Static.termControl_targetType.SetValue(MyEntity.EntityId, value); }
		}

		private TargetingFlags value_termControl_targetFlag;
		private TargetingFlags termControl_targetFlag
		{
			get { return value_termControl_targetFlag; }
			set { Static.termControl_targetFlag.SetValue(MyEntity.EntityId, value); }
		}

		private WeaponFlags value_termControl_weaponFlags;
		private WeaponFlags termControl_weaponFlags
		{
			get { return value_termControl_weaponFlags; }
			set { Static.termControl_weaponFlags.SetValue(MyEntity.EntityId, value); }
		}

		private float value_termControl_range;
		private float termControl_range
		{
			get { return value_termControl_range; }
			set { Static.termControl_range.SetValue(MyEntity.EntityId, value); }
		}

		private StringBuilder value_termControl_blockList = new StringBuilder();
		private StringBuilder termControl_blockList
		{
			get { return value_termControl_blockList; }
			set { Static.termControl_blockList.SetValue(MyEntity.EntityId, value); }
		}

		private Vector3D value_termControl_targetGolis;
		private Vector3D termControl_targetGolis
		{
			get { return value_termControl_targetGolis; }
			set { Static.termControl_targetGolis.SetValue(MyEntity.EntityId, value); }
		}

		private bool value_suppressTargeting;
		public bool SuppressTargeting
		{
			get { return value_suppressTargeting; }
			set
			{
				if (value)
					SetTarget(NoTarget.Instance);
				value_suppressTargeting = value;
			}
		}

		public Control CurrentControl
		{
			get { return value_currentControl; }
			set
			{
				if (value_currentControl == value || Globals.WorldClosed)
					return;

				Log.DebugLog("Control changed from " + value_currentControl + " to " + value);

				if (MyAPIGateway.Multiplayer.IsServer)
				{
					if (IsNormalTurret && value == Control.Off)
						GameThreadActions.AddTail(RestoreDefaultTargeting);
					else
						GameThreadActions.AddTail(ShootOff);
				}

				if (value == Control.Engager)
					UpdateAmmo();

				value_currentControl = value;
				FireWeapon = false;
			}
		}

		/*
		 * Bug in Space Engineers breaks Shoot_On, Shoot_Off, SetShooting, SetTarget, and TrackTarget for turrets.
		 */

		private MyEntityUpdateEnum _defaultNeedsUpdate;

		private void TurretNeedsUpdate(bool enable)
		{
			if (enable == (myTurret.NeedsUpdate != MyEntityUpdateEnum.NONE))
				return;
			if (enable)
				myTurret.NeedsUpdate = _defaultNeedsUpdate;
			else
			{
				_defaultNeedsUpdate |= myTurret.NeedsUpdate;
				myTurret.NeedsUpdate = MyEntityUpdateEnum.NONE;
			}
		}

		private void RestoreDefaultTargeting()
		{
			if (myTurret != null)
			{
				myTurret.ResetTargetingToDefault();
				TurretNeedsUpdate(true);
			}
		}

		private static ITerminalProperty<bool> m_shootProperty;
		private static Sandbox.ModAPI.Interfaces.ITerminalAction m_shootOnce;

		private bool GetShootProp()
		{
			if (m_shootProperty == null)
			{
				m_shootProperty = CubeBlock.GetProperty("Shoot").AsBool();
				m_shootOnce = CubeBlock.GetAction("ShootOnce");
			}
			return m_shootProperty.GetValue(CubeBlock);
		}

		private void ShootOn()
		{
			if (GetShootProp())
				return;

			Log.TraceLog("Opening fire");
			if (myTurret != null)
			{
				TurretNeedsUpdate(true);
				myTurret.SetTarget(ProjectilePosition() + CurrentTarget.FiringDirection.Value * 1000f);
			}
			m_shootProperty.SetValue(CubeBlock, true);
			m_shootOnce.Apply(CubeBlock);
		}

		private void ShootOff()
		{
			if (!GetShootProp())
				return;

			Log.TraceLog("Holding fire");
			if (myTurret != null)
				TurretNeedsUpdate(false);
			m_shootProperty.SetValue(CubeBlock, false);
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

		private Logable Log { get { return new Logable(myTurret); } }

		private long TermControl_TargetEntityId
		{ get { return (termControl_weaponFlags & WeaponFlags.EntityId) == 0 ? 0L : termControl_targetEntityId; } }

		private Vector3D TermControl_TargetGolis
		{ get { return (termControl_weaponFlags & WeaponFlags.Golis) == 0 ? (Vector3D)Vector3.Invalid : termControl_targetGolis; } }

		public bool FireWithoutLock
		{ get { return (termControl_weaponFlags & WeaponFlags.ShootWithoutLock) != 0; } }

		public WeaponTargeting(IMyCubeBlock weapon)
			: base(weapon)
		{
			if (weapon == null)
				throw new ArgumentNullException("weapon");
			if (!(weapon is IMyTerminalBlock) || !(weapon is IMyFunctionalBlock) || !((MyEntity)weapon).HasInventory || !(weapon is IMyUserControllableGun))
				throw new ArgumentException("weapon(" + weapon.DefinitionDisplayNameText + ") is not of correct type");

			this.myTurret = weapon as IMyLargeTurretBase;

			this.Interpreter = new InterpreterWeapon(weapon);
			this.CubeBlock.OnClose += weapon_OnClose;
			this.CubeBlock.AppendingCustomInfo += FuncBlock_AppendingCustomInfo;

			if (WeaponDescription.GetFor(weapon).LastSeenTargeting)
				m_relayPart = RelayClient.GetOrCreateRelayPart(weapon);

			WeaponDefinition = MyDefinitionManager.Static.GetWeaponDefinition(((MyWeaponBlockDefinition)weapon.GetCubeBlockDefinition()).WeaponDefinitionId);

			Ignore(new IMyEntity[] { });

			Registrar.Add(weapon, this);

			//Log.DebugLog("initialized", "WeaponTargeting()", Logger.severity.INFO);
		}

		private void weapon_OnClose(IMyEntity obj)
		{
			//Log.DebugLog("entered weapon_OnClose()", "weapon_OnClose()");

			CubeBlock.OnClose -= weapon_OnClose;
			if (Options != null)
				Options.Flags = TargetingFlags.None;

			//Log.DebugLog("leaving weapon_OnClose()", "weapon_OnClose()");
		}

		public void ResumeFromSave(Builder_WeaponTargeting builder)
		{
			GameThreadActions.AddTail(() => {
				termControl_targetType = builder.TargetTypeFlags;
				termControl_targetFlag = builder.TargetOptFlags;
				termControl_range = builder.Range;
				termControl_blockList = new StringBuilder(builder.TargetBlockList);
			});
		}

		/// <summary>
		/// UpdateManager invokes this every update.
		/// </summary>
		public void Update_Targeting()
		{
			if (!MyAPIGateway.Multiplayer.IsServer && !MyAPIGateway.Session.Player.IdentityId.canControlBlock(CubeBlock))
				return;

			try
			{
				GameThreadActions.PopHeadInvokeAll();
				if (CurrentControl != Control.Off && MyAPIGateway.Multiplayer.IsServer)
				{
					if (FireWeapon)
					{
						//Log.TraceLog("Opening fire");
						ShootOn();
					}
					else
					{
						//Log.TraceLog("Holding fire");
						ShootOff();
					}
				}

				if (CurrentControl != Control.Off && (termControl_weaponFlags & WeaponFlags.Laser) != 0 &&
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
							MySimpleObjectDraw.DrawLine(start + target.FiringDirection.Value, start + target.FiringDirection.Value * 11f, Globals.WeaponLaser, ref yellow, 0.05f);
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
					MySimpleObjectDraw.DrawLine(start, end, Globals.WeaponLaser, ref colour, 0.05f);
				}

				Update1_GameThread();

				if (lock_Queued.TryAcquireExclusive())
					Static.Thread.EnqueueAction(Update_Thread);
			}
			catch (Exception ex)
			{
				Log.AlwaysLog("Exception: " + ex, Logger.severity.ERROR);
				if (MyAPIGateway.Multiplayer.IsServer && CubeBlock != null)
					CubeBlock.Enabled = false;

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
		public abstract Vector3 Facing();

		protected override float ProjectileSpeed(ref Vector3D targetPos)
		{
			if (LoadedAmmo == null)
				return 1f;

			if (LoadedAmmo.DistanceToMaxSpeed < 1)
			{
				//Log.DebugLog("DesiredSpeed = " + LoadedAmmo.AmmoDefinition.DesiredSpeed, "LoadedAmmoSpeed()");
				return LoadedAmmo.AmmoDefinition.DesiredSpeed;
			}

			if (LoadedAmmo.MissileDefinition == null)
			{
				Log.AlwaysLog("Missile Ammo expected: " + LoadedAmmo.AmmoDefinition.DisplayNameText, Logger.severity.ERROR);
				return LoadedAmmo.AmmoDefinition.DesiredSpeed;
			}

			float distance = (float)Vector3D.Distance(ProjectilePosition(), targetPos);
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
					{
						if (UpdateNumber % 1000 == 0 || CurrentTarget.IsNull())
							ClearBlacklist();
						Profiler.Profile(Update100);
					}
					Profiler.Profile(Update10);
				}
				Profiler.Profile(Update1);
			}
			catch (Exception ex)
			{ Log.AlwaysLog("Exception: " + ex, Logger.severity.WARNING); }
			UpdateNumber++;
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
			{
				Log.TraceLog("off", condition: CurrentControl == Control.Off);
				Log.TraceLog("suppressed", condition: SuppressTargeting);
				return;
			}

			if (!GuidedLauncher)
				UpdateAmmo();
			if (LoadedAmmo == null)
			{
				Log.TraceLog("No ammo loaded");
				SetTarget(NoTarget.Instance);
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
				//Log.DebugLog("cannot control", "Update100()");
				CurrentControl = Control.Off;
				Options.Flags = TargetingFlags.None;
				return;
			}

			//Log.DebugLog("fire: " + FireWeapon + ", isFiring: " + IsFiringWeapon, "Update100()");

			Interpreter.UpdateInstruction();
			Options.Assimilate(Interpreter.Options, termControl_targetType, termControl_targetFlag, termControl_range, TermControl_TargetGolis, TermControl_TargetEntityId, m_termControl_blockList);
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

			//Log.DebugLog("Not running targeting");
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
				Log.TraceLog("no firing direction");
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
				Log.TraceLog("still turning, change: " + directionChange);
				if (++facingWrongWayFor > 9)
					FireWeapon = false;
				return;
			}

			Vector3 firingDirection = target.FiringDirection.Value;
			float accuracy;
			Vector3.Dot(ref CurrentDirection, ref firingDirection, out accuracy);

			if (accuracy < WeaponDefinition.RequiredAccuracyCos)
			{
				// not facing target
				Log.TraceLog("not facing, accuracy: " + accuracy + ", required: " + WeaponDefinition.RequiredAccuracyCos);
				if (++facingWrongWayFor > 9)
					FireWeapon = false;
				return;
			}

			Vector3D position = target.ContactPoint.Value;
			if (Obstructed(ref position, target.Entity))
			{
				Log.TraceLog("blacklisting: " + target.Entity.getBestName());
				BlacklistTarget();
				if (++facingWrongWayFor > 9)
					FireWeapon = false;
				return;
			}

			//Log.TraceLog("firing");
			facingWrongWayFor = 0;
			FireWeapon = true;
		}

		/// <summary>
		/// <para>Test line segment between weapon and target for obstructing entities.</para>
		/// <para>Tests for obstructing voxel map, non-hostile character, or non-hostile grid.</para>
		/// </summary>
		/// <param name="contactPosition">position of entity to shoot</param>
		/// Not going to add a ready-to-fire bypass for ignoring source grid it would only protect against suicidal designs
		protected override bool Obstructed(ref Vector3D contactPosition, IMyEntity target)
		{
			Log.DebugLog("CubeBlock == null", Logger.severity.FATAL, condition: CubeBlock == null);
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
				MyAPIGateway.Utilities.InvokeOnGameThread(CubeBlock.UpdateCustomInfo);
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

			//if (GuidedLauncher)
			//{
			//	Target t = CurrentTarget;
			//	if (t.Entity != null)
			//	{
			//		Ammo la = LoadedAmmo;
			//		if (la != null && !string.IsNullOrEmpty(la.AmmoDefinition.DisplayNameString))
			//			customInfo.Append(la.AmmoDefinition.DisplayNameString);
			//		else
			//			customInfo.Append("Guided Missile");
			//		customInfo.Append(" fired at ");

			//		LastSeenTarget lst = t as LastSeenTarget;
			//		if (lst != null)
			//		{
			//			if (lst.Block != null)
			//			{
			//				customInfo.Append(lst.Block.DefinitionDisplayNameText);
			//				customInfo.Append(" on ");
			//			}
			//			customInfo.AppendLine(lst.LastSeen.HostileName());
			//		}
			//		else
			//			customInfo.AppendLine(t.Entity.GetNameForDisplay(CubeBlock.OwnerId));
			//	}
			//	// else, guided missile has no initial target though it may acquire one
			//}

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
					customInfo.AppendLine("Fighter controlling");
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
