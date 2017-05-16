using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using Rynchodon.Threading;
using Rynchodon.Weapons.Guided;
using Sandbox.Common.ObjectBuilders;
using Sandbox.Definitions;
using Sandbox.Game.Entities;
using Sandbox.Game.Entities.Cube;
using Sandbox.Game.Lights;
using Sandbox.ModAPI;
using Sandbox.ModAPI.Interfaces;
using VRage.Game;
using VRage.Game.Entity;
using VRage.Game.ModAPI;
using VRage.Game.ObjectBuilders.Definitions;
using VRage.ModAPI;
using VRage.ObjectBuilders;
using VRageMath;
using Ingame = Sandbox.ModAPI.Ingame;

namespace Rynchodon.AntennaRelay
{
	/// <summary>
	/// <para>Class for Radar and Radar Jammers</para>
	[Obsolete]
	public class RadarEquipment
	{
		/// <summary>
		/// <para>Every field and property can be explicitly defined in .sbc files, except EnforceAngle.</para>
		/// </summary>
		public class Definition
		{
			#region Public Fields

			/// <summary>
			/// <para>Can actively detect objects.</para>
			/// <para>Passive detection is not restricted to radar.</para>
			/// </summary>
			public bool Radar = false;

			/// <summary>Equipment requires line of sight to work.</summary>
			public bool LineOfSight = true;

			/// <summary>The maximum number of targets that can be detected (active + passive).</summary>
			public int MaxTargets_Tracking = 1;

			/// <summary>The maximum number of radar that can be jammed. No effect on passive detection.</summary>
			public int MaxTargets_Jamming = 0;

			/// <summary>Strength of jamming signal necessary to passively determine the location of a radar jammer. 0 = no passive detection.</summary>
			public float PassiveDetect_Jamming = 0;

			/// <summary>Strength of radar signal necessary to passively determine the location of a radar. 0 = no passive detection.</summary>
			public float PassiveDetect_Radar = 0;

			/// <summary>Power level will be forced down to this number.</summary>
			public float MaxPowerLevel = 50000;

			/// <summary>Power change per 100 updates while on.</summary>
			public float PowerIncrease = 1000;

			/// <summary>Power change per 100 updates while off.</summary>
			public float PowerDecrease = -2000;

			/// <summary>
			/// <para>Represents the quality of this piece of equipment. Affects the function of the device without affecting its detectability.</para>
			/// </summary>
			public float SignalEnhance = 1;

			///// <summary>Not implemented. How well the signal can penetrate a solid object.</summary>
			//public float Penetration = 0;

			/// <summary>How much the jamming effect affects this radar. In the range [1-0].</summary>
			public float JammingEffect = 1f;

			/// <summary>How much of the jamming effect is applied to friendly radar and radar beyond the MaximumTargets limit.</summary>
			public float JamIncidental = 0.2f;

			/// <summary>
			/// <para>Affects how much of the signal is reflected back.</para>
			/// <para>Reflected signal = signal * (volume + A) / (volume + B)</para>
			/// </summary>
			public float Reflect_A = 1000, Reflect_B = 20000;

			/// <summary>
			/// <para>Iff false, angles are ignored.</para>
			/// <para>Will be set to true if any angles are specified.</para>
			/// </summary>
			public bool EnforceAngle = false;

			#endregion

			#region Public Properties

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

			#endregion

			private float value_MinAzimuth = -MathHelper.Pi, value_MaxAzimuth = MathHelper.Pi, value_MinElevation = -MathHelper.Pi, value_MaxElevation = MathHelper.Pi;
		}

		private class DetectedInfo : IComparable<DetectedInfo>
		{
			public readonly IMyEntity Entity;
			public readonly ExtensionsRelations.Relations Relate;
			private readonly byte relateOrder;

			public LastSeen.RadarInfo Info { get; private set; }

			public float RadarSignature { get; private set; }
			public float RadarSignal { get; set; }
			public float JammerSignal { get; set; }

			public float MaxSignal { get { return Math.Max(RadarSignature, Math.Max(RadarSignal, JammerSignal)); } }

			public LastSeen.DetectedBy Times
			{
				get
				{
					LastSeen.DetectedBy times = LastSeen.DetectedBy.None;
					if (RadarSignal > 0)
						times |= LastSeen.DetectedBy.HasRadar;
					if (JammerSignal > 0)
						times |= LastSeen.DetectedBy.HasJammer;
					return times;
				}
			}

			public DetectedInfo(IMyEntity entity, ExtensionsRelations.Relations relate)
			{
				this.Entity = entity;
				this.Relate = relate;
				this.relateOrder = Relate.PriorityOrder();
			}

			public void SetRadar(float radarSignature, LastSeen.RadarInfo info)
			{
				this.Info = info;
				this.RadarSignature = radarSignature;
			}

