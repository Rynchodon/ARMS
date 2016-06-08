using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Entities.Blocks;
using Rynchodon.Settings;
using Rynchodon.Utility;
using Rynchodon.Utility.Network;
using Rynchodon.Utility.Vectors;
using Sandbox.Definitions;
using Sandbox.Game.Entities;
using Sandbox.Game.Entities.Cube;
using Sandbox.Game.Gui;
using Sandbox.ModAPI;
using Sandbox.ModAPI.Interfaces.Terminal;
using VRage.Game;
using VRage.Game.Entity;
using VRage.Game.ModAPI;
using VRage.ModAPI;
using VRage.Utils;
using VRageMath;
using Ingame = Sandbox.ModAPI.Ingame;

namespace Rynchodon.AntennaRelay
{
	/// <summary>
	/// Projects miniature ships. Could do asteroids / planets in the future or other entities.
	/// </summary>
	public class Projector
	{

		private enum Option : byte
		{
			None = 0,
			Owner = ExtensionsRelations.Relations.Owner,
			Faction = ExtensionsRelations.Relations.Faction,
			Neutral = ExtensionsRelations.Relations.Neutral,
			Enemy = ExtensionsRelations.Relations.Enemy,
			ThisShip = 128,
			OnOff = 16,
			IntegrityColours = 32,
			ShowOffset = 64
		}

		private class StaticVariables
		{
			public readonly Vector3[] Directions = { Vector3.Zero, Vector3.Right, Vector3.Left, Vector3.Up, Vector3.Down, Vector3.Backward, Vector3.Forward };

			/// <summary>Maximum time since detection for entity to be displayed.</summary>
			public readonly TimeSpan displayAllowed = new TimeSpan(0, 0, 2);
			/// <summary>Maximum time since detection for entity to be kept in cache.</summary>
			public readonly TimeSpan keepInCache = new TimeSpan(0, 1, 0);

			public readonly Logger logger = new Logger("Projector");
			public readonly List<IMyTerminalControl> TermControls = new List<IMyTerminalControl>();
			public readonly List<IMyTerminalControl> TermControls_Colours = new List<IMyTerminalControl>();
			public readonly List<IMyTerminalControl> TermControls_Offset = new List<IMyTerminalControl>();

			public bool MouseControls;
			public Color
				value_IntegrityFull = new Color(UserSettings.GetSetting(UserSettings.IntSettingName.IntegrityFull)),
				value_IntegrityFunctional = new Color(UserSettings.GetSetting(UserSettings.IntSettingName.IntegrityFunctional)),
				value_IntegrityDamaged = new Color(UserSettings.GetSetting(UserSettings.IntSettingName.IntegrityDamaged)),
				value_IntegrityZero = new Color(UserSettings.GetSetting(UserSettings.IntSettingName.IntegrityZero));
		}

		public static Color IntegrityFull
		{
			get { return Static.value_IntegrityFull; }
			set
			{
				Static.value_IntegrityFull = value;
				UserSettings.SetSetting(UserSettings.IntSettingName.IntegrityFull, value.PackedValue);
			}
		}

		public static Color IntegrityFunctional
		{
			get { return Static.value_IntegrityFunctional; }
			set
			{
				Static.value_IntegrityFunctional = value;
				UserSettings.SetSetting(UserSettings.IntSettingName.IntegrityFunctional, value.PackedValue);
			}
		}

		public static Color IntegrityDamaged
		{
			get { return Static.value_IntegrityDamaged; }
			set
			{
				Static.value_IntegrityDamaged = value;
				UserSettings.SetSetting(UserSettings.IntSettingName.IntegrityDamaged, value.PackedValue);
			}
		}

		public static Color IntegrityZero
		{
			get { return Static.value_IntegrityZero; }
			set
			{
				Static.value_IntegrityZero = value;
				UserSettings.SetSetting(UserSettings.IntSettingName.IntegrityZero, value.PackedValue);
			}
		}

		public static bool ShowBoundary
		{
			get { return UserSettings.GetSetting(UserSettings.BoolSettingName.HologramShowBoundary); }
			set { UserSettings.SetSetting(UserSettings.BoolSettingName.HologramShowBoundary, value); }
		}

		private class SeenHolo
		{
			public LastSeen Seen;
			public MyEntity Holo;
			public bool ColouredByIntegrity;
		}

		private const float InputScrollMulti = 1f / 120f, ScrollRangeMulti = 1.1f;
		private const float MinRangeDetection = 1e2f, MaxRangeDetection = 1e5f, DefaultRangeDetection = 1e3f;
		private const float MinRadiusHolo = 1f, MaxRadiusHolo = 10f, DefaultRadiusHolo = 2.5f;
		private const float MinSizeScale = 1f, MaxSizeScale = 1e3f, DefaultSizeScale = 1f;
		private const float MinOffset = -20f, MaxOffset = 20f;
		private const double CrosshairRange = 20d;

		private static StaticVariables Static = new StaticVariables();

