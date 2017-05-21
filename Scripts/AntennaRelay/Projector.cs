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
using Sandbox.Game.Gui;
using Sandbox.ModAPI;
using Sandbox.ModAPI.Interfaces.Terminal;
using VRage.Game;
using VRage.Game.Entity;
using VRage.Game.ModAPI;
using VRage.ModAPI;
using VRage.ObjectBuilders;
using VRage.Utils;
using VRageMath;

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
			//ShowOffset = 64
		}

		private class StaticVariables
		{
			public readonly Vector3[] Directions = { Vector3.Zero, Vector3.Right, Vector3.Left, Vector3.Up, Vector3.Down, Vector3.Backward, Vector3.Forward };

			/// <summary>Maximum time since detection for entity to be displayed.</summary>
			public readonly TimeSpan displayAllowed = new TimeSpan(0, 0, 2);
			/// <summary>Maximum time since detection for entity to be kept in cache.</summary>
			public readonly TimeSpan keepInCache = new TimeSpan(0, 1, 0);

			public readonly List<IMyTerminalControl> TermControls = new List<IMyTerminalControl>();
			public readonly List<IMyTerminalControl> TermControls_Colours = new List<IMyTerminalControl>();
			public readonly List<IMyTerminalControl> TermControls_Offset = new List<IMyTerminalControl>();

			public bool MouseControls, ShowOffset;
			public Color
				value_IntegrityFull = new Color(UserSettings.GetSetting(UserSettings.IntSettingName.IntegrityFull)),
				value_IntegrityFunctional = new Color(UserSettings.GetSetting(UserSettings.IntSettingName.IntegrityFunctional)),
				value_IntegrityDamaged = new Color(UserSettings.GetSetting(UserSettings.IntSettingName.IntegrityDamaged)),
				value_IntegrityZero = new Color(UserSettings.GetSetting(UserSettings.IntSettingName.IntegrityZero));

			public StaticVariables()
			{
				Logger.DebugLog("entered", Logger.severity.TRACE);
				MyAPIGateway.Session.DamageSystem.RegisterAfterDamageHandler((int)MyDamageSystemPriority.Low, AfterDamageHandler);

				TerminalControlHelper.EnsureTerminalControlCreated<MySpaceProjector>();

				TermControls.Add(new MyTerminalControlSeparator<MySpaceProjector>());

				AddCheckbox("HoloDisplay", "Holographic Display", "Holographically display this ship and nearby detected ships", Option.OnOff);
				AddCheckbox("HD_This_Ship", "This Ship", "Holographically display this ship", Option.ThisShip);
				AddCheckbox("HD_Owner", "Owned Ships", "Holographically display ships owned by this block's owner", Option.Owner);
				AddCheckbox("HD_Faction", "Faction Ships", "Holographically display faction owned ships", Option.Faction);
				AddCheckbox("HD_Neutral", "Neutral Ships", "Holographically display neutral ships", Option.Neutral);
				AddCheckbox("HD_Enemy", "Enemy Ships", "Holographically display enemy ships", Option.Enemy);

				MyTerminalControlSlider<MySpaceProjector> slider = new MyTerminalControlSlider<MySpaceProjector>("HD_RangeDetection", MyStringId.GetOrCompute("Detection Range"), MyStringId.GetOrCompute("Maximum distance of detected entity"));
				ValueSync<float, Projector> tvsRange = new ValueSync<float, Projector>(slider, (proj) => proj.m_rangeDetection, (proj, value) => proj.m_rangeDetection = value);
				slider.DefaultValue = DefaultRangeDetection;
				slider.Normalizer = (block, value) => Normalizer(MinRangeDetection, MaxRangeDetection, block, value);
				slider.Denormalizer = (block, value) => Denormalizer(MinRangeDetection, MaxRangeDetection, block, value);
				slider.Writer = (block, sb) => WriterMetres(tvsRange.GetValue(block), sb);
				TermControls.Add(slider);

				slider = new MyTerminalControlSlider<MySpaceProjector>("HD_RadiusHolo", MyStringId.GetOrCompute("Hologram Radius"), MyStringId.GetOrCompute("Maximum radius of hologram"));
				ValueSync<float, Projector>  tvsRadius = new ValueSync<float, Projector>(slider, (proj) => proj.m_radiusHolo, (proj, value) => proj.m_radiusHolo = value);
				slider.DefaultValue = DefaultRadiusHolo;
				slider.Normalizer = (block, value) => Normalizer(MinRadiusHolo, MaxRadiusHolo, block, value);
				slider.Denormalizer = (block, value) => Denormalizer(MinRadiusHolo, MaxRadiusHolo, block, value);
				slider.Writer = (block, sb) => WriterMetres(tvsRadius.GetValue(block), sb);
				TermControls.Add(slider);

				slider = new MyTerminalControlSlider<MySpaceProjector>("HD_EntitySizeScale", MyStringId.GetOrCompute("Entity Size Scale"), MyStringId.GetOrCompute("Larger value causes entities to appear larger"));
				ValueSync<float, Projector> tvsScale = new ValueSync<float, Projector>(slider, (proj) => proj.m_sizeDistScale, (proj, value) => proj.m_sizeDistScale = value);
				slider.DefaultValue = DefaultSizeScale;
				slider.Normalizer = (block, value) => Normalizer(MinSizeScale, MaxSizeScale, block, value);
				slider.Denormalizer = (block, value) => Denormalizer(MinSizeScale, MaxSizeScale, block, value);
				slider.Writer = (block, sb) => sb.Append(tvsScale.GetValue(block));
				TermControls.Add(slider);

				TermControls.Add(new MyTerminalControlSeparator<MySpaceProjector>());

				MyTerminalControlCheckbox<MySpaceProjector> control = new MyTerminalControlCheckbox<MySpaceProjector>("HD_MouseControls", MyStringId.GetOrCompute("Mouse Controls"),
					MyStringId.GetOrCompute("Allow manipulation of hologram with mouse. User-specific setting."));
				IMyTerminalValueControl<bool> valueControlBool = control;
				valueControlBool.Getter = block => MouseControls;
				valueControlBool.Setter = (block, value) => MouseControls = value;
				TermControls.Add(control);

				control = new MyTerminalControlCheckbox<MySpaceProjector>("HD_ShowBoundary", MyStringId.GetOrCompute("Show Boundary"), MyStringId.GetOrCompute("Show the boundaries of the hologram. User-specific setting."));
				valueControlBool = control;
				valueControlBool.Getter = block => ShowBoundary;
				valueControlBool.Setter = (block, value) => ShowBoundary = value;
				TermControls.Add(control);

				control = new MyTerminalControlCheckbox<MySpaceProjector>("HD_ShowOffset", MyStringId.GetOrCompute("Show Offset Controls"), MyStringId.GetOrCompute("Display controls that can be used to adjust the position of the hologram. User-specific setting."));
				control.Getter = block => ShowOffset;
				control.Setter = (block, value) => {
					ShowOffset = value;
					IMyTerminalBlockExtensions.RebuildControls(null);
				};
				TermControls.Add(control);

				AddOffsetSlider("HD_OffsetX", "Right/Left Offset", "+ve moves hologram to the right, -ve moves hologram to the left", 0);
				AddOffsetSlider("HD_OffsetY", "Up/Down Offset", "+ve moves hologram up, -ve moves hologram down", 1);
				AddOffsetSlider("HD_OffsetZ", "Back/Fore Offset", "+ve moves hologram back, -ve moves hologram forward", 2);

				TermControls_Offset.Add(new MyTerminalControlSeparator<MySpaceProjector>());

				AddCheckbox("HD_IntegrityColour", "Colour by Integrity", "Colour blocks according to their integrities", Option.IntegrityColours);

				IMyTerminalControlColor colour = MyAPIGateway.TerminalControls.CreateControl<IMyTerminalControlColor, IMyProjector>("HD_FullIntegriyColour");
				colour.Title = MyStringId.GetOrCompute("Whole");
				colour.Tooltip = MyStringId.GetOrCompute("Colour when block has full integrity. User-specific setting.");
				colour.Getter = (block) => IntegrityFull;
				colour.Setter = (block, value) => IntegrityFull = value;
				TermControls_Colours.Add(colour);

				colour = MyAPIGateway.TerminalControls.CreateControl<IMyTerminalControlColor, IMyProjector>("HD_CriticalIntegriyColour");
				colour.Title = MyStringId.GetOrCompute("Func.");
				colour.Tooltip = MyStringId.GetOrCompute("Colour when block is just above critical integrity. User-specific setting.");
				colour.Getter = (block) => IntegrityFunctional;
				colour.Setter = (block, value) => IntegrityFunctional = value;
				TermControls_Colours.Add(colour);

				colour = MyAPIGateway.TerminalControls.CreateControl<IMyTerminalControlColor, IMyProjector>("HD_CriticalIntegriyColour");
				colour.Title = MyStringId.GetOrCompute("Broken");
				colour.Tooltip = MyStringId.GetOrCompute("Colour when block is just below critical integrity. User-specific setting.");
				colour.Getter = (block) => IntegrityDamaged;
				colour.Setter = (block, value) => IntegrityDamaged = value;
				TermControls_Colours.Add(colour);

				colour = MyAPIGateway.TerminalControls.CreateControl<IMyTerminalControlColor, IMyProjector>("HD_ZeroIntegriyColour");
				colour.Title = MyStringId.GetOrCompute("Razed");
				colour.Tooltip = MyStringId.GetOrCompute("Colour when block has zero integrity. User-specific setting.");
				colour.Getter = (block) => IntegrityZero;
				colour.Setter = (block, value) => IntegrityZero = value;
				TermControls_Colours.Add(colour);

				new ValueSync<long, Projector>("CentreEntity",
					 (script) => script.m_centreEntityId, 
					(script, value) => {
					script.m_centreEntityId = value;
					script.m_centreEntityId_AfterValueChanged();
				});
			}

			#region Terminal Controls

			private void AddCheckbox(string id, string title, string toolTip, Option opt)
			{
				MyTerminalControlCheckbox<MySpaceProjector> control = new MyTerminalControlCheckbox<MySpaceProjector>(id, MyStringId.GetOrCompute(title), MyStringId.GetOrCompute(toolTip));

				new ValueSync<bool, Projector>(control,
					(proj) => (proj.m_options & opt) == opt,
					(proj, value) => {
						Logger.DebugLog("set option: " + opt + " to " + value + ", current options: " + proj.m_options);
						if (value)
							proj.m_options |= opt;
						else
							proj.m_options &= ~opt;
						if (opt == Option.OnOff || opt == Option.IntegrityColours)
							proj.m_block.RebuildControls();
					});

				TermControls.Add(control);
			}

			private void AddOffsetSlider(string id, string title, string toolTip, int dim)
			{
				MyTerminalControlSlider<MySpaceProjector> control = new MyTerminalControlSlider<MySpaceProjector>(id, MyStringId.GetOrCompute(title), MyStringId.GetOrCompute(toolTip));

				ValueSync<float, Projector> tvs = new ValueSync<float, Projector>(control, (proj) => proj.m_offset_ev.GetDim(dim), (proj, value) => proj.m_offset_ev.SetDim(dim, value));

				control.DefaultValue = dim == 1 ? 2.5f : 0f;
				control.Normalizer = (block, value) => Normalizer(MinOffset, MaxOffset, block, value);
				control.Denormalizer = (block, value) => Denormalizer(MinOffset, MaxOffset, block, value);
				control.Writer = (block, sb) => WriterMetres(tvs.GetValue(block), sb);
				TermControls_Offset.Add(control);
			}

			public void CustomControlGetter(IMyTerminalBlock block, List<IMyTerminalControl> controls)
			{
				if (!(block is IMyProjector))
					return;

				Projector instance;
				if (!Registrar.TryGetValue(block, out instance))
				{
					if (!Globals.WorldClosed)
						Logger.AlwaysLog("Failed to get block: " + block.nameWithId());
					return;
				}

				controls.Add(TermControls[0]);
				controls.Add(TermControls[1]);

				if (instance.GetOption(Option.OnOff))
				{
					// find show on hud
					int indexSOH = 0;
					for (; indexSOH < controls.Count && controls[indexSOH].Id != "ShowOnHUD"; indexSOH++) ;
					// remove all controls after ShowOnHUD and before separator
					controls.RemoveRange(indexSOH + 1, controls.Count - indexSOH - 3);

					bool showOffset = ShowOffset;

					for (int index = 2; index < TermControls.Count; index++)
					{
						controls.Add(TermControls[index]);
						if (showOffset && TermControls[index].Id == "HD_ShowOffset")
						{
							showOffset = false;
							foreach (var offset in TermControls_Offset)
								controls.Add(offset);
						}
					}

					if (instance.GetOption(Option.IntegrityColours))
						foreach (var colour in TermControls_Colours)
							controls.Add(colour);
				}
			}

			private float Normalizer(float min, float max, IMyTerminalBlock block, float value)
			{
				return (value - min) / (max - min);
			}

			private float Denormalizer(float min, float max, IMyTerminalBlock block, float value)
			{
				return min + value * (max - min);
			}

			private void WriterMetres(float value, StringBuilder stringBuilder)
			{
				stringBuilder.Append(PrettySI.makePretty(value));
				stringBuilder.Append("m");
			}

			public void UpdateVisual()
			{
				foreach (var control in TermControls)
					control.UpdateVisual();
			}

			#endregion Terminal Controls

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

		private static StaticVariables Static;

		[OnWorldLoad]
		private static void Init()
		{
			Static = new StaticVariables();
			MyTerminalControls.Static.CustomControlGetter += Static.CustomControlGetter;
		}

		[OnWorldClose]
		private static void Unload()
		{
			MyTerminalControls.Static.CustomControlGetter -= Static.CustomControlGetter;
			Static = null;
		}

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
			//Static.logger.debugLog(entity is IMyCubeGrid, "setup: " + entity.nameWithId());
			//if (entity is IMyCubeBlock)
			//{
			//	IMyCubeBlock block2 = (IMyCubeBlock)entity;
			//	Static.logger.debugLog("setup: " + block2.nameWithId() + ", on " + block2.CubeGrid.nameWithId());
			//}

			if (entity.Physics != null && entity.Physics.Enabled)
				entity.Physics.Enabled = false;

			//Static.logger.debugLog("initial flags: " + entity.Flags);

			entity.Flags &= ~EntityFlags.NeedsResolveCastShadow;
			entity.Flags &= ~EntityFlags.Save;
			entity.Flags &= ~EntityFlags.Sync;
			entity.NeedsUpdate = MyEntityUpdateEnum.NONE;
			entity.PersistentFlags &= ~MyPersistentEntityFlags2.CastShadows;

			entity.Flags |= EntityFlags.SkipIfTooSmall;
			((MyEntity)entity).IsPreview = true;

			//Static.logger.debugLog("final flags: " + entity.Flags);

			MyCubeBlock block = entity as MyCubeBlock;
			if (block != null)
			{
				if (block.UseObjectsComponent.DetectorPhysics != null && block.UseObjectsComponent.DetectorPhysics.Enabled)
					block.UseObjectsComponent.DetectorPhysics.Enabled = false;
				//block.NeedsUpdate = MyEntityUpdateEnum.NONE;

				//if (block is Ingame.IMyBeacon || block is Ingame.IMyRadioAntenna)
				//	((IMyFunctionalBlock)block).RequestEnable(false);
			}

			foreach (var child in entity.Hierarchy.Children)
				SetupProjection(child.Container.Entity);
		}

		/// <summary>
		/// Colours a block according to its integrity.
		/// </summary>
		private static void ColourBlock(IMySlimBlock realBlock, IMyCubeGrid holoGrid)
		{
			Logger.DebugLog("realBlock == null", Logger.severity.FATAL, condition: realBlock == null);

			float integrityRatio = (realBlock.BuildIntegrity - realBlock.CurrentDamage) / realBlock.MaxIntegrity;
			float criticalRatio = ((MyCubeBlockDefinition)realBlock.BlockDefinition).CriticalIntegrityRatio;

			float scaledRatio;
			Color blockColour;
			Logger.DebugLog("integrityRatio: " + integrityRatio + ", criticalRatio: " + criticalRatio + ", fatblock: " + realBlock.FatBlock.getBestName() + ", functional: " + (realBlock.FatBlock != null && realBlock.FatBlock.IsFunctional), condition: integrityRatio != 1f);
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
			Logger.DebugLog("holoBlock == null", Logger.severity.FATAL, condition: holoBlock == null);

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

		private readonly IMyProjector m_block;
		private readonly RelayClient m_netClient;

		private readonly Dictionary<long, SeenHolo> m_holoEntities = new Dictionary<long, SeenHolo>();
		/// <summary>List of entities to remove holo entirely, it will have to be re-created to be displayed again</summary>
		private readonly List<long> m_holoEntitiesRemove = new List<long>();

		private Option m_options;
		/// <summary>How close a detected entity needs to be to be placed inside the holographic area.</summary>
		private float m_rangeDetection;
		/// <summary>Maximum radius of holographic area, holo entities should fit inside a sphere of this size.</summary>
		private float m_radiusHolo;
		/// <summary>Size scale is distance scale * m_sizeDistScale</summary>
		private float m_sizeDistScale;
		/// <summary>Id of the real entity that is centred</summary>
		private long m_centreEntityId;
		private Vector3 m_offset_ev;

		private DateTime m_clearAllAt = DateTime.UtcNow + Static.keepInCache;
		/// <summary>The real entity that is centred</summary>
		private IMyEntity value_centreEntity;
		private bool m_playerCanSee;

		private bool Enabled { get { return m_block.IsWorking && (m_options & Option.OnOff) != 0; } }

		private IMyEntity m_centreEntity { get { return value_centreEntity ?? m_block; } }

		private PositionBlock m_offset { get { return m_offset_ev; } }

		private Logable Log { get { return new Logable(m_block); } }

		public Projector(IMyCubeBlock block)
		{
			if (Static == null)
				throw new Exception("StaticVariables not loaded");

			this.m_block = (IMyProjector)block;
			this.m_netClient = new RelayClient(block);

			Registrar.Add(block, this);
		}

		/// <summary>
		/// Check opts, load/unload
		/// </summary>
		public void Update100()
		{
			if (m_holoEntities.Count != 0 && DateTime.UtcNow >= m_clearAllAt)
			{
				Log.DebugLog("clearing all holo entities");
				foreach (SeenHolo sh in m_holoEntities.Values)
				{
					Log.DebugLog("removing " + sh.Seen.Entity.EntityId + "from m_holoEntities (clear all)");
					OnRemove(sh);
				}
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
					LineD ray = new LineD(playerPos, holoCentre + vector * m_radiusHolo);
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

			RelayStorage storage = m_netClient.GetStorage();
			if (storage == null)
			{
				((IMyTerminalBlock)m_block).AppendCustomInfo("No network connection");
				return;
			}

			m_clearAllAt = DateTime.UtcNow + Static.keepInCache;

			storage.ForEachLastSeen(CreateHolo);

			foreach (SeenHolo sh in m_holoEntities.Values)
			{
				if (CanDisplay(sh.Seen))
				{
					if (!sh.Holo.Render.Visible)
					{
						Log.DebugLog("showing holo: " + sh.Seen.Entity.getBestName());
						SetupProjection(sh.Holo);
						SetVisible(sh.Holo, true);
					}
					if (sh.Seen.Entity is MyCubeGrid && sh.ColouredByIntegrity != ((m_options & Option.IntegrityColours) != 0))
					{
						if (sh.ColouredByIntegrity)
							RestoreColour(sh);
						else
							ColourByIntegrity(sh);
					}
				}
				else if (sh.Holo.Render.Visible)
				{
					Log.DebugLog("hiding holo: " + sh.Seen.Entity.getBestName());
					SetVisible(sh.Holo, false);
				}
			}

			if (m_holoEntitiesRemove.Count != 0)
			{
				foreach (long entityId in m_holoEntitiesRemove)
				{
					Log.DebugLog("removing " + entityId + "from m_holoEntities");
					SeenHolo sh;
					if (!m_holoEntities.TryGetValue(entityId, out sh))
					{
						// this may be normal
						Log.DebugLog("not in m_holoEntities: " + entityId, Logger.severity.WARNING);
						continue;
					}
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
						Log.DebugLog("hiding holo: " + sh.Seen.Entity.getBestName());
						SetVisible(sh.Holo, false);
					}
				return;
			}

			if (MyGuiScreenTerminal.GetCurrentScreen() == MyTerminalPageEnum.None && MyAPIGateway.Session.ControlledObject.Entity is IMyCharacter && Static.MouseControls)
				CheckInput();

			PositionWorld projectionCentre = m_offset.ToWorld(m_block);

			float distanceScale = m_radiusHolo / m_rangeDetection;
			float sizeScale = distanceScale * m_sizeDistScale;

			foreach (SeenHolo sh in m_holoEntities.Values)
			{
				MatrixD worldMatrix = sh.Seen.Entity.WorldMatrix;
				worldMatrix.Translation = projectionCentre + (sh.Seen.Entity.GetPosition() - m_centreEntity.GetPosition()) * distanceScale + (sh.Seen.Entity.GetPosition() - sh.Seen.Entity.GetCentre()) * (sizeScale - distanceScale);
				Log.DebugLog("entity: " + sh.Seen.Entity.getBestName() + "(" + sh.Seen.Entity.EntityId + "), centre: " + projectionCentre + ", offset: " + (worldMatrix.Translation - projectionCentre) + ", position: " + worldMatrix.Translation);
				sh.Holo.PositionComp.SetWorldMatrix(worldMatrix);
				sh.Holo.PositionComp.Scale = sizeScale;
			}

			if (ShowBoundary)
			{
				MatrixD sphereMatrix = m_block.WorldMatrix;
				sphereMatrix.Translation = projectionCentre;
				Color c = Color.Yellow;
				MySimpleObjectDraw.DrawTransparentSphere(ref sphereMatrix, m_radiusHolo, ref c, MySimpleObjectRasterizer.Wireframe, 8);
			}
		}

		private void CheckInput()
		{
			MatrixD headMatrix = MyAPIGateway.Session.ControlledObject.GetHeadMatrix(true);
			RayD ray = new RayD(headMatrix.Translation, headMatrix.Forward);

			BoundingSphereD holoSphere = new BoundingSphereD(m_offset.ToWorld(m_block), m_radiusHolo);
			double tmin, tmax;

			if (!holoSphere.IntersectRaySphere(ray, out tmin, out tmax) || tmin > CrosshairRange)
				return;

			int scroll = MyAPIGateway.Input.DeltaMouseScrollWheelValue();
			if (scroll != 0)
			{
				int scrollSteps = (int)Math.Round(scroll * InputScrollMulti);
				float rangeMulti = 1f;
				while (scrollSteps > 0)
				{
					rangeMulti *= ScrollRangeMulti;
					scrollSteps--;
				}
				while (scrollSteps < 0)
				{
					rangeMulti /= ScrollRangeMulti;
					scrollSteps++;
				}
				m_rangeDetection *= rangeMulti;
			}

			if (MyAPIGateway.Input.IsNewRightMousePressed())
			{
				m_centreEntityId = 0L;
			}
			else if (MyAPIGateway.Input.IsNewLeftMousePressed())
			{
				IMyEntity firstHit = null;
				double firstHitDistance = CrosshairRange;

				foreach (SeenHolo sh in m_holoEntities.Values)
					if (sh.Holo.Render.Visible && sh.Holo.PositionComp.WorldAABB.Intersect(ref ray, out tmin, out tmax) && tmin < firstHitDistance)
					{
						firstHit = sh.Seen.Entity;
						firstHitDistance = tmin;
					}

				if (firstHit != null)
					m_centreEntityId = firstHit.EntityId;
			}
		}

		private bool CanDisplay(LastSeen seen)
		{
			if (!CheckRelations(seen.Entity))
				return false;

			TimeSpan time = Globals.ElapsedTime - seen.RadarInfoTime();
			if (time > Static.displayAllowed)
			{
				if (time > Static.keepInCache)
					m_holoEntitiesRemove.Add(seen.Entity.EntityId);
				return false;
			}

			float rangeDetection = m_rangeDetection; rangeDetection *= rangeDetection;
			return Vector3D.DistanceSquared(m_centreEntity.GetPosition(), seen.Entity.GetCentre()) <= rangeDetection;
		}

		private bool CheckRelations(IMyEntity entity)
		{
			return entity == m_block.CubeGrid ? (m_options & Option.ThisShip) != 0 : (m_options & (Option)m_block.getRelationsTo(entity)) != 0;
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

			Profiler.StartProfileBlock("CreateHolo.GetObjectBuilder");
			MyObjectBuilder_CubeGrid builder = (MyObjectBuilder_CubeGrid)seen.Entity.GetObjectBuilder();
			Profiler.EndProfileBlock();

			MyEntities.RemapObjectBuilder(builder);

			builder.IsStatic = false;
			builder.CreatePhysics = false;
			builder.EnableSmallToLargeConnections = false;

			Profiler.StartProfileBlock("CreateHolo.CreateFromObjectBuilder");
			MyCubeGrid holo = (MyCubeGrid)MyEntities.CreateFromObjectBuilder(builder);
			Profiler.EndProfileBlock();

			Profiler.StartProfileBlock("CreateHolo.SetupProjection");
			SetupProjection(holo);
			Profiler.EndProfileBlock();

			Profiler.StartProfileBlock("CreateHolo.AddEntity");
			MyAPIGateway.Entities.AddEntity(holo);
			Profiler.EndProfileBlock();

			Log.DebugLog("created holo for " + seen.Entity.nameWithId() + ", holo: " + holo.nameWithId());
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
				Log.DebugLog("failed lookup of grid: " + obj.CubeGrid.DisplayName, Logger.severity.ERROR);
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
				Log.DebugLog("failed lookup of grid: " + obj.CubeGrid.DisplayName, Logger.severity.ERROR);
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
			Log.DebugLog("colouring by integriy: " + sh.Seen.Entity.getBestName());
			MyCubeGrid realGrid = (MyCubeGrid)sh.Seen.Entity;
			IMyCubeGrid holoGrid = (IMyCubeGrid)sh.Holo;
			foreach (IMySlimBlock block in realGrid.CubeBlocks)
				ColourBlock(block, holoGrid);
			sh.ColouredByIntegrity = true;
		}

		private void RestoreColour(SeenHolo sh)
		{
			Log.DebugLog("restoring original colour: " + sh.Seen.Entity.getBestName());
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
				Log.DebugLog("grid entity id lookup failed", Logger.severity.ERROR);
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
			long entityId = m_centreEntityId;
			if (entityId == 0L)
			{
				value_centreEntity = null;
			}
			else if (!MyAPIGateway.Entities.TryGetEntityById(entityId, out value_centreEntity))
			{
				Log.AlwaysLog("Failed to get entity for id: " + entityId, Logger.severity.WARNING);
				value_centreEntity = null;
			}
			Log.DebugLog("centre entity is now " + m_centreEntity.getBestName() + "(" + entityId + ")");
		}

		private bool GetOption(Option opt)
		{
			return (m_options & opt) == opt;
		}

	}
}