			/// <summary>
			/// Sorted based on relations and signal strength.
			/// </summary>
			public int CompareTo(DetectedInfo other)
			{
				int byRelate = this.relateOrder - other.relateOrder;
				if (byRelate != 0)
					return byRelate;

				return Math.Sign(other.MaxSignal - this.MaxSignal); // descending
			}
		}

		#region Static

		// these might be moved to definition
		/// <summary>Signal strength of each decoy.</summary>
		private const float decoySignal = 5000f;
		/// <summary>Ammount each decoy adds to reported volume of ship.</summary>
		private const float decoyVolume = 10000f;

		private static Logger staticLogger = new Logger();
		private static ThreadManager myThread = new ThreadManager(background: true, threadName: "Radar");
		private static Dictionary<SerializableDefinitionId, Definition> AllDefinitions = new Dictionary<SerializableDefinitionId, Definition>();
		private static FieldInfo MyBeacon__m_light = typeof(MyBeacon).GetField("m_light", BindingFlags.Instance | BindingFlags.NonPublic);

		/// <summary>Returns true if this block is either a radar or a radar jammer.</summary>
		public static bool IsRadarOrJammer(IMyCubeBlock block)
		{
			return block.BlockDefinition.SubtypeName.ToLower().Contains("radar");
		}

		/// <summary>
		/// Gets the radar equipment with the highest power level from a LastSeen.
		/// </summary>
		/// <param name="gridLastSeen">LastSeen for the detected grid.</param>
		/// <param name="strongestEquipment">The equipment with the highest power level.</param>
		/// <param name="powerLevel">The power level of the equipment.</param>
		/// <returns>True iff a radar equipment was found.</returns>
		public static bool GetRadarEquipment(LastSeen gridLastSeen, out IMyCubeBlock strongestEquipment, out float powerLevel)
		{
			strongestEquipment = null;
			powerLevel = 0f;
			IMyCubeBlock strongest_in = null;
			float powerLevel_in = 0f;

			bool recentRadar = gridLastSeen.isRecent_Radar();
			bool recentJammer = gridLastSeen.isRecent_Jam();

			Registrar.ForEach((RadarEquipment re) => {
				if (re.CubeBlock.CubeGrid == gridLastSeen.Entity)
				{
					if (!re.IsWorking)
						return;

					float reStr = 0f;
					if (recentRadar && re.myDefinition.Radar) // PowerLevel_Radar can be > 0 even if it is not a radar
						reStr = re.PowerLevel_Radar;
					if (recentJammer && re.PowerLevel_Jammer > reStr)
						reStr = re.PowerLevel_Jammer;
					if (reStr > powerLevel_in)
					{
						powerLevel_in = reStr;
						strongest_in = re.CubeBlock;
					}
				}
			});

			strongestEquipment = strongest_in;
			powerLevel = powerLevel_in;

			return strongestEquipment != null;
		}

		private static Definition GetDefinition(IMyCubeBlock block)
		{
			Definition result;
			SerializableDefinitionId ID = block.BlockDefinition;

			if (AllDefinitions.TryGetValue(ID, out result))
			{
				staticLogger.debugLog("definition already loaded for " + ID);
				return result;
			}

			staticLogger.debugLog("creating new definition for " + ID);
			result = new Definition();

			MyCubeBlockDefinition def = block.GetCubeBlockDefinition();
			if (def == null)
				throw new NullReferenceException("no block definition found for " + block.getBestName());

			if (string.IsNullOrWhiteSpace(def.DescriptionString))
			{
				staticLogger.debugLog("No description, using defaults for " + ID, Logger.severity.WARNING);
				result.Radar = true;
				AllDefinitions.Add(ID, result);
				return result;
			}

			XML_Amendments<Definition> ammend = new XML_Amendments<Definition>(result);
			ammend.AmendAll(def.DescriptionString, true);
			result = ammend.Deserialize();

			//staticLogger.debugLog("new definition:\n" + MyAPIGateway.Utilities.SerializeToXML<Definition>(result), "GetDefinition()");
			AllDefinitions.Add(ID, result);
			return result;
		}

		#endregion

		/// <summary>The entity that has the radar.</summary>
		private readonly IMyEntity Entity;
		/// <summary>Block for comparing relations.</summary>
		private readonly IMyCubeBlock RelationsBlock;
		private readonly MyLight m_beaconLight;

		/// <summary>Entity as IMyCubeBlock</summary>
		private IMyCubeBlock CubeBlock
		{ get { return Entity as IMyCubeBlock; } }
		/// <summary>Entity as IMyTerminalBlock</summary>
		private IMyTerminalBlock TermBlock
		{ get { return CubeBlock as IMyTerminalBlock; } }
		private RelayNode m_node;