		static Projector()
		{
			MyAPIGateway.Entities.OnCloseAll += Entities_OnCloseAll;
			MyTerminalControls.Static.CustomControlGetter += CustomControlGetter;

			MyAPIGateway.Session.DamageSystem.RegisterAfterDamageHandler((int)MyDamageSystemPriority.Low, AfterDamageHandler);

			MyTerminalControlFactory.AddControl(new MyTerminalControlSeparator<MySpaceProjector>());

			AddCheckbox("HoloDisplay", "Holographic Display", "Holographically display this ship and nearby detected ships", Option.OnOff);
			AddCheckbox("HD_This Ship", "This Ship", "Holographically display this ship", Option.ThisShip);
			AddCheckbox("HD_Owner", "Owned Ships", "Holographically display ships owned by this block's owner", Option.Owner);
			AddCheckbox("HD_Faction", "Faction Ships", "Holographically display faction owned ships", Option.Faction);
			AddCheckbox("HD_Neutral", "Neutral Ships", "Holographically display neutral ships", Option.Neutral);
			AddCheckbox("HD_Enemy", "Enemy Ships", "Holographically display enemy ships", Option.Enemy);

			MyTerminalControlSlider<MySpaceProjector> slider = new MyTerminalControlSlider<MySpaceProjector>("HD_RangeDetection", MyStringId.GetOrCompute("Detection Range"), MyStringId.GetOrCompute("Maximum distance of detected entity"));
			slider.DefaultValue = DefaultRangeDetection;
			slider.Normalizer = (block, value) => Normalizer(MinRangeDetection, MaxRangeDetection, block, value);
			slider.Denormalizer = (block, value) => Denormalizer(MinRangeDetection, MaxRangeDetection, block, value);
			slider.Writer = (block, sb) => WriterMetres(GetRangeDetection, block, sb);
			IMyTerminalValueControl<float> valueControl = slider;
			valueControl.Getter = GetRangeDetection;
			valueControl.Setter = SetRangeDetection;
			Static.TermControls.Add(slider);

			slider = new MyTerminalControlSlider<MySpaceProjector>("HD_RadiusHolo", MyStringId.GetOrCompute("Hologram Radius"), MyStringId.GetOrCompute("Maximum radius of hologram"));
			slider.DefaultValue = DefaultRadiusHolo;
			slider.Normalizer = (block, value) => Normalizer(MinRadiusHolo, MaxRadiusHolo, block, value);
			slider.Denormalizer = (block, value) => Denormalizer(MinRadiusHolo, MaxRadiusHolo, block, value);
			slider.Writer = (block, sb) => WriterMetres(GetRadiusHolo, block, sb);
			valueControl = slider;
			valueControl.Getter = GetRadiusHolo;
			valueControl.Setter = SetRadiusHolo;
			Static.TermControls.Add(slider);

			slider = new MyTerminalControlSlider<MySpaceProjector>("HD_EntitySizeScale", MyStringId.GetOrCompute("Entity Size Scale"), MyStringId.GetOrCompute("Larger value causes entities to appear larger"));
			slider.DefaultValue = DefaultSizeScale;
			slider.Normalizer = (block, value) => Normalizer(MinSizeScale, MaxSizeScale, block, value);
			slider.Denormalizer = (block, value) => Denormalizer(MinSizeScale, MaxSizeScale, block, value);
			slider.Writer = (block, sb) => sb.Append(GetSizeScale(block));
			valueControl = slider;
			valueControl.Getter = GetSizeScale;
			valueControl.Setter = SetSizeScale;
			Static.TermControls.Add(slider);

			Static.TermControls.Add(new MyTerminalControlSeparator<MySpaceProjector>());

			//MyTerminalControlCheckbox<MySpaceProjector> control = new MyTerminalControlCheckbox<MySpaceProjector>("HD_MouseControls", MyStringId.GetOrCompute("Mouse Controls"),
			//	MyStringId.GetOrCompute("Allow manipulation of hologram with mouse. User-specific setting."));
			//IMyTerminalValueControl<bool> valueControlBool = control;
			//valueControlBool.Getter = block => Static.MouseControls;
			//valueControlBool.Setter = (block, value) => Static.MouseControls = value;
			//Static.TermControls.Add(control);

			MyTerminalControlCheckbox<MySpaceProjector> control = new MyTerminalControlCheckbox<MySpaceProjector>("HD_ShowBoundary", MyStringId.GetOrCompute("Show Boundary"), MyStringId.GetOrCompute("Show the boundaries of the hologram. User-specific setting."));
			IMyTerminalValueControl<bool> valueControlBool = control;
			valueControlBool.Getter = block => ShowBoundary;
			valueControlBool.Setter = (block, value) => ShowBoundary = value;
			Static.TermControls.Add(control);

			AddCheckbox("HD_ShowOffset", "Show Offset Controls", "Display controls that can be used to adjust the position of the hologram", Option.ShowOffset);

			AddOffsetSlider("HD_OffsetX", "Right/Left Offset", "+ve moves hologram to the right, -ve moves hologram to the left", 0);
			AddOffsetSlider("HD_OffsetY", "Up/Down Offset", "+ve moves hologram up, -ve moves hologram down", 1);
			AddOffsetSlider("HD_OffsetZ", "Back/Fore Offset", "+ve moves hologram back, -ve moves hologram forward", 2);

			Static.TermControls_Offset.Add(new MyTerminalControlSeparator<MySpaceProjector>());

			AddCheckbox("HD_IntegrityColour", "Colour by Integrity", "Colour blocks according to their integrities", Option.IntegrityColours);

			IMyTerminalControlColor colour = MyAPIGateway.TerminalControls.CreateControl<IMyTerminalControlColor, IMyProjector>("HD_FullIntegriyColour");
			colour.Title = MyStringId.GetOrCompute("Whole");
			colour.Tooltip = MyStringId.GetOrCompute("Colour when block has full integrity. User-specific setting.");
			colour.Getter = (block) => IntegrityFull;
			colour.Setter = (block, value) => IntegrityFull = value;
			Static.TermControls_Colours.Add(colour);

			colour = MyAPIGateway.TerminalControls.CreateControl<IMyTerminalControlColor, IMyProjector>("HD_CriticalIntegriyColour");
			colour.Title = MyStringId.GetOrCompute("Func.");
			colour.Tooltip = MyStringId.GetOrCompute("Colour when block is just above critical integrity. User-specific setting.");
			colour.Getter = (block) => IntegrityFunctional;
			colour.Setter = (block, value) => IntegrityFunctional = value;
			Static.TermControls_Colours.Add(colour);

			colour = MyAPIGateway.TerminalControls.CreateControl<IMyTerminalControlColor, IMyProjector>("HD_CriticalIntegriyColour");
			colour.Title = MyStringId.GetOrCompute("Broken");
			colour.Tooltip = MyStringId.GetOrCompute("Colour when block is just below critical integrity. User-specific setting.");
			colour.Getter = (block) => IntegrityDamaged;
			colour.Setter = (block, value) => IntegrityDamaged = value;
			Static.TermControls_Colours.Add(colour);

			colour = MyAPIGateway.TerminalControls.CreateControl<IMyTerminalControlColor, IMyProjector>("HD_ZeroIntegriyColour");
			colour.Title = MyStringId.GetOrCompute("Razed");
			colour.Tooltip = MyStringId.GetOrCompute("Colour when block has zero integrity. User-specific setting.");
			colour.Getter = (block) => IntegrityZero;
			colour.Setter = (block, value) => IntegrityZero = value;
			Static.TermControls_Colours.Add(colour);
		}

