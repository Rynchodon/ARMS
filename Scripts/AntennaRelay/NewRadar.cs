using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using Rynchodon.Update;
using Sandbox;
using Sandbox.Common.ObjectBuilders;
using Sandbox.Game.Entities;
using Sandbox.Game.Entities.Cube;
using Sandbox.Game.Lights;
using Sandbox.Game.World;
using Sandbox.ModAPI;
using VRage.Game.Entity;
using VRage.Game.ModAPI;
using VRage.ModAPI;
using VRageMath;

namespace Rynchodon.AntennaRelay
{
	/// <remarks>
	/// <para>Now with real radar equation!</para>
	/// <para>pattern propogation factor = 1</para>
	/// <para>wavelength = 1</para>
	/// <para>min signal strength = 1</para>
	/// </remarks>
	sealed class NewRadar : LogWise
	{
		public sealed class Definition
		{
			/// <summary>Maximum power of transmitter.</summary>
			public float Radar_MaxTransmitterPower;

			/// <summary>Maximum power of transmitter.</summary>
			public float Jammer_MaxTransmitterPower;

			/// <summary>Antenna gain in decibels.</summary>
			public float AntennaGainDecibels;

			/// <summary>Antenna gain factor, not decibels.</summary>
			public float AntennaGainFactor;

			/// <summary>How much stronger the signal needs to be than jam noise.</summary>
			public float SignalToJamRatio = 10f;

			public bool PassiveRadarDetection, PassiveJammerDetection;

			/// <summary>antenna gain / (4 Pi). Calaculated by <see cref="Init"/>.</summary>
			public float AntennaConstant;

			public void Init()
			{
				if (AntennaGainFactor == 0f)
					AntennaGainFactor = (float)Math.Pow(10d, AntennaGainDecibels / 10d);
				AntennaConstant = AntennaGainFactor / MathHelper.FourPi;
			}
		}

		private static readonly int _blockLimit;
		/// <summary>RCS for largest possible ship.</summary>
		private static readonly float _maxRCS;

