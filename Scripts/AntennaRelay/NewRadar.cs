#if DEBUG
#define TRACE
#endif

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Reflection;
using System.Text;
using System.Xml.Serialization;
using Rynchodon.Utility.Network;
using Sandbox;
using Sandbox.Common.ObjectBuilders;
using Sandbox.Definitions;
using Sandbox.Game.Entities;
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
	/// beacon/radio power
	/// guided missile reflectivity => RCS
	/// round up linked blocks
	public sealed class NewRadar : LogWise
	{
		public sealed class Definition
		{
			/// <summary>Definition for a decoy block.</summary>
			public static readonly Definition Decoy;

			private static readonly Dictionary<SerializableDefinitionId, Definition> _knownDefinitions = new Dictionary<SerializableDefinitionId, Definition>();
			private static readonly List<SerializableDefinitionId> _notRadarEquip = new List<SerializableDefinitionId>();

			static Definition()
			{
				const float decoyPower = 50f; // ~ RTG

				Decoy = new Definition();
				Decoy.MaxTransmitterPower = decoyPower / _transmitElectricityRatio;
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

			public bool ByRadar;
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

		/// <summary>Ratio of transmission power to electricity required.</summary>
		private const float _transmitElectricityRatio = 0.002f;

		private static readonly int _blockLimit;
		/// <summary>RCS for largest possible ship.</summary>
		private static readonly float _maxRCS;

		private static readonly Threading.ThreadManager _thread = new Threading.ThreadManager(threadName: typeof(NewRadar).Name);

		private static readonly FieldInfo MyBeacon__m_light = typeof(MyBeacon).GetField("m_light", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

		/// <summary>Group of equipment that is linked through relay currently being processed.</summary>
		private static readonly HashSet<NewRadar> _linkedEquipment = new HashSet<NewRadar>();
		private static readonly List<MyEntity> _nearbyEntities = new List<MyEntity>();
		private static readonly List<NewRadar> _nearbyEquipment = new List<NewRadar>();
		private static readonly Dictionary<IMyEntity, Detected> _detected = new Dictionary<IMyEntity, Detected>();
		private static readonly IMyEntity[] _obstructedIgnore = new IMyEntity[2];

		private static readonly MyTerminalControlSlider<MyFunctionalBlock> _radarSlider, _jammerSlider;

		static NewRadar()
		{
			Logger.DebugLog("start init");

			_blockLimit = MySession.Static.MaxGridSize;
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

			Logger.DebugLog("end init");
		}

		public static bool IsDefinedRadarEqipment(IMyCubeBlock block)
		{
			return Definition.GetFor(block) != null;
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

			Logger.TraceLog(block.nameWithId() + " is radar: " + equip._definition.IsRadar);
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

			Logger.TraceLog(block.nameWithId() + " is jammer: " + equip._definition.IsJammer);
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

		private static void ReplaceByGridIfBlock(ref IMyEntity entity)
		{
			if (entity is IMyCubeBlock)
				entity = ((IMyCubeBlock)entity).CubeGrid;
		}

		private static IMyEntity GridIfBlock(IMyEntity entity)
		{
			return entity is IMyCubeBlock ? ((IMyCubeBlock)entity).CubeGrid : entity;
		}

		private static void Update(RelayNode node)
		{
			Debug.Assert(_nearbyEntities.Count == 0, "_nearbyEntities not cleared");
			Debug.Assert(_nearbyEquipment.Count == 0, "_nearbyEquipment not cleared");
			Debug.Assert(_detected.Count == 0, "_detected not cleared");

			BoundingSphereD locale;
			locale.Radius = 50000d;

			foreach (NewRadar radar in _linkedEquipment)
				if (radar.IsWorking && radar.CanLocate)
				{
					radar.Update();
					locale.Center = radar._entity.GetCentre();
					MyGamePruningStructure.GetAllTopMostEntitiesInSphere(ref locale, _nearbyEntities);
				}

			// all jamming must be applied before any detection
			long ownerId = node.OwnerId;
			CollectEquipmentAndJam(ownerId);
			RadarDetection();
			PassiveDetection();
			CreateLastSeen(node);

			_nearbyEntities.Clear();
			_nearbyEquipment.Clear();
			_detected.Clear();
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
			if (!Registrar.TryGetValue(entity, out equip) || !equip.IsWorking || _linkedEquipment.Contains(equip))
				return;

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
		/// Check that a top-most entity is a valid target for detection by radar.
		/// </summary>
		private static bool IsValidRadarTarget(IMyEntity entity)
		{
			Debug.Assert(!(entity is IMyCubeBlock), "block not expected");
			return entity is IMyCubeGrid || entity is IMyCharacter || entity is MyAmmoBase;
		}

		/// <summary>
		/// Foreach entity in <see cref="_nearbyEntities"/>, attempt to actively detect the entity with radar in <see cref="_linkedEquipment"/>.
		/// </summary>
		private static void RadarDetection()
		{
			const float minSignal = 1f;

			foreach (MyEntity entity in _nearbyEntities)
				if (IsValidRadarTarget(entity))
				{
					Vector3D position = entity.GetCentre();
					float RCS = RadarCrossSection(entity);
					float signal = 0f;
					foreach (NewRadar linked in _linkedEquipment)
					{
						if (!linked.IsWorking || !linked.IsRadarOn)
							continue;
						signal += linked.RadarSignal(ref position, entity, RCS);
						if (signal > minSignal)
						{
							Detected detect = new Detected();
							detect.ByRadar = true;
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
				Vector3D position = equip._entity.GetCentre();

				Detected detect;
				_detected.TryGetValue(GridIfBlock(equip._entity), out detect);

				if (detect.IsRadarLoud && detect.IsJammerLoud)
					continue;

				foreach (NewRadar linked in _linkedEquipment)
				{
					if (!linked.IsWorking)
						continue;

					bool radar =  detect.IsRadarQuiet && equip.IsRadarOn && linked._definition.PassiveRadarDetection;
					bool jammer = detect.IsJammerQuiet && equip.IsJammerOn && linked._definition.PassiveJammerDetection;

					if (radar || jammer)
					{
						LineD line = new LineD(linked._entity.GetCentre(), position);
						if (!linked.SignalCanReach(ref line, linked._entity))
							continue;

						if (radar)
							detect.RadarNoise += linked.PassiveSignalFromRadar(ref line, equip);
						if (jammer)
							detect.JammerNoise += linked.PassiveSignalFromJammer(ref line, equip);

						PushDetected(GridIfBlock(equip._entity), ref detect);

						if (detect.IsRadarLoud && detect.IsJammerLoud)
							break;
					}
				}
			}
		}

		private static void DecoyDetection()
		{
			foreach (IMyEntity entity in _nearbyEntities)
				if (entity is IMyCubeGrid)
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
							if (detect.IsRadarLoud && detect.IsJammerLoud)
								break;
						}

					PushDetected(entity, ref detect);
				}
		}

		private static void PushDetected(IMyEntity entity, ref Detected detect)
		{
			Debug.Assert(!(entity is IMyCubeBlock), entity.nameWithId() + " is block, grid should be passed");
			_detected[entity] = detect;
		}

		private static void CreateLastSeen(RelayNode node)
		{
			RelayStorage store = node.GetStorage();
			if (store == null)
				return;

			foreach (var entityDetected in _detected)
			{
				Debug.Assert(IsValidRadarTarget(entityDetected.Key), "Not valid radar target: " + entityDetected.Key);

				LastSeen.DetectedBy detBy = LastSeen.DetectedBy.None;
				if (entityDetected.Value.IsRadarLoud)
					detBy |= LastSeen.DetectedBy.HasRadar;
				if (entityDetected.Value.IsJammerLoud)
					detBy |= LastSeen.DetectedBy.HasJammer;

				if (entityDetected.Value.ByRadar)
					store.Receive(new LastSeen(entityDetected.Key, detBy, new LastSeen.RadarInfo(Volume(entityDetected.Key))));
				else
					store.Receive(new LastSeen(entityDetected.Key, detBy));
			}
		}

		private readonly IMyEntity _entity;
		private readonly Definition _definition;
		private readonly MyLight _beaconLight;

		private float _targetRadarTransmitPower, _targetJammerTransmitPower;
		private float _radarTransmitPower, _jammerTransmitPower;
		private float _radarJammed;

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
			_entity = block;
			_definition = Definition.GetFor(block);
			if (block is MyBeacon)
				_beaconLight = (MyLight)MyBeacon__m_light.GetValue(block);
			if (block is IMyTerminalBlock)
			{
				IMyTerminalBlock term = (IMyTerminalBlock)block;
				term.AppendingCustomInfo += AppendingCustomInfo;
			}

			if (_definition.IsRadar && _definition.IsJammer)
				_targetRadarTransmitPower = _targetJammerTransmitPower = 0.5f * _definition.MaxTransmitterPower;
			else if (_definition.IsRadar)
				_targetRadarTransmitPower = _definition.MaxTransmitterPower;
			else if (_definition.IsJammer)
				_targetJammerTransmitPower = _definition.MaxTransmitterPower;
			else
				debugLog("Block is neither radar nor jammer", Logger.severity.WARNING);

			Registrar.Add(block, this);
			block.ResourceSink.SetRequiredInputFuncByType(Globals.Electricity, GetElectrictyConsumption);
			UpdateElectricityConsumption();
			debugLog("created");
		}

		protected override string GetContext()
		{
			return GridIfBlock(_entity).nameWithId();
		}

		protected override string GetPrimary()
		{
			if (_entity is IMyCubeBlock)
				return ((IMyCubeBlock)_entity).DefinitionDisplayNameText;
			return null;
		}

		protected override string GetSecondary()
		{
			if (_entity is IMyCubeBlock)
				return ((IMyCubeBlock)_entity).nameWithId();
			return null;
		}

		private float GetElectrictyConsumption()
		{
			const float standbyPowerRatio = _transmitElectricityRatio / 100f;

			IMyTerminalBlock block = (IMyTerminalBlock)_entity;

			float transmissionPower = _transmitElectricityRatio * (_targetRadarTransmitPower + _targetJammerTransmitPower);
			float standbyPower = standbyPowerRatio * _definition.MaxTransmitterPower;

			return 1e-6f * MathHelper.Max(transmissionPower, standbyPower, 1f);
		}

		private void UpdateElectricityConsumption()
		{
			MyCubeBlock block = (MyCubeBlock)_entity;
			block.ResourceSink.Update();

			MyRadioBroadcaster broadcaster = block.Components.Get<MyRadioBroadcaster>();
			if (broadcaster != null)
				broadcaster.OnBroadcastRadiusChanged?.Invoke();
		}

		private void AppendingCustomInfo(IMyTerminalBlock block, StringBuilder info)
		{
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

			// jammer
			if (IsJammerOn)
			{
				info.Append("Jammer transmit power: ");
				info.Append(PrettySI.makePretty(_jammerTransmitPower));
				info.Append('W');
				info.AppendLine();

				info.Append("Max jamming range: ");
				info.Append(PrettySI.makePretty(CalcMaxJammerRange()));
				info.Append('m');
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
			return (float)Math.Pow(_radarTransmitPower * _definition.AntennaConstant * _definition.AntennaConstant * _maxRCS, 1d / 4d);
		}

		private float CalcMaxJammerRange()
		{
			return (float)Math.Sqrt(_jammerTransmitPower * _definition.AntennaConstant);
		}

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

			_radarJammed = 0f;

			if (IsWorking)
			{
				// TODO: set target power from slider

				IncreasePowerLevel(ref _radarTransmitPower);
				IncreasePowerLevel(ref _jammerTransmitPower);
			}
			else
			{
				DecreasePowerLevel(ref _radarTransmitPower);
				DecreasePowerLevel(ref _jammerTransmitPower);
			}
		}

		private void IncreasePowerLevel(ref float powerLevel)
		{
			if (powerLevel == _definition.MaxTransmitterPower)
				return;
			powerLevel += _definition.MaxTransmitterPower * 0.1f;
			if (powerLevel > _definition.MaxTransmitterPower)
				powerLevel = _definition.MaxTransmitterPower;
			if (_entity is IMyTerminalBlock)
				MySandboxGame.Static.Invoke(UpdateElectricityConsumption);
		}

		private void DecreasePowerLevel(ref float powerLevel)
		{
			if (powerLevel == 0f)
				return;
			powerLevel -= _definition.MaxTransmitterPower * 0.1f;
			if (powerLevel < 0f)
				powerLevel = 0f;
			if (_entity is IMyTerminalBlock)
				MySandboxGame.Static.Invoke(UpdateElectricityConsumption);
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
				debugLog(target.nameWithId() + " rejected because it is outside angle limits");
				return false;
			}

			_obstructedIgnore[0] = _entity;
			_obstructedIgnore[1] = target;
#if DEBUG
			if (RayCast.Obstructed(line, _nearbyEntities, _obstructedIgnore, true, true))
			{
				debugLog(target.nameWithId() + " is obstructed");
				return false;
			}
			return true;
#else
			return !RayCast.Obstructed(line, _nearbyEntities, _obstructedIgnore, true, true);
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
			Debug.Assert(targetDevice.IsWorking, targetDevice._entity.nameWithId() + " is not working");
			Debug.Assert(CanLocate, "This device cannot be jammed");
			Debug.Assert(targetDevice.IsJammerOn, targetDevice._entity.nameWithId() + " is not jammer");

			LineD line = new LineD(_entity.GetCentre(), targetPosition);
			if (SignalCanReach(ref line, targetDevice._entity))
			{
				double distSquared; Vector3D.DistanceSquared(ref targetPosition, ref line.From, out distSquared);
				_radarJammed += targetDevice._jammerTransmitPower * targetDevice._definition.AntennaConstant / (float)distSquared;
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
			Debug.Assert(targetDevice.IsWorking, targetDevice._entity.nameWithId() + " is not working");
			Debug.Assert(_definition.PassiveRadarDetection, "This device cannot passively detect radar");
			Debug.Assert(targetDevice.IsRadarOn, targetDevice._entity.nameWithId() + " is not radar");

			double distSquared; Vector3D.DistanceSquared(ref vettedLine.To, ref vettedLine.From, out distSquared);
			return targetDevice._radarTransmitPower * targetDevice._definition.AntennaConstant * _definition.AntennaConstant / (float)distSquared;
		}

		private float PassiveSignalFromJammer(ref LineD vettedLine, NewRadar targetDevice)
		{
			Debug.Assert(IsWorking, "This device is not working");
			Debug.Assert(targetDevice.IsWorking, targetDevice._entity.nameWithId() + " is not working");
			Debug.Assert(_definition.PassiveJammerDetection, "This device cannot passively detect jammer");
			Debug.Assert(targetDevice.IsJammerOn, targetDevice._entity.nameWithId() + " is not jammer");

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

	}
}