		private readonly Logger Log;
		private readonly Definition myDefinition;
		private readonly FastResourceLock myLock = new FastResourceLock();

		/// <summary>All detected entities will be added here.</summary>
		private readonly List<DetectedInfo> detectedObjects_list = new List<DetectedInfo>();
		/// <summary>Iff equipment has more than one means of detecting objects, this dictionary Keeps track of entities already found.</summary>
		private readonly Dictionary<IMyEntity, DetectedInfo> detectedObjects_hash;
		private readonly List<LastSeen> myLastSeen = new List<LastSeen>();

		private readonly SortedDictionary<float, RadarEquipment> jamming_enemy = new SortedDictionary<float, RadarEquipment>();
		private readonly SortedDictionary<float, RadarEquipment> jamming_friendly = new SortedDictionary<float, RadarEquipment>();
		private readonly Dictionary<RadarEquipment, float> beingJammedBy = new Dictionary<RadarEquipment, float>();

		/// <summary>Power level specified by the player.</summary>
		private float PowerLevel_Target = 0f;
		/// <summary>Power level achieved.</summary>
		private float PowerLevel_Current = 0f;

		private float PowerRatio_Jammer = 0f;

		private float PowerLevel_RadarEffective = 0f;

		private int deliberateJamming = 0;

		private List<IMyEntity> obstructed_ignore = new List<IMyEntity>();
		/// <summary>Only GetNearby() should fill this list.</summary>
		private List<MyEntity> m_nearbyEntities = new List<MyEntity>();
		private float m_nearbyRange;

		#region Properties

		private bool IsWorking
		{ get { return CubeBlock == null || CubeBlock.IsWorking; } }

		private bool IsRadar
		{ get { return myDefinition.Radar; } }

		private bool IsJammer
		{ get { return myDefinition.MaxTargets_Jamming > 0; } }

		private bool CanPassiveDetectRadar
		{ get { return myDefinition.PassiveDetect_Radar > 0; } }

		private bool CanPassiveDetectJammer
		{ get { return myDefinition.PassiveDetect_Jamming > 0; } }

		private float PowerRatio_Radar
		{ get { return 1f - PowerRatio_Jammer; } }

		private float PowerLevel_Jammer
		{ get { return PowerLevel_Current * PowerRatio_Jammer; } }

		private float PowerLevel_Radar
		{ get { return PowerLevel_Current * PowerRatio_Radar; } }

		#endregion

		/// <summary>
		/// Create a RadarEquipment for a block, getting the definition from the block.
		/// </summary>
		public RadarEquipment(IMyCubeBlock block)
		{
			this.Log = new Logger(block);

			this.Entity = block;
			this.RelationsBlock = block;
			this.myDefinition = GetDefinition(block);

			Registrar.Add(block, this);

			TermBlock.OnClose += CustomInfoBlock_OnClose;

			TermBlock.AppendingCustomInfo += AppendingCustomInfo;

			UpdateTargetPowerLevel();
			PowerLevel_Current = Math.Min(PowerLevel_Target, myDefinition.MaxPowerLevel);
			MyAPIGateway.Utilities.InvokeOnGameThread(UpdatePowerConsumption);

			// maybe this is a bug fix?
			MyFunctionalBlock func = (MyFunctionalBlock)block;
			func.Enabled = false;
			func.Enabled = true;

			TermBlock.UpdateCustomInfo();

			byte detectionTypes = 0;
			if (myDefinition.Radar)
				detectionTypes++;
			if (myDefinition.PassiveDetect_Jamming > 0)
				detectionTypes++;
			if (myDefinition.PassiveDetect_Radar > 0)
				detectionTypes++;
			if (detectionTypes > 1)
				detectedObjects_hash = new Dictionary<IMyEntity, DetectedInfo>();

			m_beaconLight = GetLight();

			Log.DebugLog("Radar equipment initialized, power level: " + PowerLevel_Current, Logger.severity.INFO);
		}

		/// <summary>
		/// Create a RadarEquipment for an entity, with the specified definition and an owner block.
		/// </summary>
		public RadarEquipment(IMyEntity radarEntity, Definition radarDef, IMyCubeBlock relationsBlock)
		{
			this.Log = new Logger(() => RelationsBlock.CubeGrid.DisplayName, () => RelationsBlock.DisplayNameText, () => radarEntity.ToString());

			this.Entity = radarEntity;
			this.RelationsBlock = relationsBlock;
			this.myDefinition = radarDef;

			Registrar.Add(radarEntity, this);
			PowerLevel_Current = PowerLevel_Target = this.myDefinition.MaxPowerLevel;

			byte detectionTypes = 0;
			if (myDefinition.Radar)
				detectionTypes++;
			if (myDefinition.PassiveDetect_Jamming > 0)
				detectionTypes++;
			if (myDefinition.PassiveDetect_Radar > 0)
				detectionTypes++;
			if (detectionTypes > 1)
				detectedObjects_hash = new Dictionary<IMyEntity, DetectedInfo>();

			m_beaconLight = GetLight();

			Log.DebugLog("Radar equipment initialized, power level: " + PowerLevel_Current, Logger.severity.INFO);
		}