		private static FieldInfo MyBeacon__m_light = typeof(MyBeacon).GetField("m_light", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

		/// <summary>Group of equipment that is linked through relay currently being processed.</summary>
		private static readonly HashSet<NewRadar> _linkedEquipment = new HashSet<NewRadar>();
		private static readonly List<MyEntity> _nearbyEntities = new List<MyEntity>();
		private static readonly List<NewRadar> _nearbyEquipment = new List<NewRadar>();
		private static readonly Dictionary<IMyEntity, LastSeen.UpdateTime> _detected = new Dictionary<IMyEntity, LastSeen.UpdateTime>();

		static NewRadar()
		{
			_blockLimit = MySession.Static.MaxGridSize;
			_maxRCS = SphereCrossSection(_blockLimit * 2.5f * 2.5f * 2.5f);
			if (MyBeacon__m_light == null)
				throw new NullReferenceException("MyBeacon__m_light");
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
		/// Estimate the cross section of the entity by pretending it is a sphere.
		/// </summary>
		private static float RadarCrossSection(IMyEntity entity)
		{
			IMyCubeGrid grid = entity as IMyCubeGrid;
			if (grid != null)
			{
				CubeGridCache cache = CubeGridCache.GetFor(grid);
				if (cache != null)
					return SphereCrossSection(cache.CellCount * grid.GridSize * grid.GridSize * grid.GridSize);
			}
			return SphereCrossSection(entity.LocalAABB.Volume());
		}

		private static void Update(long ownerId)
		{
			Logger.DebugLog("_nearbyEntities not cleared", Logger.severity.ERROR, condition: _nearbyEntities.Count != 0);
			Logger.DebugLog("_nearbyEquipment not cleared", Logger.severity.ERROR, condition: _nearbyEquipment.Count != 0);
			Logger.DebugLog("_detected not cleared", Logger.severity.ERROR, condition: _detected.Count != 0);

			BoundingSphereD locale;
			locale.Radius = 50000d;

			foreach (NewRadar radar in _linkedEquipment)
				if (radar.IsWorking)
				{
					radar.Update();
					locale.Center = radar._entity.GetCentre();
					MyGamePruningStructure.GetAllTopMostEntitiesInSphere(ref locale, _nearbyEntities);
				}

			// all jamming must be applied before any detection
			CollectEquipmentAndJam(ownerId);
			RadarDetection();
			PassiveDetection();

			_nearbyEntities.Clear();
			_nearbyEquipment.Clear();
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
				else
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
			NewRadar equip;
			if (!Registrar.TryGetValue(entity, out equip) || !equip.IsWorking || _linkedEquipment.Contains(equip))
				return;

			_nearbyEquipment.Add(equip);

			if (ExtensionsRelations.canConsiderHostile(ownerId, entity, false) && equip.IsJammerOn)
			{
				Vector3D position = entity.GetCentre();
				foreach (NewRadar linked in _linkedEquipment)
					if (linked.IsWorking && linked.CanBeJammed)
						linked.ApplyJamming(ref position, equip);
			}
		}

		/// <summary>
		/// Foreach entity in <see cref="_nearbyEntities"/>, attempt to actively detect the entity with radar in <see cref="_linkedEquipment"/>.
		/// </summary>
		private static void RadarDetection()
		{
			const float minSignal = 1f;

			foreach (MyEntity entity in _nearbyEntities)
			{
				Vector3D position = entity.GetCentre();
				float RCS = RadarCrossSection(entity);
				float signal = 0f;
				foreach (NewRadar linked in _linkedEquipment)
				{
					if (!linked.IsWorking || !linked.IsRadarOn)
						continue;
					signal += linked.RadarSignal(ref position, RCS);
					if (signal > minSignal)
					{
						AddRadarDetected(entity);
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
			const float minSignalDetectRadar = 1e5f;
			const float minSignalDetectJammer = 1e6f;

			foreach (NewRadar equip in _nearbyEquipment)
			{
				if (!equip.IsWorking || !equip.CanBePassivelyDetected)
					continue;
				Vector3D position = equip._entity.GetCentre();
				float radarSignal = 0f, jammerSignal = 0f; // TODO: if already passive detected of group, reuse values?
				foreach (NewRadar linked in _linkedEquipment)
				{
					if (!linked.IsWorking)
						continue;
					if (radarSignal < minSignalDetectRadar && equip.IsRadarOn && linked._definition.PassiveRadarDetection)
					{
						radarSignal += linked.PassiveSignalFromRadar(ref position, equip);
						if (radarSignal >= minSignalDetectRadar)
						{
							AddPassiveDetectedRadar(equip);
							if (jammerSignal >= minSignalDetectJammer || !equip.IsJammerOn)
								break;
						}
					}

					if (jammerSignal < minSignalDetectJammer && equip.IsJammerOn && linked._definition.PassiveJammerDetection)
					{
						jammerSignal += linked.PassiveSignalFromJammer(ref position, equip);
						if (jammerSignal >= minSignalDetectJammer)
						{
							AddPassiveDetectedJammer(equip);
							if (radarSignal >= minSignalDetectRadar || !equip.IsRadarOn)
								break;
						}
					}
				}
			}
		}

		private static void AddRadarDetected(IMyEntity entity)
		{
			if (entity is IMyCubeBlock)
				entity = ((IMyCubeBlock)entity).CubeGrid;

			LastSeen.UpdateTime times;
			_detected.TryGetValue(entity, out times);
			_detected[entity] = times;
		}

		private static void AddPassiveDetectedRadar(NewRadar radar)
		{

		}

		private static void AddPassiveDetectedJammer(NewRadar jammer)
		{

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

		public bool CanBeJammed
		{
			get { return _radarTransmitPower != 0f || _definition.PassiveRadarDetection || _definition.PassiveJammerDetection; }
		}

		public bool CanBePassivelyDetected
		{
			get { return _radarTransmitPower != 0f || _jammerTransmitPower != 0f; }
		}

		private NewRadar(IMyEntity entity, Definition defn)
		{
			_entity = entity;
			_definition = defn;
			if (entity is MyBeacon)
				_beaconLight = (MyLight)MyBeacon__m_light.GetValue(entity);
			if (entity is IMyTerminalBlock)
			{
				IMyTerminalBlock term = (IMyTerminalBlock)entity;
				term.AppendingCustomInfo += AppendingCustomInfo;
			}
			Registrar.Add(entity, this);
		}

		protected override string GetContext()
		{
			if (_entity is IMyCubeBlock)
				return ((IMyCubeBlock)_entity).CubeGrid.nameWithId();
			return _entity.nameWithId();
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

		private void UpdateElectricityConsumption()
		{
			const float standbyPowerRatio = 0.001f;

			IMyTerminalBlock block = (IMyTerminalBlock)_entity;

			float electricityConsumption = Math.Max(_targetRadarTransmitPower + _targetJammerTransmitPower, standbyPowerRatio * (_definition.Radar_MaxTransmitterPower + _definition.Jammer_MaxTransmitterPower));
			block.ResourceSink.SetRequiredInputByType(Globals.Electricity, electricityConsumption * 1e-6f);

			block.UpdateCustomInfo();
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

			info.Append("Electricty use: ");
			info.Append(PrettySI.makePretty(((IMyCubeBlock)_entity).ResourceSink.RequiredInputByType(Globals.Electricity) * 1e6));
			info.Append('W');
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

				IncreasePowerLevel(ref _radarTransmitPower, _targetRadarTransmitPower);
				IncreasePowerLevel(ref _jammerTransmitPower, _targetJammerTransmitPower);
			}
			else
			{
				DecreasePowerLevel(ref _radarTransmitPower, _definition.Radar_MaxTransmitterPower);
				DecreasePowerLevel(ref _jammerTransmitPower, _definition.Jammer_MaxTransmitterPower);
				return;
			}
		}

		private void IncreasePowerLevel(ref float powerLevel, float maxPower)
		{
			if (powerLevel == maxPower)
				return;
			powerLevel += maxPower * 0.01f;
			if (powerLevel > maxPower)
				powerLevel = maxPower;
			MySandboxGame.Static.Invoke(UpdateElectricityConsumption);
		}

		private void DecreasePowerLevel(ref float powerLevel, float maxPower)
		{
			if (powerLevel == 0f)
				return;
			powerLevel -= maxPower * 0.01f;
			if (powerLevel < 0f)
				powerLevel = 0f;
			MySandboxGame.Static.Invoke(UpdateElectricityConsumption);
		}

		private void ApplyJamming(ref Vector3D position, NewRadar jammer)
		{
			debugLog("This device is not working", Logger.severity.ERROR, condition: !IsWorking);
			debugLog(jammer._entity.nameWithId() + " is not working", Logger.severity.ERROR, condition: !jammer.IsWorking);
			debugLog("This device cannot be jammed", Logger.severity.ERROR, condition: !CanBeJammed);
			debugLog(jammer._entity.nameWithId() + " is not jammer", Logger.severity.ERROR, condition: !jammer.IsJammerOn);

			Vector3D ePosition = _entity.GetCentre();
			double distSquared; Vector3D.DistanceSquared(ref position, ref ePosition, out distSquared);
			_radarJammed += jammer._jammerTransmitPower * jammer._definition.AntennaConstant / (float)distSquared;
		}

		private float RadarSignal(ref Vector3D position, float RCS)
		{
			debugLog("This device is not working", Logger.severity.ERROR, condition: !IsWorking);
			debugLog("This device is not radar", Logger.severity.ERROR, condition: !IsRadarOn);

			Vector3D ePosition = _entity.GetCentre();
			double distSquared; Vector3D.DistanceSquared(ref position, ref ePosition, out distSquared);
			return Math.Max(_radarTransmitPower * _definition.AntennaConstant * _definition.AntennaConstant * RCS / ((float)distSquared * (float)distSquared) - _radarJammed, 0f);
		}

		private float PassiveSignalFromRadar(ref Vector3D position, NewRadar otherDevice)
		{
			debugLog("This device is not working", Logger.severity.ERROR, condition: !IsWorking);
			debugLog(otherDevice._entity.nameWithId() + " is not working", Logger.severity.ERROR, condition: !otherDevice.IsWorking);
			debugLog("This device cannot passively detect radar", Logger.severity.ERROR, condition: _definition.PassiveRadarDetection);
			debugLog(otherDevice._entity.nameWithId() + " is not radar", Logger.severity.ERROR, condition: !otherDevice.IsRadarOn);

			Vector3D ePosition = _entity.GetCentre();
			double distSquared; Vector3D.DistanceSquared(ref position, ref ePosition, out distSquared);
			return otherDevice._radarTransmitPower * otherDevice._definition.AntennaConstant * _definition.AntennaConstant / (float)distSquared;
		}

		private float PassiveSignalFromJammer(ref Vector3D position, NewRadar otherDevice)
		{
			debugLog("This device is not working", Logger.severity.ERROR, condition: !IsWorking);
			debugLog(otherDevice._entity.nameWithId() + " is not working", Logger.severity.ERROR, condition: !otherDevice.IsWorking);
			debugLog("This device cannot passively detect jammer", Logger.severity.ERROR, condition: _definition.PassiveJammerDetection);
			debugLog(otherDevice._entity.nameWithId() + " is not jammer", Logger.severity.ERROR, condition: !otherDevice.IsJammerOn);

			Vector3D ePosition = _entity.GetCentre();
			double distSquared; Vector3D.DistanceSquared(ref position, ref ePosition, out distSquared);
			return otherDevice._jammerTransmitPower * otherDevice._definition.AntennaConstant * _definition.AntennaConstant / (float)distSquared;
		}

	}
}