		private static void Entities_OnCloseAll()
		{
			MyAPIGateway.Entities.OnCloseAll -= Entities_OnCloseAll;
			MyTerminalControls.Static.CustomControlGetter -= CustomControlGetter;
			Static = null;
		}

		#region Terminal Controls

		private static void AddCheckbox(string id, string title, string toolTip, Option opt)
		{
			MyTerminalControlCheckbox<MySpaceProjector> control = new MyTerminalControlCheckbox<MySpaceProjector>(id, MyStringId.GetOrCompute(title), MyStringId.GetOrCompute(toolTip));
			IMyTerminalValueControl<bool> valueControl = control;
			valueControl.Getter = block => GetOptionTerminal(block, opt);
			valueControl.Setter = (block, value) => SetOptionTerminal(block, opt, value);
			if (Static.TermControls.Count == 0)
				MyTerminalControlFactory.AddControl(control);
			Static.TermControls.Add(control);
		}

		private static void AddOffsetSlider(string id, string title, string toolTip, int dim)
		{
			MyTerminalControlSlider<MySpaceProjector> control = new MyTerminalControlSlider<MySpaceProjector>(id, MyStringId.GetOrCompute(title), MyStringId.GetOrCompute(toolTip));
			Func<IMyTerminalBlock, float> getter = block => GetOffset(block, dim);
			control.DefaultValue = dim == 1 ? 2.5f : 0f;
			control.Normalizer = (block, value) => Normalizer(MinOffset, MaxOffset, block, value);
			control.Denormalizer = (block, value) => Denormalizer(MinOffset, MaxOffset, block, value);
			control.Writer = (block, sb) => WriterMetres(getter, block, sb);
			IMyTerminalValueControl<float> valueControl = control;
			valueControl.Getter = getter;
			valueControl.Setter = (block, value) => SetOffset(block, dim, value);
			Static.TermControls_Offset.Add(control);
		}

		private static void CustomControlGetter(IMyTerminalBlock block, List<IMyTerminalControl> controls)
		{
			if (GetOptionTerminal(block, Option.OnOff))
			{
				// find show on hud
				int indexSOH = 0;
				for (; indexSOH < controls.Count && controls[indexSOH].Id != "ShowOnHUD"; indexSOH++) ;
				// remove all controls after ShowOnHUD and before separator
				controls.RemoveRange(indexSOH + 1, controls.Count - indexSOH - 3);

				bool showOffset = GetOptionTerminal(block, Option.ShowOffset);

				for (int index = 1; index < Static.TermControls.Count; index++)
				{
					controls.Add(Static.TermControls[index]);
					if (showOffset && Static.TermControls[index].Id == "HD_ShowOffset")
					{
						showOffset = false;
						foreach (var offset in Static.TermControls_Offset)
							controls.Add(offset);
					}
				}

				if (GetOptionTerminal(block, Option.IntegrityColours))
					foreach (var colour in Static.TermControls_Colours)
						controls.Add(colour);
			}
		}

