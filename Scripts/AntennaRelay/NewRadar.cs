#if DEBUG
#define TRACE
#endif

using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Xml.Serialization;
using Rynchodon.Utility;
using Rynchodon.Utility.Network;
using Sandbox;
using Sandbox.Common.ObjectBuilders;
using Sandbox.Definitions;
using Sandbox.Game.Entities;
using Sandbox.Game.Entities.Blocks;
using Sandbox.Game.Entities.Cube;
using Sandbox.Game.Gui;
using Sandbox.Game.Lights;
using Sandbox.Game.Weapons;
using Sandbox.Game.World;
using Sandbox.ModAPI;
using VRage.Game.Entity;
using VRage.Game.ModAPI;
using VRage.ModAPI;
using VRage.ObjectBuilders;
using VRage.Utils;
using VRageMath;

namespace Rynchodon.AntennaRelay
{
	/// <remarks>
	/// <para>Now with real radar equation!</para>
	/// <para>pattern propogation factor = 1</para>
	/// <para>wavelength = 1</para>
	/// <para>min signal strength = 1</para>
	/// </remarks>
	/// TODO:
	/// guided missile reflectivity => RCS
	/// when finished, replace assert is empty with clear
	public sealed class NewRadar : IComparable<NewRadar>
	{
		public sealed class Definition
		{
			/// <summary>Definition for a decoy block.</summary>
			public static readonly Definition Decoy;

			private static readonly Dictionary<SerializableDefinitionId, Definition> _knownDefinitions = new Dictionary<SerializableDefinitionId, Definition>();
			private static readonly List<SerializableDefinitionId> _notRadarEquip = new List<SerializableDefinitionId>();

			static Definition()
			{
				Decoy = new Definition();
				Decoy.MaxTransmitterPower = 25000f; // RTG has power of about 50 W, though this clashes with higher power usages in game
				Decoy.AntennaGainDecibels = 40f;
				Decoy.Init();
			}

			[OnWorldClose]
			private static void OnUnload()
			{
				_knownDefinitions.Clear();
				_notRadarEquip.Clear();
			}

			public static Definition GetFor(IMyCubeBlock block)
			{
				Definition result;
				if (_knownDefinitions.TryGetValue(block.BlockDefinition, out result))
					return result;

				if (_notRadarEquip.Contains(block.BlockDefinition))
					return null;

				MyCubeBlockDefinition defn = block.GetCubeBlockDefinition();
				if (defn == null)
					throw new NullReferenceException("defn");

				if (string.IsNullOrWhiteSpace(defn.DescriptionString))
				{
					_notRadarEquip.Add(block.BlockDefinition);
					return null;
				}

				XML_Amendments<Definition> ammend = new XML_Amendments<Definition>(new Definition());
				ammend.AmendAll(defn.DescriptionString, true);
				result = ammend.Deserialize();

				if (result.IsRadar || result.IsJammer || result.PassiveRadarDetection || result.PassiveJammerDetection)
				{
					Logger.DebugLog("Created definition for " + block.DefinitionDisplayNameText, Logger.severity.DEBUG);
					_knownDefinitions.Add(block.BlockDefinition, result);
					result.Init();
					return result;
				}
				else
				{
					Logger.DebugLog("Nothing in description for " + block.DefinitionDisplayNameText, Logger.severity.DEBUG);
					_notRadarEquip.Add(block.BlockDefinition);
					return null;
				}
			}

			public bool IsRadar, IsJammer;
			public bool PassiveRadarDetection, PassiveJammerDetection;

			/// <summary>Maximum power of transmitter.</summary>
			public float MaxTransmitterPower;

			/// <summary>Antenna gain in decibels.</summary>
			public float AntennaGainDecibels;

			/// <summary>Antenna gain factor, not decibels.</summary>
			public float AntennaGainFactor;

			/// <summary>How much stronger the signal needs to be than jam noise.</summary>
			public float SignalToJamRatio;

			/// <summary>antenna gain / (4 Pi). Calaculated by <see cref="Init"/>.</summary>
			[XmlIgnore]
			public float AntennaConstant { get; private set; }

			#region Angle Properties

			/// <summary>In radians.</summary>
			public float MinAzimuth
			{
				get { return value_MinAzimuth; }
				set
				{
					value_MinAzimuth = value;
					EnforceAngle = true;
				}
			}

			/// <summary>In radians.</summary>
			public float MaxAzimuth
			{
				get { return value_MaxAzimuth; }
				set
				{
					value_MaxAzimuth = value;
					EnforceAngle = true;
				}
			}

			/// <summary>In radians.</summary>
			public float MinElevation
			{
				get { return value_MinElevation; }
				set
				{
					value_MinElevation = value;
					EnforceAngle = true;
				}
			}

			/// <summary>In radians.</summary>
			public float MaxElevation
			{
				get { return value_MaxElevation; }
				set
				{
					value_MaxElevation = value;
					EnforceAngle = true;
				}
			}

			/// <summary>If true, angles need to be checked.</summary>
			[XmlIgnore]
			public bool EnforceAngle { get; private set; }

			#endregion

			private float value_MinAzimuth = -MathHelper.Pi, value_MaxAzimuth = MathHelper.Pi, value_MinElevation = -MathHelper.Pi, value_MaxElevation = MathHelper.Pi;

			private void Init()
			{
				if (AntennaGainFactor == 0f)
					AntennaGainFactor = (float)Math.Pow(10d, AntennaGainDecibels / 10d);
				AntennaConstant = AntennaGainFactor / MathHelper.FourPi;
			}
		}

		private struct Detected
		{
			/// <summary>Minimum signal strength for radar to be located from its noise.</summary>
			private const float _signalRadarNoise = 1e5f;
			/// <summary>Minimum signal strength for jammer to be located from its noise.</summary>
			private const float _signalJammerNoise = 1e6f;

			public bool ByRadarSensorOrCamera;
			public float RadarNoise, JammerNoise;

			public bool IsRadarQuiet
			{
				get { return RadarNoise < _signalRadarNoise; }
			}

			public bool IsRadarLoud
			{
				get { return RadarNoise >= _signalRadarNoise; }
			}

