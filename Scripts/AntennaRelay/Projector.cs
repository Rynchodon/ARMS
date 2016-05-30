using System;
using System.Collections.Generic;
using System.Text;
using Entities.Blocks;
using Rynchodon.Utility;
using Rynchodon.Utility.Network;
using Rynchodon.Utility.Vectors;
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
	/// TODO:
	/// special case, only display this ship, use full radius?
	/// offset adjustment
	/// ToHSV methods don't produce correct results
	public class Projector
	{

		private enum Option : byte
		{
			None = 0,
			Owner = ExtensionsRelations.Relations.Owner,
			Faction = ExtensionsRelations.Relations.Faction,
			Neutral = ExtensionsRelations.Relations.Neutral,
			Enemy = ExtensionsRelations.Relations.Enemy,
			OnOff = 16,
			Integrity = 32
		}

		private class StaticVariables
		{
			/// <summary>Maximum time since detection for entity to be displayed.</summary>
			public readonly TimeSpan displayAllowed = new TimeSpan(0, 0, 2);
			/// <summary>Maximum time since detection for entity to be kept in cache.</summary>
			public readonly TimeSpan keepInCache = new TimeSpan(0, 1, 0);

			public readonly Logger logger = new Logger("Projector");
			public readonly List<IMyTerminalControl> TermControls = new List<IMyTerminalControl>();

			// per-user options, should be saved
			public bool MouseControls;
			public Color FullIntegrity = Color.Blue, ZeroIntegrity = Color.Red;
		}

		private class SeenHolo
		{
			public LastSeen Seen;
			public MyEntity Holo;
			public bool ColouredByIntegrity;
		}

		private const double CrosshairRange = 20d;
		private const float InputScrollMulti = 1f / 120f, ScrollRangeMulti = 1.1f;
		private const float MinRangeDetection = 1e2f, MaxRangeDetection = 1e5f, DefaultRangeDetection = 1e3f;
		private const float MinRadiusHolo = 1f, MaxRadiusHolo = 10f, DefaultRadiusHolo = 2.5f;
		private const float MinSizeScale = 1, MaxSizeScale = 1e3f, DefaultSizeScale = 10f;

		private static StaticVariables Static = new StaticVariables();

		static Projector()
		{
			MyAPIGateway.Entities.OnCloseAll += Entities_OnCloseAll;
			MyTerminalControls.Static.CustomControlGetter += CustomControlGetter;

			MyAPIGateway.Session.DamageSystem.RegisterAfterDamageHandler((int)MyDamageSystemPriority.Low, AfterDamageHandler);

			MyTerminalControlFactory.AddControl(new MyTerminalControlSeparator<MySpaceProjector>());

			AddCheckbox("HoloDisplay", "Holographic Display", "Holographically display this grid and nearby detected grids", Option.OnOff);
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

			slider = new MyTerminalControlSlider<MySpaceProjector>("HD_SizeScale", MyStringId.GetOrCompute("Size Scale"), MyStringId.GetOrCompute("Larger value causes entities to apear larger"));
			slider.DefaultValue = DefaultSizeScale;
			slider.Normalizer = (block, value) => Normalizer(MinSizeScale, MaxSizeScale, block, value);
			slider.Denormalizer = (block, value) => Denormalizer(MinSizeScale, MaxSizeScale, block, value);
			slider.Writer = (block, sb) => sb.Append(GetSizeScale(block));
			valueControl = slider;
			valueControl.Getter = GetSizeScale;
			valueControl.Setter = SetSizeScale;
			Static.TermControls.Add(slider);

			Static.TermControls.Add(new MyTerminalControlSeparator<MySpaceProjector>());

			MyTerminalControlCheckbox<MySpaceProjector> control = new MyTerminalControlCheckbox<MySpaceProjector>("HD_MouseControls", MyStringId.GetOrCompute("Mouse Controls"), MyStringId.GetOrCompute("Allow manipulation of holgram with mouse"));
			IMyTerminalValueControl<bool> valueControlBool = control;
			valueControlBool.Getter = block => Static.MouseControls;
			valueControlBool.Setter = (block, value) => Static.MouseControls = value;
			Static.TermControls.Add(control);

			AddCheckbox("HD_IntegrityColour", "Colour by integrity", "Colour blocks according to their integrities", Option.Integrity);

			IMyTerminalControlColor colour = MyAPIGateway.TerminalControls.CreateControl<IMyTerminalControlColor, IMyProjector>("HD_FullIntegriyColour");
			colour.Title = MyStringId.GetOrCompute("Full Integrity Colour");
			colour.Tooltip = MyStringId.GetOrCompute("Colour when block has full integrity");
			colour.Getter = (block) => Static.FullIntegrity;
			colour.Setter = (block, value) => Static.FullIntegrity = value;
			Static.TermControls.Add(colour);

			colour = MyAPIGateway.TerminalControls.CreateControl<IMyTerminalControlColor, IMyProjector>("HD_ZeroIntegriyColour");
			colour.Title = MyStringId.GetOrCompute("Zero Integrity Colour");
			colour.Tooltip = MyStringId.GetOrCompute("Colour when block has zero integrity");
			colour.Getter = (block) => Static.ZeroIntegrity;
			colour.Setter = (block, value) => Static.ZeroIntegrity = value;
			Static.TermControls.Add(colour);
		}

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

		private static void CustomControlGetter(IMyTerminalBlock block, List<IMyTerminalControl> controls)
		{
			if (GetOptionTerminal(block, Option.OnOff))
			{
				// find show on hud
				int indexSOH = 0;
				for (; indexSOH < controls.Count && controls[indexSOH].Id != "ShowOnHUD"; indexSOH++) ;
				// remove all controls after ShowOnHUD and before separator
				controls.RemoveRange(indexSOH + 1, controls.Count - indexSOH - 3);

				for (int index = 1; index < Static.TermControls.Count; index++)
					controls.Add(Static.TermControls[index]);
			}
		}

		private static void Entities_OnCloseAll()
		{
			MyAPIGateway.Entities.OnCloseAll -= Entities_OnCloseAll;
			MyTerminalControls.Static.CustomControlGetter -= CustomControlGetter;
			Static = null;
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

			if (opt == Option.OnOff)
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

		private static void AfterDamageHandler(object obj, MyDamageInformation damageInfo)
		{
			if (!(obj is IMySlimBlock))
				return;

			IMySlimBlock block = (IMySlimBlock)obj;
			long gridId = block.CubeGrid.EntityId;
			Registrar.ForEach((Projector proj) => {
				SeenHolo sh;
				if (!proj.m_holoEntities.TryGetValue(gridId, out sh) || !sh.Holo.Render.Visible || !sh.ColouredByIntegrity)
					return;

				float integrityRatio = (block.BuildIntegrity - block.CurrentDamage) / block.MaxIntegrity;
				Color blockColour = Static.FullIntegrity * integrityRatio + Static.ZeroIntegrity * (1 - integrityRatio);
				Static.logger.debugLog("integrityRatio: " + integrityRatio + ", blockColour: " + blockColour);
				((MyCubeGrid)sh.Holo).ColorBlocks(block.Position, block.Position, blockColour.ColorToHSVDX11(), false);
			});
		}

		private readonly Logger m_logger;
		private readonly IMyCubeBlock m_block;
		private readonly NetworkClient m_netClient;

		private readonly Dictionary<long, SeenHolo> m_holoEntities = new Dictionary<long, SeenHolo>();
		/// <summary>List of entities to remove holo entirely, it will have to be re-created to be displayed again</summary>
		private readonly List<long> m_holoEntitiesRemove = new List<long>();

		private PositionBlock m_offset = new Vector3(0f, 2.5f, 0f);

		private EntityValue<Option> m_options;
		/// <summary>How close a detected entity needs to be to be placed inside the holographic area.</summary>
		private EntityValue<float> m_rangeDetection;
		/// <summary>Maximum radius of holographic area, holo entities should fit inside a sphere of this size.</summary>
		private EntityValue<float> m_radiusHolo;
		/// <summary>Size scale is distance scale * m_sizeDistScale</summary>
		private EntityValue<float> m_sizeDistScale;
		/// <summary>Id of the real entity that is centred</summary>
		private EntityValue<long> m_centreEntityId;

		private DateTime m_clearAllAt = DateTime.UtcNow + Static.keepInCache;
		/// <summary>The real entity that is centred</summary>
		private IMyEntity value_centreEntity;
		private bool m_playerCanSee;

		private bool Enabled { get { return m_block.IsWorking && (m_options.Value & Option.OnOff) != 0; } }

		private IMyEntity m_centreEntity { get { return value_centreEntity ?? m_block; } }

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
				m_holoEntities.Clear();
			}

			if (!Enabled)
				return;

			Vector3D playerPos = MyAPIGateway.Session.Player.GetPosition(), holoCentre = m_offset.ToWorld(m_block);
			m_playerCanSee = Vector3D.DistanceSquared(playerPos, holoCentre) <= 1e4d;// && !MyAPIGateway.Entities.IsRaycastBlocked(playerPos, holoCentre);

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
					if (sh.Seen.Entity is MyCubeGrid && sh.ColouredByIntegrity != ((m_options.Value & Option.Integrity) != 0))
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
					MyEntities.Remove(sh.Holo);
					MyCubeGrid grid = sh.Seen.Entity as MyCubeGrid;
					if (grid != null)
					{
						grid.OnBlockAdded -= Actual_OnBlockAdded;
						grid.OnBlockRemoved -= Actual_OnBlockRemoved;
						grid.OnBlockIntegrityChanged -= OnBlockIntegrityChanged;
					}
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

			if (MyGuiScreenTerminal.GetCurrentScreen() == MyTerminalPageEnum.None && MyAPIGateway.Session.ControlledObject.Entity is IMyCharacter &&  Static.MouseControls)
				CheckInput();

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
		}

		private void CheckInput()
		{
			MatrixD headMatrix = MyAPIGateway.Session.ControlledObject.GetHeadMatrix(true);
			RayD ray = new RayD(headMatrix.Translation, headMatrix.Forward);

			BoundingSphereD holoSphere = new BoundingSphereD(m_offset.ToWorld(m_block), m_radiusHolo.Value);
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
				m_rangeDetection.Value *= rangeMulti;
			}

			if (MyAPIGateway.Input.IsNewRightMousePressed())
			{
				m_centreEntityId.Value = 0L;
			}
			else if (MyAPIGateway.Input.IsNewLeftMousePressed())
			{
				IMyEntity firstHit = null;
				double firstHitDistance = CrosshairRange;

				foreach (SeenHolo sh in m_holoEntities.Values)
					if (sh.Holo.PositionComp.WorldAABB.Intersect(ref ray, out tmin, out tmax) && tmin < firstHitDistance)
					{
						firstHit = sh.Seen.Entity;
						firstHitDistance = tmin;
					}

				if (firstHit != null)
					m_centreEntityId.Value = firstHit.EntityId;
			}
		}

		private bool CanDisplay(LastSeen seen)
		{
			if (!CheckRelations(seen.Entity))
				return false;

			TimeSpan time = seen.GetTimeSinceLastSeen();
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
			return entity == m_block.CubeGrid || (m_options.Value & (Option)m_block.getRelationsTo(entity)) != 0;
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

			IMyCubeGrid actual = (IMyCubeGrid)seen.Entity;
			actual.OnBlockAdded += Actual_OnBlockAdded;
			actual.OnBlockRemoved += Actual_OnBlockRemoved;
		}

		private void SetupProjection(IMyEntity entity)
		{
			if (entity.Physics != null && entity.Physics.Enabled)
				entity.Physics.Enabled = false;

			entity.Render.Transparency = 0.5f;

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
			foreach (IMySlimBlock block in realGrid.CubeBlocks)
			{
				float integrityRatio = (block.BuildIntegrity - block.CurrentDamage) / block.MaxIntegrity;
				Color blockColour = Static.FullIntegrity * integrityRatio + Static.ZeroIntegrity * (1 - integrityRatio);
				((MyCubeGrid)sh.Holo).ColorBlocks(block.Position, block.Position, blockColour.ColorToHSVDX11(), false);
			}
			realGrid.OnBlockIntegrityChanged += OnBlockIntegrityChanged;
			sh.ColouredByIntegrity = true;
		}

		private void RestoreColour(SeenHolo sh)
		{
			m_logger.debugLog("restoring original colour: " + sh.Seen.Entity.getBestName());
			MyCubeGrid realGrid = (MyCubeGrid)sh.Seen.Entity;
			MyCubeGrid holo = (MyCubeGrid)sh.Holo;
			foreach (IMySlimBlock block in realGrid.CubeBlocks)
				holo.ColorBlocks(block.Position, block.Position, block.GetColorMask(), false);
			realGrid.OnBlockIntegrityChanged -= OnBlockIntegrityChanged;
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

			float integrityRatio = (block.BuildIntegrity - block.CurrentDamage) / block.MaxIntegrity;
			Color blockColour = Static.FullIntegrity * integrityRatio + Static.ZeroIntegrity * (1 - integrityRatio);
			((MyCubeGrid)sh.Holo).ColorBlocks(block.Position, block.Position, blockColour.ColorToHSVDX11(), false);
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