		private static bool GetOptionTerminal(IMyTerminalBlock block, Option opt)
		{
			Projector proj;
			if (!Registrar.TryGetValue(block, out proj))
				return false;

			return (proj.m_options.Value & opt) != 0;
		}

		private static void SetOptionTerminal(IMyTerminalBlock block, Option opt, bool value)
		{
			Projector proj;
			if (!Registrar.TryGetValue(block, out proj))
				return;

			if (value)
				proj.m_options.Value |= opt;
			else
				proj.m_options.Value &= ~opt;

			if (opt == Option.OnOff || opt == Option.ShowOffset || opt == Option.IntegrityColours)
				MyGuiScreenTerminal.SwitchToControlPanelBlock((MyTerminalBlock)block);
		}

		private static float GetRangeDetection(IMyTerminalBlock block)
		{
			Projector proj;
			if (!Registrar.TryGetValue(block, out proj))
				return 0f;

			return proj.m_rangeDetection.Value;
		}

		private static void SetRangeDetection(IMyTerminalBlock block, float value)
		{
			Projector proj;
			if (!Registrar.TryGetValue(block, out proj))
				return;

			proj.m_rangeDetection.Value = value;
		}

		private static float GetRadiusHolo(IMyTerminalBlock block)
		{
			Projector proj;
			if (!Registrar.TryGetValue(block, out proj))
				return 0f;

			return proj.m_radiusHolo.Value;
		}

		private static void SetRadiusHolo(IMyTerminalBlock block, float value)
		{
			Projector proj;
			if (!Registrar.TryGetValue(block, out proj))
				return;

			proj.m_radiusHolo.Value = value;
		}

		private static float GetSizeScale(IMyTerminalBlock block)
		{
			Projector proj;
			if (!Registrar.TryGetValue(block, out proj))
				return 0f;

			return proj.m_sizeDistScale.Value;
		}

		private static void SetSizeScale(IMyTerminalBlock block, float value)
		{
			Projector proj;
			if (!Registrar.TryGetValue(block, out proj))
				return;

			proj.m_sizeDistScale.Value = value;
		}

		private static float GetOffset(IMyTerminalBlock block, int dim)
		{
			Projector proj;
			if (!Registrar.TryGetValue(block, out proj))
				return 0f;

			return proj.m_offset_ev.Value.GetDim(dim);
		}

		private static void SetOffset(IMyTerminalBlock block, int dim, float value)
		{
			Projector proj;
			if (!Registrar.TryGetValue(block, out proj))
				return;

			Vector3 offset = proj.m_offset_ev.Value;//.SetDim(dim, value);
			offset.SetDim(dim, value);
			proj.m_offset_ev.Value = offset;
		}

		private static float Normalizer(float min, float max, IMyTerminalBlock block, float value)
		{
			return (value - min) / (max - min);
		}

		private static float Denormalizer(float min, float max, IMyTerminalBlock block, float value)
		{
			return min + value * (max - min);
		}

		private static void WriterMetres(Func<IMyTerminalBlock, float> Getter, IMyTerminalBlock block, StringBuilder stringBuilder)
		{
			stringBuilder.Append(PrettySI.makePretty(Getter(block)));
			stringBuilder.Append("m");
		}

		private static void UpdateVisual()
		{
			foreach (var control in Static.TermControls)
				control.UpdateVisual();
		}

		#endregion Terminal Controls

		private static void AfterDamageHandler(object obj, MyDamageInformation damageInfo)
		{
			if (!(obj is IMySlimBlock))
				return;

			// grind damage is handled by OnBlockIntegrityChanged, which is faster
			if (damageInfo.Type == MyDamageType.Grind)
				return;

			IMySlimBlock block = (IMySlimBlock)obj;
			long gridId = block.CubeGrid.EntityId;
			Registrar.ForEach((Projector proj) => {
				SeenHolo sh;
				if (!proj.m_holoEntities.TryGetValue(gridId, out sh) || !sh.Holo.Render.Visible || !sh.ColouredByIntegrity)
					return;
				ColourBlock(block, (IMyCubeGrid)sh.Holo);
			});
		}

		/// <summary>
		/// Prepare a projected entity by disabling physics, game logic, etc.
		/// </summary>
		private static void SetupProjection(IMyEntity entity)
		{
			if (entity.Physics != null && entity.Physics.Enabled)
				entity.Physics.Enabled = false;

			MyCubeBlock block = entity as MyCubeBlock;
			if (block != null)
			{
				if (block.UseObjectsComponent.DetectorPhysics != null && block.UseObjectsComponent.DetectorPhysics.Enabled)
					block.UseObjectsComponent.DetectorPhysics.Enabled = false;
				block.NeedsUpdate = MyEntityUpdateEnum.NONE;

				if (block is Ingame.IMyBeacon || block is Ingame.IMyRadioAntenna)
					((IMyFunctionalBlock)block).RequestEnable(false);
			}

			foreach (var child in entity.Hierarchy.Children)
				SetupProjection(child.Container.Entity);
		}