			public bool IsJammerQuiet
			{
				get { return JammerNoise < _signalJammerNoise; }
			}

			public bool IsJammerLoud
			{
				get { return JammerNoise >= _signalJammerNoise; }
			}
		}

		private static readonly int _blockLimit;
		/// <summary>RCS for largest possible ship.</summary>
		private static readonly float _maxRCS;

		private static readonly Threading.ThreadManager _thread = new Threading.ThreadManager(threadName: typeof(NewRadar).Name);

		private static readonly FieldInfo MyBeacon__m_light = typeof(MyBeacon).GetField("m_light", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

		/// <summary>Primary storage node and all the equipment with the same storage.</summary>
		private static readonly Dictionary<IRelayPart, List<NewRadar>> _equipByPrimaryNode = new Dictionary<IRelayPart, List<NewRadar>>();
		/// <summary>Group of equipment that is linked through relay currently being processed.</summary>
		private static List<NewRadar> _linkedEquipment = new List<NewRadar>();
		/// <summary>All the entities that are part of the current relay network.</summary>
		private static readonly HashSet<IMyEntity> _linkedTopEntity = new HashSet<IMyEntity>();
		private static readonly List<MyCubeGrid> _gridLogicalGroupNodes = new List<MyCubeGrid>();
		/// <summary>Entities that are near any equipment of the current relay network.</summary>
		private static readonly List<MyEntity> _nearbyEntities = new List<MyEntity>();
		/// <summary>Radar equipment that are near any equipment of the the current relay network.</summary>
		private static readonly List<NewRadar> _nearbyEquipment = new List<NewRadar>();
		/// <summary>Detected entities and how they were detected.</summary>
		private static readonly Dictionary<IMyEntity, Detected> _detected = new Dictionary<IMyEntity, Detected>();
		/// <summary>Ignore set for ray cast.</summary>
		private static ICollection<IMyEntity> _obstructedIgnore = new List<IMyEntity>();

		private static readonly MyTerminalControlSlider<MyFunctionalBlock> _radarSlider, _jammerSlider;

		static NewRadar()
		{
			_blockLimit = MySession.Static.EnableBlockLimits ? MySession.Static.MaxGridSize : 50000;
			_maxRCS = SphereCrossSection(_blockLimit * 2.5f * 2.5f * 2.5f);
			if (MyBeacon__m_light == null)
				throw new NullReferenceException("MyBeacon__m_light");

			TerminalControlHelper.EnsureTerminalControlCreated<MyBeacon>();
			TerminalControlHelper.EnsureTerminalControlCreated<MyRadioAntenna>();

			{
				_radarSlider = new MyTerminalControlSlider<MyFunctionalBlock>("RadarPowerLevel", MyStringId.GetOrCompute("Radar Power"), MyStringId.GetOrCompute("Power of radar transmissions"));
				new ValueSync<float, NewRadar>(_radarSlider, GetRadarValue, SetRadarValue, false);
				_radarSlider.SetEnabledAndVisible(IsRadarBlock);
				_radarSlider.DefaultValueGetter = DefaultRadarValue;
				_radarSlider.Normalizer = Normalizer;
				_radarSlider.Denormalizer = Denormalizer;
				_radarSlider.Writer = (block, sb) => WriterWatts(_radarSlider.GetValue(block), sb);

				MyTerminalControlFactory.AddControl<MyFunctionalBlock, MyBeacon>(_radarSlider);
				MyTerminalControlFactory.AddControl<MyFunctionalBlock, MyRadioAntenna>(_radarSlider);
			}

			{
				_jammerSlider = new MyTerminalControlSlider<MyFunctionalBlock>("JammerPowerLevel", MyStringId.GetOrCompute("Jammer Power"), MyStringId.GetOrCompute("Power of jammer transmissions"));
				new ValueSync<float, NewRadar>(_jammerSlider, GetJammerValue, SetJammerValue, false);
				_jammerSlider.SetEnabledAndVisible(IsJammerBlock);
				_jammerSlider.DefaultValueGetter = DefaultJammerValue;
				_jammerSlider.Normalizer = Normalizer;
				_jammerSlider.Denormalizer = Denormalizer;
				_jammerSlider.Writer = (block, sb) => WriterWatts(_jammerSlider.GetValue(block), sb);

				MyTerminalControlFactory.AddControl<MyFunctionalBlock, MyBeacon>(_jammerSlider);
				MyTerminalControlFactory.AddControl<MyFunctionalBlock, MyRadioAntenna>(_jammerSlider);
			}
		}

		#region Terminal Functions

		private static bool IsRadarBlock(IMyCubeBlock block)
		{
			NewRadar equip;
			if (!Registrar.TryGetValue(block, out equip))
			{
				Logger.TraceLog("No equipment: " + block.nameWithId());
				return false;
			}

			//Logger.TraceLog(block.nameWithId() + " is radar: " + equip._definition.IsRadar);
			return equip._definition.IsRadar;
		}

		private static bool IsJammerBlock(IMyCubeBlock block)
		{
			NewRadar equip;
			if (!Registrar.TryGetValue(block, out equip))
			{
				Logger.TraceLog("No equipment: " + block.nameWithId());
				return false;
			}

			//Logger.TraceLog(block.nameWithId() + " is jammer: " + equip._definition.IsJammer);
			return equip._definition.IsJammer;
		}

		private static float GetRadarValue(NewRadar equip)
		{
			return equip._targetRadarTransmitPower;
		}

		/// <summary>
		/// Does not update visual for radar or sync.
		/// </summary>
		private static void SetRadarValue(NewRadar equip, float value)
		{
			if (value > equip._definition.MaxTransmitterPower)
				value = equip._definition.MaxTransmitterPower;

			if (equip._targetRadarTransmitPower == value)
				return;

			equip.Log.TraceLog("change radar target from " + equip._targetRadarTransmitPower + " to " + value);
			equip._targetRadarTransmitPower = value;

			if (equip._definition.IsJammer)
			{
				float maxJam = equip._definition.MaxTransmitterPower - value;
				if (equip._targetJammerTransmitPower > maxJam)
				{
					equip._targetJammerTransmitPower = maxJam;
					_jammerSlider.UpdateVisual();
				}
			}

			equip.UpdateElectricityConsumption();
		}

		private static float GetJammerValue(NewRadar equip)
		{
			return equip._targetJammerTransmitPower;
		}

		/// <summary>
		/// Does not update visual for jammer or sync.
		/// </summary>
		private static void SetJammerValue(NewRadar equip, float value)
		{
			if (value > equip._definition.MaxTransmitterPower)
				value = equip._definition.MaxTransmitterPower;

			if (equip._targetJammerTransmitPower == value)
				return;

			equip.Log.TraceLog("change jammer target from " + equip._targetJammerTransmitPower + " to " + value);
			equip._targetJammerTransmitPower = value;

			if (equip._definition.IsRadar)
			{
				float maxRadar = equip._definition.MaxTransmitterPower - value;
				if (equip._targetRadarTransmitPower > maxRadar)
				{
					equip._targetRadarTransmitPower = maxRadar;
					_radarSlider.UpdateVisual();
				}
			}
			
			equip.UpdateElectricityConsumption();
		}

		private static float DefaultRadarValue(IMyCubeBlock block)
		{
			NewRadar equip;
			if (!Registrar.TryGetValue(block, out equip))
			{
				Logger.TraceLog("No equipment: " + block.nameWithId());
				return default(float);
			}

			Debug.Assert(equip._definition.IsRadar, "Not radar");

			if (equip._definition.IsJammer)
				return 0.5f * equip._definition.MaxTransmitterPower;
			return equip._definition.MaxTransmitterPower;
		}

		private static float DefaultJammerValue(IMyCubeBlock block)
		{
			NewRadar equip;
			if (!Registrar.TryGetValue(block, out equip))
			{
				Logger.TraceLog("No equipment: " + block.nameWithId());
				return default(float);
			}

			Debug.Assert(equip._definition.IsJammer, "Not jammer");

			if (equip._definition.IsRadar)
				return 0.5f * equip._definition.MaxTransmitterPower;
			return equip._definition.MaxTransmitterPower;
		}

		private static float Normalizer(IMyCubeBlock block, float val)
		{
			NewRadar equip;
			if (!Registrar.TryGetValue(block, out equip))
			{
				Logger.TraceLog("No equipment: " + block.nameWithId());
				return default(float);
			}

			return val / equip._definition.MaxTransmitterPower;
		}

		private static float Denormalizer(IMyCubeBlock block, float val)
		{
			NewRadar equip;
			if (!Registrar.TryGetValue(block, out equip))
			{
				Logger.TraceLog("No equipment: " + block.nameWithId());
				return default(float);
			}

			return val * equip._definition.MaxTransmitterPower;
		}

		private static void WriterWatts(float value, StringBuilder sb)
		{
			sb.Append(PrettySI.makePretty(value));
			sb.Append("W");
		}

		#endregion

		public static bool IsDefinedRadarEquipment(IMyCubeBlock block)
		{
			return Definition.GetFor(block) != null;
		}

		/// <summary>
		/// Get the cross section area of a sphere with a given volume.
		/// </summary>
		private static float SphereCrossSection(float volume)
		{
			const double value1 = 3d / (4d * Math.PI);
			const double value2 = 2d / 3d;
			return (float)Math.Pow(value1 * volume, value2) * MathHelper.Pi;
		}

		/// <summary>
		/// Volume of entity, including decoy blocks, if present.
		/// </summary>
		/// <param name="entity">Entity to get the volume of.</param>
		/// <returns>The volume of an entity.</returns>
		private static float Volume(IMyEntity entity)
		{
			Debug.Assert(IsValidRadarTarget(entity), "Not a valid target: " + entity.nameWithId());

			const int decoyFakeBlocks = 1000;

			if (entity is IMyCubeGrid)
			{
				IMyCubeGrid grid = (IMyCubeGrid)entity;
				CubeGridCache cache = CubeGridCache.GetFor(grid);
				if (cache != null)
					return (cache.CellCount + decoyFakeBlocks * cache.CountByType(typeof(MyObjectBuilder_Decoy))) * grid.GridSize * grid.GridSize * grid.GridSize;
			}
			return entity.LocalAABB.Volume();
		}

		/// <summary>
		/// Estimate the cross section of the entity by pretending it is a sphere.
		/// </summary>
		private static float RadarCrossSection(IMyEntity entity)
		{
			return SphereCrossSection(Volume(entity));
		}

		private static IMyEntity GridIfBlock(IMyEntity entity)
		{
			return entity is IMyCubeBlock ? ((IMyCubeBlock)entity).CubeGrid : entity;
		}

		/// <summary>
		/// Check that a top-most entity is a valid target for detection by radar.
		/// </summary>
		private static bool IsValidRadarTarget(IMyEntity entity)
		{
			Debug.Assert(!(entity is IMyCubeBlock), "block not expected: " + entity.nameWithId());
			return entity is IMyCubeGrid || entity is IMyCharacter || entity is MyAmmoBase;
		}

		/// <summary>
		/// Helper for <see cref="RayCast.Obstructed{Tignore}(LineD, IEnumerable{Tignore}, bool, bool)"/> that builds ignore set.
		/// </summary>
		/// <param name="line">Line to ray cast.</param>
		/// <param name="from">Originating entity. Should never be a grid.</param>
		/// <param name="to">Destination entity. If it is a grid, physically attached grids will be ignored.</param>
		/// <returns>True if ray cast failed; the target is obstructed.</returns>
		private static bool RayCastObstructed(ref LineD line, IMyEntity from, IMyEntity to)
		{
			Debug.Assert(!(from is IMyCubeGrid), "from should not be a grid");

			_obstructedIgnore.Clear();
			_obstructedIgnore.Add(from);
			if (to is MyCubeGrid)
				foreach (var grid in Attached.AttachedGrid.AttachedGrids((IMyCubeGrid)to, Attached.AttachedGrid.AttachmentKind.Physics, true))
					_obstructedIgnore.Add(grid);
			else
				_obstructedIgnore.Add(to);

			// don't use _nearbyEntities, entities in ray is a much shorter list
			bool result = RayCast.Obstructed(line, _obstructedIgnore, true, true);

			if (_obstructedIgnore is IList && _obstructedIgnore.Count >= 20)
				_obstructedIgnore = new HashSet<IMyEntity>();
			else
				_obstructedIgnore.Clear();
			return result;
		}

		#region Update

		public static void UpdateAll()
		{
			_thread.EnqueueAction(UpdateOnThread);
		}

		private static void UpdateOnThread()
		{
			Logger.TraceLog("entered member");
			Debug.Assert(_equipByPrimaryNode.Count == 0, "_equipByPrimaryNode is not empty");

			// collect radar equip and sort by primary node
			foreach (NewRadar equip in Registrar.Scripts<NewRadar>())
			{
				IRelayPart rp = equip._relayPart;
				if (rp == null && !equip.TryGetRelayNode(out rp))
				{
					Logger.TraceLog("Has no relay node: " + equip.DebugName);
					continue;
				}

				RelayStorage store = rp.GetStorage();
				if (store == null)
				{
					Logger.TraceLog("Has no relay storage: " + equip.DebugName);
					continue;
				}

				List<NewRadar> list;
				if (!_equipByPrimaryNode.TryGetValue(store.PrimaryNode, out list))
				{
					ResourcePool.Get(out list);
					_equipByPrimaryNode.Add(store.PrimaryNode, list);
				}
				list.Add(equip);
				Logger.TraceLog("added " + equip.DebugName + " to group with primary node: " + store.PrimaryNode.DebugName + ", count: " + list.Count);
			}

			Logger.TraceLog("finished collecting, updating each");

			foreach (var pair in _equipByPrimaryNode)
			{
				if (pair.Value.Count != 0)
				{
					_linkedEquipment = pair.Value;
					_linkedEquipment.Sort();
					Update(pair.Key);
					_linkedEquipment.Clear();
				}
				ResourcePool.Return(_linkedEquipment);
			}

			_equipByPrimaryNode.Clear();
		}

		private static void Update(IRelayPart primaryNode)
		{
			Debug.Assert(_nearbyEntities.Count == 0, "_nearbyEntities not cleared");
			Debug.Assert(_nearbyEquipment.Count == 0, "_nearbyEquipment not cleared");
			Debug.Assert(_detected.Count == 0, "_detected not cleared");
			Debug.Assert(_linkedTopEntity.Count == 0, "_linkedTopEntity not cleared");
			Debug.Assert(primaryNode is RelayNode, "primaryNode is not a relay node");

			Logger.TraceLog("Updating, primary node: " + primaryNode.DebugName + ", count: " + _linkedEquipment.Count);

			BoundingSphereD locale;
			locale.Radius = 50000d;

			foreach (NewRadar radar in _linkedEquipment)
			{
				radar.Update();
				if (radar.IsWorking && radar.CanLocate)
				{
					Logger.TraceLog("Collecting entities near " + radar.DebugName);
					locale.Center = radar._entity.GetCentre();
					MyGamePruningStructure.GetAllTopMostEntitiesInSphere(ref locale, _nearbyEntities);
				}
			}

			foreach (RelayNode node in Registrar.Scripts<RelayNode>())
				if (node.Storage?.PrimaryNode == primaryNode)
					if (node.Entity is IMyCubeBlock)
					{
						IMyCubeGrid grid = node.Block.CubeGrid;
						if (_linkedTopEntity.Add(grid))
						{
							Logger.TraceLog("Adding grids logically connected to " + grid.nameWithId() + " to top entities");
							Debug.Assert(_gridLogicalGroupNodes.Count == 0, "_gridLogicalGroupNodes not cleared");
							foreach (var aGrid in Attached.AttachedGrid.AttachedGrids(grid, Attached.AttachedGrid.AttachmentKind.Terminal, true))
								_linkedTopEntity.Add(aGrid);
							_gridLogicalGroupNodes.Clear();
						}
					}
					else
					{
						Logger.TraceLog("Adding " + node.Entity.nameWithId() + " to top entities");
						_linkedTopEntity.Add(node.Entity);
					}

			// all jamming must be applied before any detection
			CollectEquipmentAndJam(primaryNode.OwnerId);
			RadarDetection();
			PassiveDetection();
			DecoyDetection();
			SensorAndCameraBlocks();
			CreateLastSeen(primaryNode);

			_nearbyEntities.Clear();
			_nearbyEquipment.Clear();
			_detected.Clear();
			_linkedTopEntity.Clear();
		}

		/// <summary>
		/// Collect equipment from entities in <see cref="_nearbyEntities"/> and apply any jamming effect.
		/// </summary>
		/// <param name="ownerId">Owner of <see cref="_linkedEquipment"/>.</param>
		private static void CollectEquipmentAndJam(long ownerId)
		{
			foreach (MyEntity entity in _nearbyEntities)
				if (entity is IMyCubeGrid)
				{
					CubeGridCache cache = CubeGridCache.GetFor((IMyCubeGrid)entity);
					if (cache == null)
						continue;
					foreach (IMyEntity beacon in cache.BlocksOfType(typeof(MyObjectBuilder_Beacon)))
						CollectEquipmentAndJam(ownerId, beacon);
					foreach (IMyEntity antennna in cache.BlocksOfType(typeof(MyObjectBuilder_RadioAntenna)))
						CollectEquipmentAndJam(ownerId, antennna);
				}
				else if (IsValidRadarTarget(entity))
					CollectEquipmentAndJam(ownerId, entity);
		}

		/// <summary>
		/// If entity has equipment, add it to <see cref="_nearbyEquipment"/>.
		/// If equipment is hostile, apply jamming to equipment in <see cref="_linkedEquipment"/>.
		/// </summary>
		/// <param name="ownerId">Owner of <see cref="_linkedEquipment"/>.</param>
		/// <param name="entity">The entity that might have equipment.</param>
		private static void CollectEquipmentAndJam(long ownerId, IMyEntity entity)
		{
			Debug.Assert(!(entity is IMyCubeGrid), "grid not expected");

			NewRadar equip;
			if (!Registrar.TryGetValue(entity, out equip) || !equip.IsWorking || _linkedEquipment.BinarySearch(equip) >= 0)
				return;

			Logger.DebugLog("nearby equipment: " + equip.DebugName + ", hostile: " + ExtensionsRelations.canConsiderHostile(ownerId, entity, false) + ", jammer: " + equip.IsJammerOn);
			_nearbyEquipment.Add(equip);

			if (ExtensionsRelations.canConsiderHostile(ownerId, entity, false) && equip.IsJammerOn)
			{
				Vector3D position = entity.GetCentre();
				foreach (NewRadar linked in _linkedEquipment)
					if (linked.IsWorking && linked.CanLocate)
						linked.ApplyJamming(ref position, equip);
			}
		}

		/// <summary>
		/// Foreach entity in <see cref="_nearbyEntities"/>, attempt to actively detect the entity with radar in <see cref="_linkedEquipment"/>.
		/// </summary>
		private static void RadarDetection()
		{
			const float minSignal = 1f;

			foreach (IMyEntity entity in _nearbyEntities)
				if (IsValidRadarTarget(entity) && !_linkedTopEntity.Contains(entity))
				{
					Logger.TraceLog("trying to detect " + entity.nameWithId());
					Vector3D position = entity.GetCentre();
					float RCS = RadarCrossSection(entity);
					float signal = 0f;
					foreach (NewRadar linked in _linkedEquipment)
					{
						if (!linked.IsWorking || !linked.IsRadarOn)
							continue;
						signal += linked.RadarSignal(ref position, entity, RCS);
						Logger.TraceLog(linked.DebugName + " increased total signal to " + signal);
						if (signal > minSignal)
						{
							Logger.TraceLog("entity located");
							Detected detect = new Detected();
							detect.ByRadarSensorOrCamera = true;
							PushDetected(entity, ref detect);
							break;
						}
					}
				}
		}

		/// <summary>
		/// Foreach <see cref="NewRadar"/> in <see cref="_nearbyEquipment"/>, attempt to passively detect the equipment with radar in <see cref="_linkedEquipment"/>.
		/// </summary>
		private static void PassiveDetection()
		{
			foreach (NewRadar equip in _nearbyEquipment)
			{
				if (!equip.IsWorking || !equip.CanBePassivelyDetected)
					continue;

				IMyEntity topEntity = GridIfBlock(equip._entity);
				if (_linkedTopEntity.Contains(topEntity))
					continue;

				Detected detect;
				_detected.TryGetValue(topEntity, out detect);

				if (detect.IsRadarLoud && detect.IsJammerLoud)
					continue;

				Vector3D position = equip._entity.GetCentre();
				Logger.TraceLog("Trying to detect " + equip.DebugName);

				foreach (NewRadar linked in _linkedEquipment)
				{
					if (!linked.IsWorking)
						continue;

					bool radar =  detect.IsRadarQuiet && equip.IsRadarOn && linked._definition.PassiveRadarDetection;
					bool jammer = detect.IsJammerQuiet && equip.IsJammerOn && linked._definition.PassiveJammerDetection;

					if (radar || jammer)
					{
						LineD line = new LineD(linked._entity.GetCentre(), position);
						if (!linked.SignalCanReach(ref line, equip._entity))
							continue;

						if (radar)
						{
							detect.RadarNoise += linked.PassiveSignalFromRadar(ref line, equip);
							Logger.TraceLog(linked.DebugName + " increased total radar noise to " + detect.RadarNoise);
						}
						if (jammer)
						{
							detect.JammerNoise += linked.PassiveSignalFromJammer(ref line, equip);
							Logger.TraceLog(linked.DebugName + " increased total jammer noise to " + detect.JammerNoise);
						}

						PushDetected(GridIfBlock(equip._entity), ref detect);

						if (detect.IsRadarLoud && detect.IsJammerLoud)
						{
							Logger.TraceLog("Detected both radar and jammer, early exit");
							break;
						}
					}
				}
			}
		}

		private static void DecoyDetection()
		{
			foreach (IMyEntity entity in _nearbyEntities)
				if (entity is IMyCubeGrid && !_linkedTopEntity.Contains(entity))
				{
					CubeGridCache cache = CubeGridCache.GetFor((IMyCubeGrid)entity);
					if (cache == null)
						continue;

					int count = cache.CountByType(typeof(MyObjectBuilder_Decoy));
					if (count == 0)
						continue;

					Detected detect;
					_detected.TryGetValue(entity, out detect);

					if (detect.IsRadarLoud && detect.IsJammerLoud)
						continue;

					Logger.TraceLog("Trying to detect decoys from " + entity.nameWithId());

					// use average position of decoys
					Vector3D position = Vector3D.Zero;
					foreach (IMyDecoy decoy in cache.BlocksOfType(typeof(MyObjectBuilder_Decoy)))
						position += decoy.GetPosition();
					position /= count;

					foreach (NewRadar linked in _linkedEquipment)
						if (linked.IsWorking && (detect.IsRadarQuiet && linked._definition.PassiveRadarDetection || detect.IsJammerQuiet && linked._definition.PassiveJammerDetection))
						{
							float signal = linked.PassiveSignalFromDecoy(ref position, entity);
							detect.RadarNoise += signal;
							detect.JammerNoise += signal;
							Logger.TraceLog(linked.DebugName + " increased signal strength to " + detect.RadarNoise + '/' + detect.JammerNoise);
							if (detect.IsRadarLoud && detect.IsJammerLoud)
							{
								Logger.TraceLog("Deception is total, early exit");
								break;
							}
						}

					PushDetected(entity, ref detect);
				}
		}

		/// <summary>
		/// Add entities detected by sensors and cameras. _nearbyEntities is reused by this method.
		/// </summary>
		private static void SensorAndCameraBlocks()
		{
			foreach (IMyEntity linked in _linkedTopEntity)
				if (linked is IMyCubeGrid)
				{
					Logger.TraceLog("grid: " + linked.nameWithId());

					CubeGridCache cache = CubeGridCache.GetFor((IMyCubeGrid)linked);
					if (cache == null)
						continue;

					CheckSensor(cache);
					CheckCamera(cache);
				}
		}

		private static void CheckSensor(CubeGridCache cache)
		{
			foreach (MySensorBlock sensor in cache.BlocksOfType(typeof(MyObjectBuilder_SensorBlock)))
			{
				Logger.TraceLog("sensor: " + sensor.nameWithId());
				IMyEntity lastDetected = sensor.LastDetectedEntity;
				if (lastDetected != null && IsValidRadarTarget(lastDetected))
				{
					Logger.TraceLog("located: " + lastDetected.nameWithId());
					Detected detect;
					_detected.TryGetValue(lastDetected, out detect);
					if (detect.ByRadarSensorOrCamera)
						continue;

					detect.ByRadarSensorOrCamera = true;
					PushDetected(lastDetected, ref detect);
				}
			}
		}

		/// <summary>
		/// Add entities detected by cameras. _nearbyEntities is reused by this method.
		/// </summary>
		private static void CheckCamera(CubeGridCache cache)
		{
			BoundingSphereD nearby;
			nearby.Radius = 1000d;

			foreach (MyCameraBlock camera in cache.BlocksOfType(typeof(MyObjectBuilder_CameraBlock)))
			{
				Logger.TraceLog("camera: " + camera.nameWithId());
				nearby.Center = camera.PositionComp.GetPosition();
				_nearbyEntities.Clear();
				MyGamePruningStructure.GetAllTopMostEntitiesInSphere(ref nearby, _nearbyEntities);

				foreach (IMyEntity nearbyEntity in _nearbyEntities)
					if (IsValidRadarTarget(nearbyEntity) && !_linkedTopEntity.Contains(nearbyEntity))
					{
						Detected detect;
						_detected.TryGetValue(nearbyEntity, out detect);
						if (detect.ByRadarSensorOrCamera)
							continue;

						LineD line = new LineD(nearby.Center, nearbyEntity.GetCentre());
						if (!camera.CheckAngleLimits(line.Direction))
						{
							Logger.TraceLog("Outside angle limits: " + nearbyEntity.nameWithId());
							continue;
						}
						if (RayCastObstructed(ref line, camera, nearbyEntity))
						{
							Logger.TraceLog("Ray cast obstructed: " + nearbyEntity.nameWithId());
							continue;
						}

						Logger.TraceLog("located: " + nearbyEntity.nameWithId());
						detect.ByRadarSensorOrCamera = true;
						PushDetected(nearbyEntity, ref detect);
					}
			}
		}

		private static void PushDetected(IMyEntity entity, ref Detected detect)
		{
			Debug.Assert(!(entity is IMyCubeBlock), entity.nameWithId() + " is block, grid should be passed");
			_detected[entity] = detect;
		}

		private static void CreateLastSeen(IRelayPart primaryNode)
		{
			RelayStorage store = primaryNode.GetStorage();
			if (store == null)
				return;

			foreach (var entityDetected in _detected)
			{
				Debug.Assert(IsValidRadarTarget(entityDetected.Key), "Not valid radar target: " + entityDetected.Key);

				Logger.TraceLog("for " + entityDetected.Key.nameWithId() + " located by radar: " + entityDetected.Value.ByRadarSensorOrCamera + ", by radar noise: " + entityDetected.Value.IsRadarLoud + ", by jammer noise: " + entityDetected.Value.IsJammerLoud);

				LastSeen.DetectedBy detBy = LastSeen.DetectedBy.None;
				if (entityDetected.Value.IsRadarLoud)
					detBy |= LastSeen.DetectedBy.HasRadar;
				if (entityDetected.Value.IsJammerLoud)
					detBy |= LastSeen.DetectedBy.HasJammer;

				if (entityDetected.Value.ByRadarSensorOrCamera)
					store.Receive(new LastSeen(entityDetected.Key, detBy, new LastSeen.RadarInfo(Volume(entityDetected.Key))));
				else
					store.Receive(new LastSeen(entityDetected.Key, detBy));
			}
		}

		#endregion Update

		private readonly IMyEntity _entity;
		private readonly Definition _definition;
		private readonly MyLight _beaconLight;

		private IRelayPart _relayPart;

		private float _targetRadarTransmitPower, _targetJammerTransmitPower;
		private float _radarTransmitPower, _jammerTransmitPower;
		private float _radarJammed, _previousRadarJammed;
		private bool _dirtyCustomInfo;

		private Logable Log
		{
			get { return new Logable(_entity); }
		}

		private string DebugName
		{
			get { return _entity.nameWithId(); }
		}

		private bool IsWorking
		{
			get
			{
				IMyCubeBlock block = _entity as IMyCubeBlock;
				return block == null || block.IsWorking;
			}
		}

		public bool IsRadarOn
		{
			get { return _radarTransmitPower != 0f; }
		}

		public bool IsJammerOn
		{
			get { return _jammerTransmitPower != 0f; }
		}

		public bool CanLocate
		{
			get { return _radarTransmitPower != 0f || _definition.PassiveRadarDetection || _definition.PassiveJammerDetection; }
		}

		public bool CanBePassivelyDetected
		{
			get { return _radarTransmitPower != 0f || _jammerTransmitPower != 0f; }
		}

		public NewRadar(IMyCubeBlock block)
		{
			Debug.Assert(block is IMyBeacon || block is IMyRadioAntenna, "block is of incorrect type: " + block.nameWithId());

			_entity = block;
			_definition = Definition.GetFor(block);
			if (block is MyBeacon)
				_beaconLight = (MyLight)MyBeacon__m_light.GetValue(block);
			((IMyTerminalBlock)block).AppendingCustomInfo += AppendingCustomInfo;

			CtorHelper();

			block.ResourceSink.SetRequiredInputFuncByType(Globals.Electricity, GenerateElectricityFunction());
			UpdateElectricityConsumption();
			Log.TraceLog("created");
		}

		public NewRadar(IMyEntity missile, Definition defn, IMyCubeBlock relationsBlock)
		{
			Debug.Assert(missile is MyAmmoBase, "supplied entity is not a missile");

			_entity = missile;
			_definition = defn;

			CtorHelper();

			if (!TryGetRelayNode(out _relayPart))
			{
				Log.DebugLog("Missile has no antenna, creating a lonely node");
				_relayPart = new RelayNode(missile, () => relationsBlock.OwnerId, null);
			}
			Log.TraceLog("created");
		}

		private void CtorHelper()
		{
			if (_definition.IsRadar && _definition.IsJammer)
			{
				float halfPower = 0.5f * _definition.MaxTransmitterPower;
				SetRadarValue(this, halfPower);
				SetJammerValue(this, halfPower);
			}
			else if (_definition.IsRadar)
				SetRadarValue(this, _definition.MaxTransmitterPower);
			else if (_definition.IsJammer)
				SetJammerValue(this, _definition.MaxTransmitterPower);
			else
				Log.DebugLog("Block is neither radar nor jammer", Logger.severity.WARNING);

			Registrar.Add(_entity, this);
		}

		public int CompareTo(NewRadar other)
		{
			return Math.Sign(_entity.EntityId - other._entity.EntityId);
		}

		private bool TryGetRelayNode(out IRelayPart part)
		{
			Log.EnteredMember();

			RelayNode node;
			if (Registrar.TryGetValue(_entity, out node))
			{
				_relayPart = part = node;
				return part != null;
			}
			part = node;
			return false;
		}

		#region Electricity

		private Func<float> GenerateElectricityFunction()
		{
			if (_entity is IMyCubeBlock)
			{
				MethodInfo UpdatePowerInput = _entity.GetType().GetMethod("UpdatePowerInput", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

				if (UpdatePowerInput != null)
				{
					Func<float> delegateUPI = (Func<float>)Delegate.CreateDelegate(typeof(Func<float>), _entity, UpdatePowerInput);
					return () => delegateUPI.Invoke() + ElectricityConsumptionByRadarEquipment();
				}
				else
					Log.AlwaysLog("Failed to get \"UpdatePowerInput\" method", Logger.severity.WARNING);
			}

			return ElectricityConsumptionByRadarEquipment;
		}

		/// <summary>
		/// Calculates electricty needed to power radar equipment.
		/// </summary>
		/// <returns>Electricty needed to power radar equipment.</returns>
		/// <remarks>
		/// Radar electricty requirements in game is significantly greater than real life because we operate at shorter distances.
		/// </remarks>
		private float ElectricityConsumptionByRadarEquipment()
		{
			Log.EnteredMember();

			const float standbyPowerRatio = 1f / 100f;

			IMyTerminalBlock block = (IMyTerminalBlock)_entity;

			float transmissionPower = _targetRadarTransmitPower + _targetJammerTransmitPower;
			float standbyPower = standbyPowerRatio * _definition.MaxTransmitterPower;

			return 1e-6f * (transmissionPower + standbyPower);
		}

		/// <summary>
		/// Force game to update electricity input by invoking the secret method.
		/// </summary>
		private void UpdateElectricityConsumption()
		{
			Log.EnteredMember();

			if (MySandboxGame.Static.UpdateThread == Thread.CurrentThread)
				(_entity.Components.Get<MyDataBroadcaster>() as MyRadioBroadcaster)?.OnBroadcastRadiusChanged?.Invoke();
			else
				MySandboxGame.Static.Invoke(UpdateElectricityConsumption);
		}

		#endregion Electricity

		#region Custom Info

		private void AppendingCustomInfo(IMyTerminalBlock block, StringBuilder info)
		{
			Log.EnteredMember();

			info.AppendLine();
	
			// radar
			if (IsRadarOn)
			{
				info.Append("Radar transmit power: ");
				info.Append(PrettySI.makePretty(_radarTransmitPower));
				info.Append('W');
				info.AppendLine();

				info.Append("Max radar range: ");
				info.Append(PrettySI.makePretty(CalcMaxRadarRange()));
				info.Append('m');
				info.AppendLine();
			}

			if (_radarJammed > 1f)
				info.AppendLine("  ***  Jamming Detected  ***");

			// jammer
			if (IsJammerOn)
			{
				info.Append("Jammer transmit power: ");
				info.Append(PrettySI.makePretty(_jammerTransmitPower));
				info.Append('W');
				info.AppendLine();
			}

			info.AppendLine();
		}

		/// <summary>
		/// Calculate range at which radar can detect the largest possible ship.
		/// </summary>
		/// <returns>Range at which radar can detect the largest possible ship.</returns>
		private float CalcMaxRadarRange()
		{
			return (float)Math.Pow(_radarTransmitPower * _definition.AntennaConstant * _definition.AntennaConstant * _maxRCS / Math.Max(1f, _radarJammed), 1d / 4d);
		}

		#endregion Custom Info

		#region Update Logic

		/// <summary>
		/// Perform update of <see cref="NewRadar"/>, including <see cref="_beaconLight"/> and power levels.
		/// </summary>
		private void Update()
		{
			if (_beaconLight != null && _beaconLight.Intensity != 0)
			{
				_beaconLight.GlareIntensity = _beaconLight.Intensity = _beaconLight.ReflectorIntensity = 0f;
				MySandboxGame.Static.Invoke(_beaconLight.UpdateLight);
			}

			float change = _radarJammed / _previousRadarJammed;
			Log.TraceLog("previous jam: " + _previousRadarJammed + ", current: " + _radarJammed + ", change: " + change);
			if (change < 0.9f || change > 1.1f)
			{
				_dirtyCustomInfo = true;
				_previousRadarJammed = _radarJammed;
			}
			_radarJammed = 0f;

			if (IsWorking)
			{
				IncreasePowerLevel(ref _radarTransmitPower, _targetRadarTransmitPower);
				IncreasePowerLevel(ref _jammerTransmitPower, _targetJammerTransmitPower);
				Log.TraceLog("is working, power levels: " + _radarTransmitPower + ", " + _jammerTransmitPower);
			}
			else
			{
				DecreasePowerLevel(ref _radarTransmitPower);
				DecreasePowerLevel(ref _jammerTransmitPower);
				Log.TraceLog("not working, power levels: " + _radarTransmitPower + ", " + _jammerTransmitPower);
			}

			if (_dirtyCustomInfo)
			{
				_dirtyCustomInfo = false;
				IMyTerminalBlock term = _entity as IMyTerminalBlock;
				if (term != null)
					MySandboxGame.Static.Invoke(term.UpdateCustomInfo);
			}
		}

		private void IncreasePowerLevel(ref float powerLevel, float max)
		{
			if (powerLevel == max)
				return;
			powerLevel += max * 0.1f;
			if (powerLevel > max)
				powerLevel = max;
			_dirtyCustomInfo = true;
		}

		private void DecreasePowerLevel(ref float powerLevel)
		{
			if (powerLevel == 0f)
				return;
			powerLevel -= _definition.MaxTransmitterPower * 0.1f;
			if (powerLevel < 0f)
				powerLevel = 0f;
			_dirtyCustomInfo = true;
		}

		/// <summary>
		/// Checks for acceptable angle and ray cast.
		/// </summary>
		/// <param name="line">Line from this entity to target entity.</param>
		/// <param name="target">Entity that will be ignored by ray cast.</param>
		/// <returns></returns>
		private bool SignalCanReach(ref LineD line, IMyEntity target)
		{
			if (_definition.EnforceAngle && UnacceptableAngle(ref line.Direction))
			{
				Log.DebugLog(target.nameWithId() + " rejected because it is outside angle limits");
				return false;
			}

#if DEBUG
			if (RayCastObstructed(ref line, _entity, target)) 
			{
				Log.DebugLog(target.nameWithId() + " is obstructed");
				return false;
			}
			return true;
#else
			return !RayCastObstructed(ref line, _entity, target);
#endif
		}

		private bool UnacceptableAngle(ref Vector3D worldDirection)
		{
			Debug.Assert(_definition.EnforceAngle, "Angle should not be enforced");

			MatrixD rotate = _entity.WorldMatrixNormalizedInv;
			Vector3D localDirection; Vector3D.Rotate(ref worldDirection, ref rotate, out localDirection);

			float elevation, azimuth;
			Vector3.GetAzimuthAndElevation(localDirection, out azimuth, out elevation);
			return elevation < _definition.MinElevation || elevation > _definition.MaxElevation || azimuth < _definition.MinAzimuth || azimuth > _definition.MaxAzimuth;
		}

		private void ApplyJamming(ref Vector3D targetPosition, NewRadar targetDevice)
		{
			Debug.Assert(IsWorking, "This device is not working");
			Debug.Assert(targetDevice.IsWorking, targetDevice.DebugName + " is not working");
			Debug.Assert(CanLocate, "This device cannot be jammed");
			Debug.Assert(targetDevice.IsJammerOn, targetDevice.DebugName + " is not jammer");

			LineD line = new LineD(_entity.GetCentre(), targetPosition);
			if (SignalCanReach(ref line, targetDevice._entity))
			{
				// no receiver gain for jamming
				double distSquared; Vector3D.DistanceSquared(ref targetPosition, ref line.From, out distSquared);
				_radarJammed += targetDevice._jammerTransmitPower * targetDevice._definition.AntennaConstant / (float)distSquared;
				Log.TraceLog("Jammed by " + targetDevice.DebugName + ", total: " + _radarJammed);
			}
		}

		private float RadarSignal(ref Vector3D targetPosition, IMyEntity target, float targetRCS)
		{
			Debug.Assert(IsWorking, "This device is not working");
			Debug.Assert(IsRadarOn, "This device is not radar");
			Debug.Assert(!(target is IMyCubeBlock), "block not expected");

			LineD line = new LineD(_entity.GetCentre(), targetPosition);
			if (SignalCanReach(ref line, target))
			{
				double distSquared; Vector3D.DistanceSquared(ref targetPosition, ref line.From, out distSquared);
				return Math.Max(_radarTransmitPower * _definition.AntennaConstant * _definition.AntennaConstant * targetRCS / ((float)distSquared * (float)distSquared) - _radarJammed * _definition.SignalToJamRatio, 0f);
			}
			else
				return 0f;
		}

		private float PassiveSignalFromRadar(ref LineD vettedLine, NewRadar targetDevice)
		{
			Debug.Assert(IsWorking, "This device is not working");
			Debug.Assert(targetDevice.IsWorking, targetDevice.DebugName + " is not working");
			Debug.Assert(_definition.PassiveRadarDetection, "This device cannot passively detect radar");
			Debug.Assert(targetDevice.IsRadarOn, targetDevice.DebugName + " is not radar");

			double distSquared; Vector3D.DistanceSquared(ref vettedLine.To, ref vettedLine.From, out distSquared);
			return targetDevice._radarTransmitPower * targetDevice._definition.AntennaConstant * _definition.AntennaConstant / (float)distSquared;
		}

		private float PassiveSignalFromJammer(ref LineD vettedLine, NewRadar targetDevice)
		{
			Debug.Assert(IsWorking, "This device is not working");
			Debug.Assert(targetDevice.IsWorking, targetDevice.DebugName + " is not working");
			Debug.Assert(_definition.PassiveJammerDetection, "This device cannot passively detect jammer");
			Debug.Assert(targetDevice.IsJammerOn, targetDevice.DebugName + " is not jammer");

			double distSquared; Vector3D.DistanceSquared(ref vettedLine.To, ref vettedLine.From, out distSquared);
			return targetDevice._jammerTransmitPower * targetDevice._definition.AntennaConstant * _definition.AntennaConstant / (float)distSquared;
		}

		private float PassiveSignalFromDecoy(ref Vector3D targetPosition, IMyEntity decoyGrid)
		{
			Debug.Assert(decoyGrid is IMyCubeGrid, "not a grid: " + decoyGrid.nameWithId());

			LineD line = new LineD(_entity.GetCentre(), targetPosition);
			if (SignalCanReach(ref line, decoyGrid))
			{
				double distSquared; Vector3D.DistanceSquared(ref targetPosition, ref line.From, out distSquared);
				return  Definition.Decoy.MaxTransmitterPower * Definition.Decoy.AntennaConstant * _definition.AntennaConstant / (float)distSquared;
			}
			return 0f;
		}

		#endregion Update Logic

	}
}