		private void CustomInfoBlock_OnClose(IMyEntity obj)
		{
			ClearJamming();
			TermBlock.AppendingCustomInfo -= AppendingCustomInfo;
		}

		private MyLight GetLight()
		{
			MyBeacon beacon = this.Entity as MyBeacon;
			if (beacon == null)
				return null;

			return (MyLight)MyBeacon__m_light.GetValue(beacon);
		}

		public void Update100()
		{
			if (m_beaconLight != null && m_beaconLight.Intensity != 0f)
			{
				m_beaconLight.GlareIntensity = 0f;
				m_beaconLight.Intensity = 0f;
				m_beaconLight.ReflectorIntensity = 0f;
				m_beaconLight.UpdateLight();
			}

			if (myLock.TryAcquireExclusive())
			{
				if (m_node == null)
				{
					if (!Registrar.TryGetValue(Entity, out m_node))
					{
						Log.DebugLog("failed to get node");
						return;
					}
				}
				if (m_node.Storage == null)
				{
					Log.DebugLog("no storage");
					return;
				}

				// actions on main thread
				CheckCustomInfo();
				//if (myLastSeen.Count > 0)
				//{
				//	Log.DebugLog("sending to storage: " + myLastSeen.Count);
				//	m_node.Storage.Receive(myLastSeen);
				//}

				myThread.EnqueueAction(Update_OnThread);
			}
		}

		private void Update_OnThread()
		{
			try
			{
				m_nearbyEntities.Clear();
				m_nearbyRange = 0f;
				myLastSeen.Clear();
				detectedObjects_list.Clear();
				if (detectedObjects_hash != null)
					detectedObjects_hash.Clear();

				if (!IsWorking)
				{
					if (PowerLevel_Current > 0)
						PowerLevel_Current += myDefinition.PowerDecrease;
					ClearJamming();
					return;
				}

				UpdatePowerLevel();

				if (IsJammer)
					JamRadar();

				if (IsRadar)
					ActiveDetection();

				if (myDefinition.PassiveDetect_Jamming > 0)
					PassiveDetection(false);

				if (myDefinition.PassiveDetect_Radar > 0)
					PassiveDetection(true);

				if (detectedObjects_list.Count > 0)
				{
					detectedObjects_list.Sort();
					int transmit = Math.Min(detectedObjects_list.Count, myDefinition.MaxTargets_Tracking);
					for (int i = 0; i < transmit; i++)
					{
						DetectedInfo detFo = detectedObjects_list[i];
						myLastSeen.Add(new LastSeen(detFo.Entity, detFo.Times, detFo.Info));
						//Log.DebugLog("created last seen for: " + detFo.Entity.getBestName());
					}
					//Log.DebugLog("sending to storage: " + myLastSeen.Count);
					m_node.Storage.Receive(myLastSeen);
				}
			}
			catch (Exception ex)
			{
				Log.AlwaysLog("Exception: " + ex, Logger.severity.ERROR);
				CubeBlock.EnableGameThread(false);
			}
			finally
			{ myLock.ReleaseExclusive(); }
		}

		private void UpdateTargetPowerLevel()
		{
			Ingame.IMyBeacon asBeacon = CubeBlock as Ingame.IMyBeacon;
			if (asBeacon != null)
				PowerLevel_Target = asBeacon.Radius;
			else
			{
				Ingame.IMyRadioAntenna asRadio = CubeBlock as Ingame.IMyRadioAntenna;
				if (asRadio != null)
					PowerLevel_Target = asRadio.Radius;
			}
		}