		/// <summary>
		/// Colours a block according to its integrity.
		/// </summary>
		private static void ColourBlock(IMySlimBlock realBlock, IMyCubeGrid holoGrid)
		{
			Static.logger.debugLog(realBlock == null, "realBlock == null", Logger.severity.FATAL);

			float integrityRatio = (realBlock.BuildIntegrity - realBlock.CurrentDamage) / realBlock.MaxIntegrity;
			float criticalRatio = ((MyCubeBlockDefinition)realBlock.BlockDefinition).CriticalIntegrityRatio;

			float scaledRatio;
			Color blockColour;
			Static.logger.debugLog(integrityRatio != 1f, "integrityRatio: " + integrityRatio + ", criticalRatio: " + criticalRatio + ", fatblock: " + realBlock.FatBlock.getBestName() + ", functional: " + (realBlock.FatBlock != null && realBlock.FatBlock.IsFunctional));
			if (integrityRatio > criticalRatio && (realBlock.FatBlock == null || realBlock.FatBlock.IsFunctional))
			{
				scaledRatio = (integrityRatio - criticalRatio) / (1f - criticalRatio);
				blockColour = IntegrityFull * scaledRatio + IntegrityFunctional * (1f - scaledRatio);
			}
			else
			{
				scaledRatio = integrityRatio / criticalRatio;
				blockColour = IntegrityDamaged * scaledRatio + IntegrityZero * (1f - scaledRatio);
			}
			holoGrid.ColorBlocks(realBlock.Position, realBlock.Position, blockColour.ColorToHSVDX11());
		}

		/// <summary>
		/// Updates the appearance of the block if the integrity has changed. Only used for welding/griding.
		/// </summary>
		private static void UpdateBlockModel(IMySlimBlock realBlock, IMyCubeGrid holoGrid)
		{
			IMySlimBlock holoBlock = holoGrid.GetCubeBlock(realBlock.Position);
			Static.logger.debugLog(holoBlock == null, "holoBlock == null", Logger.severity.FATAL);

			float realIntegrityRatio = (realBlock.BuildIntegrity - realBlock.CurrentDamage) / realBlock.MaxIntegrity;
			float holoIntegrityRatio = (holoBlock.BuildIntegrity - holoBlock.CurrentDamage) / holoBlock.MaxIntegrity;

			if (realIntegrityRatio == holoIntegrityRatio)
				return;

			float min, max;
			if (realIntegrityRatio > holoIntegrityRatio)
			{
				max = realIntegrityRatio;
				min = holoIntegrityRatio;
			}
			else
			{
				max = holoIntegrityRatio;
				min = realIntegrityRatio;
			}

			if (((MyCubeBlockDefinition)realBlock.BlockDefinition).ModelChangeIsNeeded(min, max))
			{
				holoGrid.RemoveBlock(holoBlock);

				MyObjectBuilder_CubeBlock objBuilder = realBlock.GetObjectBuilder();
				objBuilder.EntityId = 0L;
				holoGrid.AddBlock(objBuilder, false);

				IMyCubeBlock cubeBlock = holoGrid.GetCubeBlock(realBlock.Position).FatBlock;
				if (cubeBlock != null)
					SetupProjection(cubeBlock);
			}
		}

		private readonly Logger m_logger;
		private readonly IMyCubeBlock m_block;
		private readonly NetworkClient m_netClient;

		private readonly Dictionary<long, SeenHolo> m_holoEntities = new Dictionary<long, SeenHolo>();
		/// <summary>List of entities to remove holo entirely, it will have to be re-created to be displayed again</summary>
		private readonly List<long> m_holoEntitiesRemove = new List<long>();

		private EntityValue<Option> m_options;
		/// <summary>How close a detected entity needs to be to be placed inside the holographic area.</summary>
		private EntityValue<float> m_rangeDetection;
		/// <summary>Maximum radius of holographic area, holo entities should fit inside a sphere of this size.</summary>
		private EntityValue<float> m_radiusHolo;
		/// <summary>Size scale is distance scale * m_sizeDistScale</summary>
		private EntityValue<float> m_sizeDistScale;
		/// <summary>Id of the real entity that is centred</summary>
		private EntityValue<long> m_centreEntityId;
		private EntityValue<Vector3> m_offset_ev;

		private DateTime m_clearAllAt = DateTime.UtcNow + Static.keepInCache;
		/// <summary>The real entity that is centred</summary>
		private IMyEntity value_centreEntity;
		private bool m_playerCanSee;

		private bool Enabled { get { return m_block.IsWorking && (m_options.Value & Option.OnOff) != 0; } }

		private IMyEntity m_centreEntity { get { return value_centreEntity ?? m_block; } }

		private PositionBlock m_offset { get { return m_offset_ev.Value; } }

		public Projector(IMyCubeBlock block)
		{
			this.m_logger = new Logger(GetType().Name, block);
			this.m_block = block;
			this.m_netClient = new NetworkClient(block);

			byte index = 0;
			this.m_options = new EntityValue<Option>(block, index++, UpdateVisual);
			this.m_rangeDetection = new EntityValue<float>(block, index++, UpdateVisual, DefaultRangeDetection);
			this.m_radiusHolo = new EntityValue<float>(block, index++, UpdateVisual, DefaultRadiusHolo);
			this.m_sizeDistScale = new EntityValue<float>(block, index++, UpdateVisual, DefaultSizeScale);
			this.m_centreEntityId = new EntityValue<long>(block, index++, m_centreEntityId_AfterValueChanged);
			this.m_offset_ev = new EntityValue<Vector3>(block, index++, UpdateVisual, new Vector3(0f, 2.5f, 0f));

			Registrar.Add(block, this);
		}

