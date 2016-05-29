using System;
using System.Collections.Generic;
using System.Linq;
using Entities.Blocks;
using Rynchodon.Utility;
using Rynchodon.Utility.Network;
using Rynchodon.Utility.Vectors;
using Sandbox.Game.Entities;
using Sandbox.Game.Entities.Cube;
using Sandbox.Game.Gui;
using Sandbox.ModAPI;
using Ingame = Sandbox.ModAPI.Ingame;
using Sandbox.ModAPI.Interfaces.Terminal;
using VRage.Game;
using VRage.Game.Entity;
using VRage.Game.ModAPI;
using VRage.ModAPI;
using VRage.Utils;
using VRageMath;
using Sandbox.Definitions;
using VRage.Game.Components;

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
			OnOff = 1 << 0,
			ThisGrid = 1 << 1,
			Owner = 1 << 2,
			Faction = 1 << 3,
			Neutral = 1 << 4,
			Enemy = 1 << 5,
		}

		private class StaticVariables
		{
			public Logger logger = new Logger("Projector");
			public List<MyTerminalControl<MySpaceProjector>> checkboxes = new List<MyTerminalControl<MySpaceProjector>>();
			public Option[] OptionArray = Enum.GetValues(typeof(Option)).Cast<Option>().ToArray();
		}

		private struct SeenHolo
		{
			public LastSeen Seen;
			public MyEntity Holo;
		}

		private static StaticVariables Static = new StaticVariables();

		static Projector()
		{
			MyAPIGateway.Entities.OnCloseAll += Entities_OnCloseAll;
			MyTerminalControls.Static.CustomControlGetter += CustomControlGetter;

			MyTerminalControlFactory.AddControl(new MyTerminalControlSeparator<MySpaceProjector>());

			AddCheckbox("HoloDisplay", "Holographic Display", "Holographically display this grid and nearby detected grids", Option.OnOff);
			AddCheckbox("HD_ThisShip", "This Ship", "Holographically display this ship", Option.ThisGrid);
			AddCheckbox("HD_Owner", "Owned Ships", "Holographically display ships owned by this block's owner", Option.Owner);
			AddCheckbox("HD_Faction", "Faction Ships", "Holographically display faction owned ships", Option.Faction);
			AddCheckbox("HD_Neutral", "Netural Ships", "Holographically display neutral ships", Option.Neutral);
			AddCheckbox("HD_Enemy", "Enemy Ships", "Holographically display enemy ships", Option.Enemy);
		}

		private static void AddCheckbox(string id, string title, string toolTip, Option opt)
		{
			MyTerminalControlCheckbox<MySpaceProjector> control = new MyTerminalControlCheckbox<MySpaceProjector>(id, MyStringId.GetOrCompute(title), MyStringId.GetOrCompute(toolTip));
			IMyTerminalValueControl<bool> valueControl = control as IMyTerminalValueControl<bool>;
			valueControl.Getter = block => GetOptionTerminal(block, opt);
			valueControl.Setter = (block, value) => SetOptionTerminal(block, opt, value);
			if (Static.checkboxes.Count == 0)
				MyTerminalControlFactory.AddControl(control);
			Static.checkboxes.Add(control);
		}

		private static void CustomControlGetter(IMyTerminalBlock block, List<IMyTerminalControl> controls)
		{
			if (GetOptionTerminal(block, Option.OnOff))
				for (int index = 1; index < Static.checkboxes.Count; index++)
					controls.Add(Static.checkboxes[index]);
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

		private static void UpdateVisual()
		{
			foreach (var control in Static.checkboxes)
				control.UpdateVisual();
		}

		private readonly Logger m_logger;
		private readonly IMyCubeBlock m_block;
		private readonly NetworkClient m_netClient;

		private readonly Dictionary<long, SeenHolo> m_holoEntities = new Dictionary<long, SeenHolo>();

		private PositionBlock m_offset = new Vector3(0f, 2.5f, 0f);
		private float m_distanceScale = 0.05f;
		private float m_sizeScale = 0.25f;

		private EntityValue<Option> m_options;

		public Projector(IMyCubeBlock block)
		{
			this.m_logger = new Logger(GetType().Name, block);
			this.m_block = block;
			this.m_options = new EntityValue<Option>(block, 0, UpdateVisual);
			this.m_netClient = new NetworkClient(block);
			Registrar.Add(block, this);
		}

		/// <summary>
		/// Check opts, load/unload
		/// </summary>
		public void Update100()
		{
			if ((m_options.Value & Option.OnOff) == 0)
			{
				if (m_holoEntities.Count != 0)
				{
					m_logger.debugLog("removing all");
					foreach (SeenHolo sh in m_holoEntities.Values)
						MyEntities.Remove(sh.Holo);
					m_holoEntities.Clear();
				}
				return;
			}

			NetworkStorage storage = m_netClient.GetStorage();
			if (storage == null)
				return;

			m_logger.debugLog("last seen count: " + storage.LastSeenCount);
			storage.ForEachLastSeen(CreateHolo);
		}

		/// <summary>
		/// Updates positions & orientations
		/// </summary>
		public void Update1()
		{
			PositionWorld projectionCentre = m_offset.ToWorld(m_block);

			foreach (SeenHolo sh in m_holoEntities.Values)
			{
				MatrixD worldMatrix = sh.Seen.Entity.WorldMatrix;
				worldMatrix.Translation = projectionCentre + (sh.Seen.Entity.GetCentre() - m_block.CubeGrid.GetCentre()) * m_distanceScale;
				sh.Holo.PositionComp.SetWorldMatrix(worldMatrix);
				sh.Holo.PositionComp.Scale = m_sizeScale;
			}
		}

		private void CreateHolo(LastSeen seen)
		{
			if (!(seen.Entity is IMyCubeGrid))
				return;

			if (m_holoEntities.ContainsKey(seen.Entity.EntityId))
			{
				m_logger.debugLog("already has a holo: " + seen.Entity.getBestName() + "(" + seen.Entity.EntityId + ")");
				return;
			}

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
		}

		private void SetupProjection(MyEntity entity)
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
				SetupProjection(child.Container.Entity as MyEntity);
		}

	}
}