		private void UpdatePowerLevel()
		{
			if (this.CubeBlock == null)
			{
				Log.DebugLog("not updating power levels, not a block");
				return;
			}

			UpdateTargetPowerLevel();

			if (PowerLevel_Current == PowerLevel_Target)
				return;

			Log.DebugLog("current power level: " + PowerLevel_Current + ", target power level: " + PowerLevel_Target, Logger.severity.TRACE);

			// cap power level
			{
				if (PowerLevel_Target > myDefinition.MaxPowerLevel)
				{
					PowerLevel_Target = myDefinition.MaxPowerLevel;
					if (MyAPIGateway.Multiplayer.IsServer)
					{
						IMyTerminalBlock TermBlock = this.TermBlock;
						IMyCubeBlock CubeBlock = this.CubeBlock;

						MyAPIGateway.Utilities.TryInvokeOnGameThread(() => {
							Log.DebugLog("Reducing target power from " + PowerLevel_Target + " to " + myDefinition.MaxPowerLevel, Logger.severity.INFO);

							// turn down slider
							Ingame.IMyBeacon asBeacon = CubeBlock as Ingame.IMyBeacon;
							if (asBeacon != null)
								asBeacon.SetValueFloat("Radius", PowerLevel_Target);
							else
							{
								Ingame.IMyRadioAntenna asRadio = CubeBlock as Ingame.IMyRadioAntenna;
								if (asRadio != null)
									asRadio.SetValueFloat("Radius", PowerLevel_Target);
							}

							UpdatePowerConsumption();
						});
					}
					else
						MyAPIGateway.Utilities.InvokeOnGameThread(UpdatePowerConsumption);
				}
			}

			// adjust current power level
			if (PowerLevel_Current < 0)
				PowerLevel_Current = 0;
			PowerLevel_Current += myDefinition.PowerIncrease;
			if (PowerLevel_Current > PowerLevel_Target)
				PowerLevel_Current = PowerLevel_Target;
			MyAPIGateway.Utilities.InvokeOnGameThread(UpdatePowerConsumption);
			Log.DebugLog("PowerLevel_Target: " + PowerLevel_Target + ", PowerLevel_Current: " + PowerLevel_Current, Logger.severity.TRACE);
		}

		private void JamRadar()
		{
			ClearJamming();

			MathHelper.Clamp(PowerRatio_Jammer, 0f, 1f);

			float effectivePowerLevel = PowerLevel_Current * myDefinition.SignalEnhance;

			if (effectivePowerLevel <= 0)
			{
				Log.DebugLog("no power for jamming");
				return;
			}

			//int allowedTargets = MathHelper.Floor(myDefinition.MaxTargets_Jamming * PowerRatio_Jammer);

			//Log.DebugLog("jamming power level: " + effectivePowerLevel + ", allowedTargets: " + allowedTargets, "JamRadar()");

			// collect targets
			Registrar.ForEach((RadarEquipment otherDevice) => {
				if (!otherDevice.IsRadar || !otherDevice.IsWorking)
					return;

				if (SignalCannotReach(otherDevice.Entity, effectivePowerLevel))
					return;

				bool notHostile = !RelationsBlock.canConsiderHostile(otherDevice.RelationsBlock);
				if (notHostile && myDefinition.JamIncidental == 0f)
				{
					Log.DebugLog("cannot jam a friendly: " + otherDevice.Entity.getBestName(), Logger.severity.TRACE);
					return;
				}

				float distance = Vector3.Distance(Entity.GetCentre(), otherDevice.Entity.GetCentre());
				float signalStrength = effectivePowerLevel - distance;

				if (signalStrength > 0)
				{
					if (notHostile)
					{
						Log.DebugLog("adding friendly: " + otherDevice.Entity.getBestName(), Logger.severity.TRACE);
						jamming_friendly.Add(signalStrength, otherDevice);
					}
					else
					{
						Log.DebugLog("adding enemy: " + otherDevice.Entity.getBestName(), Logger.severity.TRACE);
						jamming_enemy.Add(signalStrength, otherDevice);
					}
				}
			});

			// apply jamming
			if (jamming_enemy.Count == 0)
			{
				Log.DebugLog("no targets to jam", Logger.severity.TRACE);
				jamming_friendly.Clear();

				PowerRatio_Jammer = 0f;
				return;
			}

			// Up to MaximumTargets, radars will be deliberately jammed. Others will be incidentally jammed
			deliberateJamming = 0;

			foreach (var pair in jamming_enemy)
			{
				if (deliberateJamming < myDefinition.MaxTargets_Jamming)
				{
					Log.DebugLog("jamming enemy: " + pair.Value.Entity.getBestName() + ", strength: " + pair.Key, Logger.severity.TRACE);
					pair.Value.beingJammedBy.Add(this, pair.Key);
					deliberateJamming++;
				}
				else
				{
					Log.DebugLog("incidentally jamming enemy: " + pair.Value.Entity.getBestName() + ", strength: " + pair.Key * myDefinition.JamIncidental, Logger.severity.TRACE);
					pair.Value.beingJammedBy.Add(this, pair.Key * myDefinition.JamIncidental);
				}
			}

			PowerRatio_Jammer = (float)deliberateJamming / (float)myDefinition.MaxTargets_Jamming;
			Log.DebugLog("PowerRatio_Jammer: " + PowerRatio_Jammer, Logger.severity.TRACE);

			foreach (var pair in jamming_friendly)
			{
				Log.DebugLog("incidentally jamming friendly: " + pair.Value.Entity.getBestName() + ", strength: " + pair.Key * myDefinition.JamIncidental, Logger.severity.TRACE);
				pair.Value.beingJammedBy.Add(this, pair.Key * myDefinition.JamIncidental);
			}
		}