		/// <summary>
		/// Check opts, load/unload
		/// </summary>
		public void Update100()
		{
			if (m_holoEntities.Count != 0 && DateTime.UtcNow >= m_clearAllAt)
			{
				m_logger.debugLog("clearing all holo entities");
				foreach (SeenHolo sh in m_holoEntities.Values)
					OnRemove(sh);
				m_holoEntities.Clear();
			}

			if (!Enabled)
				return;

			Vector3D playerPos = MyAPIGateway.Session.Player.GetPosition(), holoCentre = m_offset.ToWorld(m_block);
			double distSquared = Vector3D.DistanceSquared(playerPos, holoCentre);
			m_playerCanSee = distSquared <= 1e4d;
			if (m_playerCanSee && distSquared > 100d)
			{
				List<MyLineSegmentOverlapResult<MyEntity>> entitiesInRay = ResourcePool<List<MyLineSegmentOverlapResult<MyEntity>>>.Get();

				m_playerCanSee = false;
				MyEntity[] ignore = new MyEntity[] { (MyEntity)MyAPIGateway.Session.Player.Controller.ControlledEntity };
				foreach (Vector3 vector in Static.Directions)
				{
					LineD ray = new LineD(playerPos, holoCentre + vector * m_radiusHolo.Value);
					MyGamePruningStructure.GetTopmostEntitiesOverlappingRay(ref ray, entitiesInRay);
					if (!RayCast.Obstructed(ray, entitiesInRay.Select(overlap => overlap.Element), ignore))
					{
						m_playerCanSee = true;
						entitiesInRay.Clear();
						break;
					}
					entitiesInRay.Clear();
				}

				ResourcePool<List<MyLineSegmentOverlapResult<MyEntity>>>.Return(entitiesInRay);
			}

			if (!m_playerCanSee)
				return;

			NetworkStorage storage = m_netClient.GetStorage();
			if (storage == null)
				return;

			m_clearAllAt = DateTime.UtcNow + Static.keepInCache;

			storage.ForEachLastSeen(CreateHolo);

			foreach (SeenHolo sh in m_holoEntities.Values)
			{
				if (CanDisplay(sh.Seen))
				{
					if (!sh.Holo.Render.Visible)
					{
						m_logger.debugLog("showing holo: " + sh.Seen.Entity.getBestName());
						SetupProjection(sh.Holo);
						SetVisible(sh.Holo, true);
					}
					if (sh.Seen.Entity is MyCubeGrid && sh.ColouredByIntegrity != ((m_options.Value & Option.IntegrityColours) != 0))
					{
						if (sh.ColouredByIntegrity)
							RestoreColour(sh);
						else
							ColourByIntegrity(sh);
					}
				}
				else if (sh.Holo.Render.Visible)
				{
					m_logger.debugLog("hiding holo: " + sh.Seen.Entity.getBestName());
					SetVisible(sh.Holo, false);
				}
			}

			if (m_holoEntitiesRemove.Count != 0)
			{
				foreach (long entityId in m_holoEntitiesRemove)
				{
					SeenHolo sh = m_holoEntities[entityId];
					OnRemove(sh);
					m_holoEntities.Remove(entityId);
				}
				m_holoEntitiesRemove.Clear();
			}
		}

		/// <summary>
		/// Updates positions & orientations
		/// </summary>
		public void Update1()
		{
			if (!Enabled || !m_playerCanSee)
			{
				foreach (SeenHolo sh in m_holoEntities.Values)
					if (sh.Holo.Render.Visible)
					{
						m_logger.debugLog("hiding holo: " + sh.Seen.Entity.getBestName());
						SetVisible(sh.Holo, false);
					}
				return;
			}

			//if (MyGuiScreenTerminal.GetCurrentScreen() == MyTerminalPageEnum.None && MyAPIGateway.Session.ControlledObject.Entity is IMyCharacter && Static.MouseControls)
			//	CheckInput();

			PositionWorld projectionCentre = m_offset.ToWorld(m_block);

			float distanceScale = m_radiusHolo.Value / m_rangeDetection.Value;
			float sizeScale = distanceScale * m_sizeDistScale.Value;

			foreach (SeenHolo sh in m_holoEntities.Values)
			{
				MatrixD worldMatrix = sh.Seen.Entity.WorldMatrix;
				worldMatrix.Translation = projectionCentre + (sh.Seen.Entity.GetPosition() - m_centreEntity.GetPosition()) * distanceScale;
				//m_logger.debugLog("entity: " + sh.Seen.Entity.getBestName() + "(" + sh.Seen.Entity.EntityId + "), centre: " + projectionCentre + ", offset: " + (worldMatrix.Translation - projectionCentre) + ", position: " + worldMatrix.Translation);
				sh.Holo.PositionComp.SetWorldMatrix(worldMatrix);
				sh.Holo.PositionComp.Scale = sizeScale;
			}

			if (ShowBoundary)
			{
				MatrixD sphereMatrix = m_block.WorldMatrix;
				sphereMatrix.Translation = projectionCentre;
				Color c = Color.Yellow;
				MySimpleObjectDraw.DrawTransparentSphere(ref sphereMatrix, m_radiusHolo.Value, ref c, MySimpleObjectRasterizer.Wireframe, 8);
			}
		}

