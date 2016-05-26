using System;
using System.Collections.Generic;
using System.Text;
using Rynchodon.AntennaRelay;
using Rynchodon.Threading;
using Rynchodon.Utility;
using Sandbox.Definitions;
using Sandbox.Game.Gui;
using Sandbox.Game.Weapons;
using Sandbox.ModAPI;
using Sandbox.ModAPI.Interfaces;
using Sandbox.ModAPI.Interfaces.Terminal;
using VRage;
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

		public enum Control : byte { Off, On, Engager }

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
			var controls = MyTerminalControlFactory.GetControls(typeof(MyUserControllableGun));

			Static.logger.debugLog("controls: " + controls);
			Static.logger.debugLog("control count: " + controls.Count);

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

			Static.logger.debugLog("shoot index: " + Static.indexShoot);

			Static.sharedControls.Add(new MyTerminalControlSeparator<MyUserControllableGun>());

			Static.armsTargeting = new MyTerminalControlOnOffSwitch<MyUserControllableGun>("ArmsTargeting", MyStringId.GetOrCompute("ARMS Targeting"), MyStringId.GetOrCompute("ARMS will control this turret"));
			AddGetSet(Static.armsTargeting, TargetingFlags.ArmsEnabled);

			Static.motorTurret = new MyTerminalControlOnOffSwitch<MyUserControllableGun>("RotorTurret", MyStringId.GetOrCompute("Rotor Turret"), MyStringId.GetOrCompute("ARMS will treat the weapon as part of a rotor-turret"));
			AddGetSet(Static.motorTurret, TargetingFlags.Turret);

			MyTerminalControlCheckbox<MyUserControllableGun> functional = new MyTerminalControlCheckbox<MyUserControllableGun>("TargetFunctional", MyStringId.GetOrCompute("Target Functional"),
				MyStringId.GetOrCompute("ARMS will target blocks that are functional, not just blocks that are working"));
			AddGetSet(functional, TargetingFlags.Functional);
			Static.sharedControls.Add(functional);

			MyTerminalControlCheckbox<MyUserControllableGun> preserve = new MyTerminalControlCheckbox<MyUserControllableGun>("PreserveTarget", MyStringId.GetOrCompute("Preserve Target"),
				MyStringId.GetOrCompute("ARMS will not shoot through hostile blocks to destroy targets"));
			AddGetSet(preserve, TargetingFlags.Preserve);
			Static.sharedControls.Add(preserve);

			MyTerminalControlCheckbox<MyUserControllableGun> destroy = new MyTerminalControlCheckbox<MyUserControllableGun>("DestroyBlocks", MyStringId.GetOrCompute("Destroy Blocks"),
				MyStringId.GetOrCompute("ARMS will destroy every terminal block"));
			AddGetSet(destroy, TargetType.Destroy);
			Static.sharedControls.Add(destroy);

			Static.sharedControls.Add(new MyTerminalControlSeparator<MyUserControllableGun>());

			MyTerminalControlTextbox<MyUserControllableGun> textBox = new MyTerminalControlTextbox<MyUserControllableGun>("TargetBlocks", MyStringId.GetOrCompute("Target Blocks"), 
				MyStringId.GetOrCompute("Comma separated list of blocks to target"));
			IMyTerminalValueControl<StringBuilder> valueControl = textBox;
			valueControl.Getter = GetBlockList;
			valueControl.Setter = SetBlockList;
			Static.sharedControls.Add(textBox);

			textBox = new MyTerminalControlTextbox<MyUserControllableGun>("EntityId", MyStringId.GetOrCompute("Target Entity ID"),
				MyStringId.GetOrCompute("ID of entity to target"));
			valueControl = textBox;
			valueControl.Getter = GetTargetEntity;
			valueControl.Setter = SetTargetEntity;
			Static.sharedControls.Add(textBox);

			Static.fixedControls.Add(new MyTerminalControlSeparator<MyUserControllableGun>());

			MyTerminalControlSlider<MyUserControllableGun> rangeSlider = CloneTurretControl_Slider("Range");
			rangeSlider.DefaultValue = 0f;
			rangeSlider.Normalizer = NormalizeRange;
			rangeSlider.Denormalizer = DenormalizeRange;
			rangeSlider.Writer = (x, result) => result.Append(PrettySI.makePretty(GetRange(x))).Append('m');
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
			valueControl.Getter = block => GetFlag(block, flag);
			valueControl.Setter = (block, value) => SetFlag(block, flag, value);
		}

		private static void AddGetSet(IMyTerminalValueControl<bool> valueControl, TargetingFlags flag)
		{
			valueControl.Getter = block => GetFlag(block, flag);
			valueControl.Setter = (block, value) => SetFlag(block, flag, value);
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

		private static bool TryGetWeaponTargeting(IMyEntity block, out WeaponTargeting result)
		{
			FixedWeapon fixedWpn;
			if (Registrar.TryGetValue(block, out fixedWpn))
			{
				result = fixedWpn;
				return true;
			}

			Turret turretWpn;
			if (Registrar.TryGetValue(block, out turretWpn))
			{
				result = turretWpn;
				return true;
			}

			if (Static == null)
			{
				result = null;
				return false;
			}

			if (!(block is IMyCubeBlock))
				throw new ArgumentException("enitity " + block.EntityId + " is not a block");
			throw new ArgumentException("block " + block.EntityId + " not found in registrar");
		}

		private static float GetRange(IMyTerminalBlock block)
		{
			WeaponTargeting instance;
			if (TryGetWeaponTargeting(block, out instance))
				return instance.TerminalOptions.TargetingRange;
			return 0f;
		}

		private static void SetRange(IMyTerminalBlock block, float value)
		{
			WeaponTargeting instance;
			if (TryGetWeaponTargeting(block, out instance))
				instance.TerminalOptions.TargetingRange = value;
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

		private static bool GetFlag(IMyTerminalBlock block, TargetType flag)
		{
			WeaponTargeting instance;
			if (TryGetWeaponTargeting(block, out instance))
				return instance.TerminalOptions.CanTargetType(flag);
			return false;
		}

		private static void SetFlag(IMyTerminalBlock block, TargetType flag, bool value)
		{
			WeaponTargeting instance;
			if (!TryGetWeaponTargeting(block, out instance))
				return;
			if (value)
				instance.TerminalOptions.CanTarget |= flag;
			else
				instance.TerminalOptions.CanTarget &= ~flag;
		}

		private static bool GetFlag(IMyTerminalBlock block, TargetingFlags flag)
		{
			WeaponTargeting instance;
			if (TryGetWeaponTargeting(block, out instance))
				return instance.TerminalOptions.FlagSet(flag);
			return false;
		}

		private static void SetFlag(IMyTerminalBlock block, TargetingFlags flag, bool value)
		{
			WeaponTargeting instance;
			if (!TryGetWeaponTargeting(block, out instance))
				return;
			if (value)
				instance.TerminalOptions.Flags |= flag;
			else
				instance.TerminalOptions.Flags &= ~flag;
		}

		private static StringBuilder GetBlockList(IMyTerminalBlock block)
		{
			WeaponTargeting instance;
			if (!TryGetWeaponTargeting(block, out instance))
				return new StringBuilder();

			return instance.m_blockListBox;
		}

		private static void SetBlockList(IMyTerminalBlock block, StringBuilder value)
		{
			WeaponTargeting instance;
			if (!TryGetWeaponTargeting(block, out instance))
				return;

			instance.m_blockListBox = value;
		}

		private static StringBuilder GetTargetEntity(IMyTerminalBlock block)
		{
			WeaponTargeting instance;
			if (!TryGetWeaponTargeting(block, out instance))
				return new StringBuilder();

			return instance.m_targetEntityBox;
		}

		private static void SetTargetEntity(IMyTerminalBlock block, StringBuilder value)
		{
			WeaponTargeting instance;
			if (!TryGetWeaponTargeting(block, out instance))
				return;

			instance.m_targetEntityBox = value;
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
		public readonly NetworkClient m_netClient;

		public readonly WeaponDefinitionExpanded WeaponDefinition;

		private bool dirty_targetEntityBox = true, dirty_blockListBox = true;
		private StringBuilder value_targetEntityBox = new StringBuilder(), value_blockListBox = new StringBuilder();

		private StringBuilder m_blockListBox
		{
			get { return value_blockListBox; }
			set
			{
				value_blockListBox = value;
				dirty_blockListBox = true;
			}
		}

		private StringBuilder m_targetEntityBox
		{
			get { return value_targetEntityBox; }
			set
			{
				value_targetEntityBox = value;
				dirty_targetEntityBox = true;
			}
		}

		public Control CurrentControl
		{
			get { return value_currentControl; }
			set
			{
				if (value_currentControl == value)
					return;

				//myLogger.debugLog("Control changed from " + value_currentControl + " to " + value, "get_CurrentControl()");

				if (IsNormalTurret && MyAPIGateway.Multiplayer.IsServer)
				{
					if (value == Control.Off)
						GameThreadActions.Enqueue(() => myTurret.ResetTargetingToDefault());
					else
						GameThreadActions.Enqueue(() => myTurret.SetTarget(ProjectilePosition() + (CubeBlock.WorldMatrix.Backward + CubeBlock.WorldMatrix.Up) * 10));
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

		public bool GuidedLauncher { get; set; }

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

			if (Static.TPro_Shoot == null)
				Static.TPro_Shoot = (weapon as IMyTerminalBlock).GetProperty("Shoot").AsBool();

			if (WeaponDescription.GetFor(weapon).LastSeenTargeting)
				m_netClient = new NetworkClient(weapon);

			WeaponDefinition = MyDefinitionManager.Static.GetWeaponDefinition(((MyWeaponBlockDefinition)weapon.GetCubeBlockDefinition()).WeaponDefinitionId);

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

			if (CurrentControl == Control.Off)
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

			if ((CurrentTarget.TType == TargetType.None || CurrentTarget is LastSeenTarget) && m_netClient != null)
				GetLastSeenTarget(m_netClient.GetStorage(), LoadedAmmo.MissileDefinition.MaxTrajectory);
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

			if (dirty_blockListBox)
			{
				dirty_blockListBox = false;
				TerminalOptions.blocksToTarget = m_blockListBox.ToString().LowerRemoveWhitespace().Split(',');
				myLogger.debugLog("rebuilt block list from " + m_blockListBox.ToString() + ", count: " + (TerminalOptions.blocksToTarget != null ? TerminalOptions.blocksToTarget.Length : 0));
			}
			if (dirty_targetEntityBox)
			{
				dirty_targetEntityBox = false;
				long entityId;
				if (long.TryParse(m_targetEntityBox.ToString().RemoveWhitespace(), out entityId))
					TerminalOptions.TargetEntityId = entityId;
				else
					TerminalOptions.TargetEntityId = null;
				myLogger.debugLog("got entity id from " + m_targetEntityBox.ToString() + ": " + TerminalOptions.TargetEntityId);
			}

			Interpreter.UpdateInstruction();
			Options.Assimilate(TerminalOptions, Interpreter.Options);
			Update100_Options_TargetingThread(Options);

			if (CurrentControl == Control.Engager)
				return;

			if (IsNormalTurret ?
				(Interpreter.HasInstructions || Options.FlagSet(TargetingFlags.ArmsEnabled)) :
				Options.FlagSet(TargetingFlags.ArmsEnabled))
			{
				CurrentControl = Control.On;
				return;
			}

			//myLogger.debugLog("Not running targeting", "Update100()");
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
				if (++facingWrongWayFor > 9)
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
		private bool prev_working, prev_playerControl, prev_noOwn, prev_ammo, prev_range, prev_grids;
		private int prev_errors;
		private long prev_target;
		private Control prev_control;

		/// <summary>
		/// Look for changes that would affect custom info.
		/// </summary>
		private void CheckCustomInfo()
		{
			condition_changed = false;

			ConditionChange(CubeBlock.IsWorking, ref prev_working);
			ConditionChange(IsNormalTurret && myTurret.IsUnderControl, ref prev_playerControl);
			ConditionChange(CubeBlock.OwnerId == 0, ref prev_noOwn);
			ConditionChange(Options.TargetingRange < 1f, ref prev_range);
			ConditionChange(Options.CanTargetType(TargetType.AllGrid | TargetType.Destroy), ref prev_range);

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

			if (!CubeBlock.IsWorking)
			{
				customInfo.AppendLine("Off");
				return;
			}
			if (IsNormalTurret && myTurret.IsUnderControl)
			{
				customInfo.AppendLine("Being controlled by player");
				return;
			}
			if (CubeBlock.OwnerId == 0)
				customInfo.AppendLine("No owner");

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
			if (Options.TargetingRange < 1f)
				customInfo.AppendLine("Range is zero");
			if (!Options.CanTargetType(TargetType.AllGrid | TargetType.Destroy))
				customInfo.AppendLine("Not targeting ships");
			if (CurrentTarget.Entity == null)
				customInfo.AppendLine("No target");
			else
			{
				customInfo.Append("Has target: ");
				customInfo.AppendLine(CurrentTarget.Entity.GetNameForDisplay(CubeBlock.OwnerId));
			}
		}

	}
}