		private void ActiveDetection()
		{
			PowerLevel_RadarEffective = PowerLevel_Radar * myDefinition.SignalEnhance;

			if (PowerLevel_RadarEffective <= 0f)
			{
				Log.DebugLog("no detection possible, effective power level: " + PowerLevel_RadarEffective, Logger.severity.DEBUG);
				return;
			}

			if (beingJammedBy.Count != 0)
			{
				float jamSum = 0;
				foreach (float jamStrength in beingJammedBy.Values)
					jamSum += jamStrength;

				jamSum *= myDefinition.JammingEffect;

				if (jamSum > 0f)
					PowerLevel_RadarEffective -= jamSum;

				Log.DebugLog("being jammed by " + beingJammedBy.Count + " jammers, power available: " + PowerLevel_Radar + ", effective power level: " + PowerLevel_RadarEffective, Logger.severity.TRACE);

				if (PowerLevel_RadarEffective <= 0f)
				{
					Log.DebugLog("no detection possible, effective power level: " + PowerLevel_RadarEffective, Logger.severity.DEBUG);
					PowerLevel_RadarEffective = 0f;
					return;
				}
			}
			//else
			//	Log.DebugLog("not being jammed, power available: " + PowerLevel_Radar + ", effective power level: " + PowerLevel_RadarEffective, Logger.severity.TRACE);

			GetNearby(PowerLevel_RadarEffective);

			foreach (IMyEntity entity in m_nearbyEntities)
			{
				if (entity.MarkedForClose)
					continue;

				if (m_node.Storage.VeryRecentRadarInfo(entity.EntityId))
					continue;

				float volume, reflectivity;
				if (entity.IsMissile())
				{
					GuidedMissile guided;
					if (!Registrar.TryGetValue(entity, out guided))
						continue;
					if (SignalCannotReach(entity, PowerLevel_RadarEffective))
						continue;
					volume = LastSeen.RadarInfo.GetVolume(entity);
					reflectivity = guided.RadarReflectivity;
					Log.DebugLog("missile volume: " + volume + ", reflectivity: " + reflectivity);
				}
				else if (entity is IMyCubeGrid)
				{
					if (!entity.Save)
						continue; 
					if (SignalCannotReach(entity, PowerLevel_RadarEffective))
						continue;
					volume = LastSeen.RadarInfo.GetVolume(entity);
					reflectivity = (volume + myDefinition.Reflect_A) / (volume + myDefinition.Reflect_B);
				}
				else if (entity is IMyCharacter)
				{
					if (SignalCannotReach(entity, PowerLevel_RadarEffective))
						continue;
					volume = LastSeen.RadarInfo.GetVolume(entity);
					reflectivity = (volume + myDefinition.Reflect_A) / (volume + myDefinition.Reflect_B);
				}
				else
					continue;

				float distance = Vector3.Distance(Entity.GetCentre(), entity.GetCentre());
				float radarSignature = (PowerLevel_RadarEffective - distance) * reflectivity - distance;
				int decoys = WorkingDecoys(entity);
				radarSignature += decoySignal * decoys;

				//Log.DebugLog("name: " + entity.getBestName() + ", volume: " + volume + ", reflectivity: " + reflectivity + ", distance: " + distance
				//	+ ", radar signature: " + radarSignature + ", decoys: " + decoys, Logger.severity.TRACE);

				if (radarSignature > 0)
				{
					DetectedInfo detFo = new DetectedInfo(entity, RelationsBlock.getRelationsTo(entity));
					detFo.SetRadar(radarSignature, new LastSeen.RadarInfo(volume + decoyVolume * decoys));
					detectedObjects_list.Add(detFo);
					if (detectedObjects_hash != null)
						detectedObjects_hash.Add(entity, detFo);
				}
			}
		}