		//private void CheckInput()
		//{
		//	MatrixD headMatrix = MyAPIGateway.Session.ControlledObject.GetHeadMatrix(true);
		//	RayD ray = new RayD(headMatrix.Translation, headMatrix.Forward);

		//	BoundingSphereD holoSphere = new BoundingSphereD(m_offset.ToWorld(m_block), m_radiusHolo.Value);
		//	double tmin, tmax;

		//	if (!holoSphere.IntersectRaySphere(ray, out tmin, out tmax) || tmin > CrosshairRange)
		//		return;

		//	int scroll = MyAPIGateway.Input.DeltaMouseScrollWheelValue();
		//	if (scroll != 0)
		//	{
		//		int scrollSteps = (int)Math.Round(scroll * InputScrollMulti);
		//		float rangeMulti = 1f;
		//		while (scrollSteps > 0)
		//		{
		//			rangeMulti *= ScrollRangeMulti;
		//			scrollSteps--;
		//		}
		//		while (scrollSteps < 0)
		//		{
		//			rangeMulti /= ScrollRangeMulti;
		//			scrollSteps++;
		//		}
		//		m_rangeDetection.Value *= rangeMulti;
		//	}

		//	if (MyAPIGateway.Input.IsNewRightMousePressed())
		//	{
		//		m_centreEntityId.Value = 0L;
		//	}
		//	else if (MyAPIGateway.Input.IsNewLeftMousePressed())
		//	{
		//		IMyEntity firstHit = null;
		//		double firstHitDistance = CrosshairRange;

		//		foreach (SeenHolo sh in m_holoEntities.Values)
		//			if (sh.Holo.Render.Visible && sh.Holo.PositionComp.WorldAABB.Intersect(ref ray, out tmin, out tmax) && tmin < firstHitDistance)
		//			{
		//				firstHit = sh.Seen.Entity;
		//				firstHitDistance = tmin;
		//			}

		//		if (firstHit != null)
		//			m_centreEntityId.Value = firstHit.EntityId;
		//	}
		//}

		private bool CanDisplay(LastSeen seen)
		{
			if (!CheckRelations(seen.Entity))
				return false;

			if (seen.Info == null)
				return false;

			TimeSpan time = Globals.ElapsedTime - seen.Info.DetectedAt;
			if (time > Static.displayAllowed)
			{
				if (time > Static.keepInCache)
					m_holoEntitiesRemove.Add(seen.Entity.EntityId);
				return false;
			}

			float rangeDetection = m_rangeDetection.Value; rangeDetection *= rangeDetection;
			return Vector3D.DistanceSquared(m_centreEntity.GetPosition(), seen.Entity.GetCentre()) <= rangeDetection;
		}

		private bool CheckRelations(IMyEntity entity)
		{
			return entity == m_block.CubeGrid ? (m_options.Value & Option.ThisShip) != 0 : (m_options.Value & (Option)m_block.getRelationsTo(entity)) != 0;
		}

		private void CreateHolo(LastSeen seen)
		{
			if (!(seen.Entity is IMyCubeGrid))
				return;

			SeenHolo sh;
			if (m_holoEntities.TryGetValue(seen.Entity.EntityId, out sh))
			{
				sh.Seen = seen;
				return;
			}

			if (!CanDisplay(seen))
				return;

			Profiler.StartProfileBlock(GetType().Name, "CreateHolo.GetObjectBuilder");
			MyObjectBuilder_CubeGrid builder = (MyObjectBuilder_CubeGrid)seen.Entity.GetObjectBuilder();
			Profiler.EndProfileBlock();

			MyEntities.RemapObjectBuilder(builder);

			builder.IsStatic = false;
			builder.CreatePhysics = false;
			builder.EnableSmallToLargeConnections = false;

			Profiler.StartProfileBlock(GetType().Name, "CreateHolo.CreateFromObjectBuilder");
			MyCubeGrid holo = (MyCubeGrid)MyEntities.CreateFromObjectBuilder(builder);
			Profiler.EndProfileBlock();

			holo.IsPreview = true;
			holo.Save = false;

			Profiler.StartProfileBlock(GetType().Name, "CreateHolo.SetupProjection");
			SetupProjection(holo);
			Profiler.EndProfileBlock();

			Profiler.StartProfileBlock(GetType().Name, "CreateHolo.AddEntity");
			MyAPIGateway.Entities.AddEntity(holo);
			Profiler.EndProfileBlock();

			m_logger.debugLog("created holo for " + seen.Entity.getBestName() + "(" + seen.Entity.EntityId + ")");
			m_holoEntities.Add(seen.Entity.EntityId, new SeenHolo() { Seen = seen, Holo = holo });

			MyCubeGrid actual = (MyCubeGrid)seen.Entity;
			actual.OnBlockAdded += Actual_OnBlockAdded;
			actual.OnBlockRemoved += Actual_OnBlockRemoved;
			actual.OnBlockIntegrityChanged += OnBlockIntegrityChanged;
		}