		private void PassiveDetection(bool radar)
		{
			float detectionThreshold;
			if (radar)
			{
				if (!CanPassiveDetectRadar)
					return;
				detectionThreshold = myDefinition.PassiveDetect_Radar;
			}
			else
			{
				if (!CanPassiveDetectJammer)
					return;
				detectionThreshold = myDefinition.PassiveDetect_Jamming;
			}

			Registrar.ForEach((RadarEquipment otherDevice) => {
				if (!otherDevice.IsWorking)
					return;

				IMyEntity otherEntity = otherDevice.Entity.Hierarchy.GetTopMostParent().Entity;
				if (m_node.Storage.VeryRecentRadarInfo(otherEntity.EntityId))
					return;

				float otherPowerLevel = radar ? otherDevice.PowerLevel_Radar : otherDevice.PowerLevel_Jammer;
				if (otherPowerLevel <= 0)
					return;
				otherPowerLevel *= myDefinition.SignalEnhance;

				if (SignalCannotReach(otherDevice.Entity, otherPowerLevel))
					return;

				float distance = Vector3.Distance(Entity.GetCentre(), otherDevice.Entity.GetCentre());
				float signalStrength = otherPowerLevel - distance - detectionThreshold;

				signalStrength += decoySignal * WorkingDecoys(otherDevice);

				if (signalStrength > 0)
				{
					Log.DebugLog("radar signal seen: " + otherDevice.Entity.getBestName(), Logger.severity.TRACE);

					DetectedInfo detFo;
					if (detectedObjects_hash == null || !detectedObjects_hash.TryGetValue(otherEntity, out detFo))
					{
						detFo = new DetectedInfo(otherEntity, RelationsBlock.getRelationsTo(otherDevice.RelationsBlock));
						detectedObjects_list.Add(detFo);
						if (detectedObjects_hash != null)
							detectedObjects_hash.Add(otherEntity, detFo);
					}
					if (radar)
						detFo.RadarSignal = signalStrength;
					else
						detFo.JammerSignal = signalStrength;
				}
			});
		}

		#region Signal Cannot Reach

		private bool SignalCannotReach(IMyEntity target, float compareDist)
		{
			//Log.DebugLog("target: " + target.getBestName() + ", really far: " + ReallyFar(target.GetCentre(), compareDist) + ", compareDist: " + compareDist + ", unacceptable angle: " + UnacceptableAngle(target) + 
			//	", obstructed: " + Obstructed(target), "SignalCannotReach()");
			return ReallyFar(target.GetCentre(), compareDist) || UnacceptableAngle(target) || Obstructed(target, compareDist);
		}

		private bool ReallyFar(Vector3D target, float compareTo)
		{
			Vector3D position = Entity.GetCentre();
			return Math.Abs(position.X - target.X) > compareTo
					|| Math.Abs(position.Y - target.Y) > compareTo
					|| Math.Abs(position.Z - target.Z) > compareTo;
		}

		/// <summary>
		/// Determines if the position does not fall within the angle limits of the radar.
		/// </summary>
		private bool UnacceptableAngle(IMyEntity target)
		{
			if (!myDefinition.EnforceAngle)
				return false;

			MatrixD Transform = Entity.WorldMatrixNormalizedInv.GetOrientation();
			Vector3 directionToTarget = Vector3.Transform(target.GetCentre() - Entity.GetCentre(), Transform);
			directionToTarget.Normalize();
			//Log.DebugLog("my position: " + Entity.GetCentre() + ", target: " + target.DisplayName + ", target position: " + target.GetCentre()
			//	+ ", displacement: " + (target.GetCentre() - Entity.GetCentre()) + ", direction: " + directionToTarget, "UnacceptableAngle()");

			float azimuth, elevation;
			Vector3.GetAzimuthAndElevation(directionToTarget, out azimuth, out elevation);

			//Log.DebugLog("azimuth: " + azimuth + ", min: " + myDefinition.MinAzimuth + ", max: " + myDefinition.MaxAzimuth
			//	+ ", elevation: " + elevation + ", min: " + myDefinition.MinElevation + ", max: " + myDefinition.MaxElevation, "UnacceptableAngle()");
			//Log.DebugLog("azimuth below: " + (azimuth < myDefinition.MinAzimuth) + ", azimuth above: " + (azimuth > myDefinition.MaxAzimuth)
			//	+ ", elevation below: " + (elevation < myDefinition.MinElevation) + ", elevation above: " + (elevation > myDefinition.MaxElevation), "UnacceptableAngle()");
			return azimuth < myDefinition.MinAzimuth || azimuth > myDefinition.MaxAzimuth || elevation < myDefinition.MinElevation || elevation > myDefinition.MaxElevation;
		}

		/// <summary>
		/// Determines if there is an obstruction between radar and target.
		/// </summary>
		private bool Obstructed(IMyEntity target, float range)
		{
			//Log.DebugLog("me: " + Entity.getBestName() + ", target: " + target.getBestName(), "Obstructed()");

			if (!myDefinition.LineOfSight)
				return false;

			GetNearby(range);

			obstructed_ignore.Clear();
			obstructed_ignore.Add(Entity);
			obstructed_ignore.Add(target);

			return RayCast.Obstructed(new LineD(Entity.GetCentre(), target.GetCentre()), m_nearbyEntities, obstructed_ignore);
		}

		#endregion

		private int WorkingDecoys(IMyEntity target)
		{
			IMyCubeGrid grid = target as IMyCubeGrid;
			if (grid == null || RelationsBlock.canConsiderFriendly(grid))
				return 0;

			CubeGridCache cache = CubeGridCache.GetFor(grid);
			if (cache == null)
				return 0;
			return cache.CountByType(typeof(MyObjectBuilder_Decoy), block => block.IsWorking);
		}