		/// <summary>
		/// Actions that must be performed when removing an entry from m_holoEntities
		/// </summary>
		private void OnRemove(SeenHolo sh)
		{
			MyEntities.Remove(sh.Holo);
			MyCubeGrid grid = sh.Seen.Entity as MyCubeGrid;
			if (grid != null)
			{
				grid.OnBlockAdded -= Actual_OnBlockAdded;
				grid.OnBlockRemoved -= Actual_OnBlockRemoved;
				grid.OnBlockIntegrityChanged -= OnBlockIntegrityChanged;
			}
		}

		private void Actual_OnBlockAdded(IMySlimBlock obj)
		{
			SeenHolo sh;
			if (!m_holoEntities.TryGetValue(obj.CubeGrid.EntityId, out sh))
			{
				m_logger.debugLog("failed lookup of grid: " + obj.CubeGrid.DisplayName, Logger.severity.ERROR);
				obj.CubeGrid.OnBlockAdded -= Actual_OnBlockAdded;
				obj.CubeGrid.OnBlockRemoved -= Actual_OnBlockRemoved;
				return;
			}

			IMyCubeGrid holo = (IMyCubeGrid)sh.Holo;
			MyObjectBuilder_CubeBlock objBuilder = obj.GetObjectBuilder();
			objBuilder.EntityId = 0L;
			holo.AddBlock(objBuilder, false);

			IMyCubeBlock cubeBlock = holo.GetCubeBlock(obj.Position).FatBlock;
			if (cubeBlock != null)
				SetupProjection(cubeBlock);
		}

		private void Actual_OnBlockRemoved(IMySlimBlock obj)
		{
			SeenHolo sh;
			if (!m_holoEntities.TryGetValue(obj.CubeGrid.EntityId, out sh))
			{
				m_logger.debugLog("failed lookup of grid: " + obj.CubeGrid.DisplayName, Logger.severity.ERROR);
				obj.CubeGrid.OnBlockAdded -= Actual_OnBlockAdded;
				obj.CubeGrid.OnBlockRemoved -= Actual_OnBlockRemoved;
				return;
			}

			Vector3I position = obj.Position;
			IMyCubeGrid holo = (IMyCubeGrid)sh.Holo;
			holo.RemoveBlock(holo.GetCubeBlock(position));
		}

		/// <summary>
		/// Show or Hide a holo entity.
		/// </summary>
		/// <param name="entity">Entity to show/hide</param>
		/// <param name="visible">Set Render.Visible to this bool</param>
		private void SetVisible(IMyEntity entity, bool visible)
		{
			entity.Render.Visible = visible;
			foreach (var child in entity.Hierarchy.Children)
				SetVisible(child.Container.Entity, visible);
		}

		private void ColourByIntegrity(SeenHolo sh)
		{
			m_logger.debugLog("colouring by integriy: " + sh.Seen.Entity.getBestName());
			MyCubeGrid realGrid = (MyCubeGrid)sh.Seen.Entity;
			IMyCubeGrid holoGrid = (IMyCubeGrid)sh.Holo;
			foreach (IMySlimBlock block in realGrid.CubeBlocks)
				ColourBlock(block, holoGrid);
			sh.ColouredByIntegrity = true;
		}

		private void RestoreColour(SeenHolo sh)
		{
			m_logger.debugLog("restoring original colour: " + sh.Seen.Entity.getBestName());
			MyCubeGrid realGrid = (MyCubeGrid)sh.Seen.Entity;
			MyCubeGrid holo = (MyCubeGrid)sh.Holo;
			foreach (IMySlimBlock block in realGrid.CubeBlocks)
				holo.ColorBlocks(block.Position, block.Position, block.GetColorMask(), false);
			sh.ColouredByIntegrity = false;
		}

		private void OnBlockIntegrityChanged(IMySlimBlock block)
		{
			SeenHolo sh;
			if (!m_holoEntities.TryGetValue(block.CubeGrid.EntityId, out sh))
			{
				m_logger.debugLog("grid entity id lookup failed", Logger.severity.ERROR);
				((MyCubeGrid)block.CubeGrid).OnBlockIntegrityChanged -= OnBlockIntegrityChanged;
				return;
			}
			IMyCubeGrid grid = (IMyCubeGrid)sh.Holo;
			if (sh.ColouredByIntegrity)
				ColourBlock(block, grid);
			UpdateBlockModel(block, grid);
		}

		private void m_centreEntityId_AfterValueChanged()
		{
			long entityId = m_centreEntityId.Value;
			if (entityId == 0L)
			{
				value_centreEntity = null;
			}
			else if (!MyAPIGateway.Entities.TryGetEntityById(entityId, out value_centreEntity))
			{
				m_logger.alwaysLog("Failed to get entity for id: " + entityId, Logger.severity.WARNING);
				value_centreEntity = null;
			}
			m_logger.debugLog("centre entity is now " + m_centreEntity.getBestName() + "(" + entityId + ")");
		}

	}
}