		private int WorkingDecoys(RadarEquipment otherEquip)
		{
			if (otherEquip.CubeBlock == null)
				return 0;

			return WorkingDecoys(otherEquip.CubeBlock.CubeGrid);
		}

		private void ClearJamming()
		{
			foreach (RadarEquipment jammed in jamming_enemy.Values)
				jammed.beingJammedBy.Remove(this);

			foreach (RadarEquipment jammed in jamming_friendly.Values)
				jammed.beingJammedBy.Remove(this);

			jamming_enemy.Clear();
			jamming_friendly.Clear();
			deliberateJamming = 0;
		}

		#region Custom Info

		private float previous_PowerLevel_Current;
		private int previous_deliberateJamming;
		private float previous_PowerLevel_Radar;
		private float previous_PowerLevel_RadarEffective;
		private int previous_beingJammedBy_Count;
		private int previous_detectedObjects_Count;

		private void CheckCustomInfo()
		{
			if (TermBlock == null)
				return;

			bool needToRefresh = false;

			if (IsJammer)
				if (PowerLevel_Current != previous_PowerLevel_Current)
				{
					needToRefresh = true;
					previous_PowerLevel_Current = PowerLevel_Current;
				}

			if (deliberateJamming != previous_deliberateJamming)
			{
				needToRefresh = true;
				previous_deliberateJamming = deliberateJamming;
			}

			if (IsRadar)
			{
				if (PowerLevel_Radar != previous_PowerLevel_Radar)
				{
					needToRefresh = true;
					previous_PowerLevel_Radar = PowerLevel_Radar;
				}
				if (PowerLevel_RadarEffective != previous_PowerLevel_RadarEffective)
				{
					needToRefresh = true;
					previous_PowerLevel_RadarEffective = PowerLevel_RadarEffective;
				}
			}

			if (beingJammedBy.Count != previous_beingJammedBy_Count)
			{
				needToRefresh = true;
				previous_beingJammedBy_Count = beingJammedBy.Count;
			}

			if (myLastSeen.Count != previous_detectedObjects_Count)
			{
				needToRefresh = true;
				previous_detectedObjects_Count = myLastSeen.Count;
			}

			if (needToRefresh)
				TermBlock.UpdateCustomInfo();
		}

		private void AppendingCustomInfo(IMyTerminalBlock block, StringBuilder customInfo)
		{
			customInfo.AppendLine();

			// jammer

			if (IsJammer)
				if (PowerLevel_Current > 0)
				{
					customInfo.AppendLine("Jammer power level: " + (int)PowerLevel_Current);
					customInfo.AppendLine("Maximum jamming range: " + (int)(PowerLevel_Current * myDefinition.SignalEnhance));
				}

			if (deliberateJamming > 0)
			{
				customInfo.AppendLine("Jamming " + deliberateJamming + " of " + myDefinition.MaxTargets_Jamming + " radars");
			}

			// radar

			if (IsRadar)
				if (PowerLevel_Radar > 0)
				{
					customInfo.AppendLine("Radar power level: " + (int)PowerLevel_Radar);
					customInfo.AppendLine("Maximum radar range: " + (int)(PowerLevel_RadarEffective / 2));
				}

			if (beingJammedBy.Count > 0)
			{
				customInfo.Append("Interference from " + beingJammedBy.Count + " source");
				if (beingJammedBy.Count != 1)
					customInfo.Append('s');
				customInfo.AppendLine();
			}

			// detected

			if (myLastSeen.Count > 0)
			{
				customInfo.AppendLine("Detecting " + myLastSeen.Count + " of " + myDefinition.MaxTargets_Tracking + " objects");
			}

			customInfo.AppendLine("Power use: " + PrettySI.makePretty(((MyCubeBlock)block).ResourceSink.RequiredInputByType(new MyDefinitionId(typeof(MyObjectBuilder_GasProperties), "Electricity")) * 1e6f) + 'W');
		}

		#endregion

		private void UpdatePowerConsumption()
		{
			// if required input is zero, the block will not be consider working by SE
			float powerConsumption = Math.Max(PowerLevel_Target, 1f) * 1e-4f;
			((MyCubeBlock)CubeBlock).ResourceSink.SetRequiredInputByType(Globals.Electricity, powerConsumption);
		}

		private void GetNearby(float range)
		{
			if (m_nearbyRange < range)
			{
				range++;
				m_nearbyEntities.Clear();
				BoundingSphereD nearby = new BoundingSphereD(Entity.GetPosition(), range);
				MyGamePruningStructure.GetAllTopMostEntitiesInSphere(ref nearby, m_nearbyEntities);
				m_nearbyRange = range;
			}
		}

	}
}
