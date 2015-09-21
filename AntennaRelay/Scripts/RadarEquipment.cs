using System;
using System.Collections.Generic;
using System.Text;
using Sandbox.Common.ObjectBuilders;
using Sandbox.Definitions;
using Sandbox.ModAPI;
using Sandbox.ModAPI.Interfaces;
using VRage.ModAPI;
using VRageMath;
using Ingame = Sandbox.ModAPI.Ingame;

namespace Rynchodon.AntennaRelay
{
	/// <summary>
	/// <para>Class for Radar and Radar Jammers</para>
	/// </summary>
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

			/// <summary>The maximum number of targets that can be detected (active + passive).</summary>
			public int MaxTargets_Tracking = 10;

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
			/// <para>Multiplier for the distance a signal carries. For passive collection, value from receiving radar will be used.</para>
			/// </summary>
			public float SignalEnhance = 1;

			/// <summary>Not implemented. How well the signal can penetrate a solid object.</summary>
			public float Penetration = 0;

			/// <summary>Reduces the effective strength of electronic jamming by this amount.</summary>
			public float JammingResistance = 0;

			// not sure if this makes any senese
			///// <summary>Not implemented. Resistance to mechanical jamming.</summary>
			//public float JammingResistance_Mechanical = 0;

			/// <summary>How much of the jamming effect is applied to friendly radar and radar beyond the MaximumTargets limit.</summary>
			public float JamIncidental = 0.1f;

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

		#region Static

		// these might be moved to definition
		private const float decoyStrength = 0.1f;
		//private const float jammerStep = 1f / 4f;

		private static readonly Logger staticLogger = new Logger("N/A", "RadarEquipment");
		private static readonly ThreadManager myThread = new ThreadManager();
		private static readonly Dictionary<string, Definition> AllDefinitions = new Dictionary<string, Definition>();
		private static readonly List<RadarEquipment> AllRadarAndJam = new List<RadarEquipment>();

		/// <summary>Returns true if this block is either a radar or a radar jammer.</summary>
		public static bool IsRadarOrJammer(IMyCubeBlock block)
		{
			return block.BlockDefinition.SubtypeName.ToLower().Contains("radar");
		}

		private static Definition GetDefinition(IMyCubeBlock block)
		{
			Definition result;
			string ID = block.BlockDefinition.ToString();

			if (AllDefinitions.TryGetValue(ID, out result))
			{
				staticLogger.debugLog("definition already loaded for " + ID, "GetDefinition()");
				return result;
			}

			staticLogger.debugLog("creating new definition for " + ID, "GetDefinition()");
			result = new Definition();

			MyCubeBlockDefinition def = DefinitionCache.GetCubeBlockDefinition(block);
			if (def == null)
				throw new NullReferenceException("no block definition found for " + block.getBestName());

			if (string.IsNullOrWhiteSpace(def.DescriptionString))
			{
				staticLogger.debugLog("no description in data file for " + ID, "GetDefinition()", Logger.severity.INFO);
				result.Radar = true;
				AllDefinitions.Add(ID, result);
				return result;
			}

			// parse description
			string[] properties = def.DescriptionString.Split(new char[] { }, StringSplitOptions.RemoveEmptyEntries);
			foreach (string prop in properties)
			{
				if (prop == "Radar")
				{
					result.Radar = true;
					continue;
				}

				string[] propValue = prop.Split('=');
				if (propValue.Length != 2)
				{
					staticLogger.alwaysLog("for " + block.BlockDefinition.ToString() + ", incorrect format for property: \"" + prop + '"', "GetDefinition()", Logger.severity.WARNING);
					continue;
				}

				int maxTargets;
				switch (propValue[0])
				{
					case "MaxTargets_Tracking":
						if (int.TryParse(propValue[1], out maxTargets))
							result.MaxTargets_Tracking = maxTargets;
						else
							staticLogger.alwaysLog("for " + block.BlockDefinition.ToString() + ".MaxTargets_Tracking, not an int: \"" + propValue[1] + '"', "GetDefinition()", Logger.severity.WARNING);
						continue;
					case "MaxTargets_Jamming":
						if (int.TryParse(propValue[1], out maxTargets))
							result.MaxTargets_Jamming = maxTargets;
						else
							staticLogger.alwaysLog("for " + block.BlockDefinition.ToString() + ".MaxTargets_Jamming, not an int: \"" + propValue[1] + '"', "GetDefinition()", Logger.severity.WARNING);
						continue;
				}

				// remaining properties are floats, so test first
				float value;
				if (!float.TryParse(propValue[1], out value))
				{
					staticLogger.alwaysLog("for " + block.BlockDefinition.ToString() + ", not a float: \"" + propValue[1] + '"', "GetDefinition()", Logger.severity.WARNING);
					continue;
				}

				switch (propValue[0])
				{
					case "PassiveDetect_Jamming":
						result.PassiveDetect_Jamming = value;
						continue;
					case "PassiveDetect_Radar":
						result.PassiveDetect_Radar = value;
						continue;
					case "MaxPowerLevel":
						result.MaxPowerLevel = value;
						continue;
					case "PowerIncrease":
						result.PowerIncrease = value;
						continue;
					case "PowerDecrease":
						result.PowerDecrease = value;
						continue;
					case "Reflect_A":
						result.Reflect_A = value;
						continue;
					case "Reflect_B":
						result.Reflect_B = value;
						continue;
					case "SignalEnhance":
						result.SignalEnhance = value;
						continue;
					case "Penetration":
						result.Penetration = value;
						continue;
					case "JammingResistance":
						result.JammingResistance = value;
						continue;
					case "JamIncidental":
						result.JamIncidental = value;
						continue;
					case "MinAzimuth":
						result.MinAzimuth = value;
						continue;
					case "MaxAzimuth":
						result.MaxAzimuth = value;
						continue;
					case "MinElevation":
						result.MinElevation = value;
						continue;
					case "MaxElevation":
						result.MaxElevation = value;
						continue;
					default:
						staticLogger.alwaysLog("for " + block.BlockDefinition.ToString() + ", failed to match to a property: \"" + propValue[0] + '"', "GetDefinition()", Logger.severity.WARNING);
						continue;
				}
			}

			if (!result.Radar && result.MaxTargets_Jamming == 0 && result.PassiveDetect_Jamming == 0 && result.PassiveDetect_Radar == 0)
			{
				staticLogger.debugLog("no detection or jamming set, assuming block is a radar", "GetDefinition()", Logger.severity.WARNING);
				result.Radar = true;
			}

			staticLogger.debugLog("parsed description for " + ID, "GetDefinition()", Logger.severity.INFO);
			staticLogger.debugLog("serialized definition:\n" + MyAPIGateway.Utilities.SerializeToXML(result), "GetDefinition()", Logger.severity.TRACE);
			AllDefinitions.Add(ID, result);
			return result;
		}

		#endregion

		private enum PowerUse : byte { None, Share, All }

		private readonly IMyEntity Entity;
		private readonly IMyCubeBlock CubeBlock;
		private readonly IMyTerminalBlock TermBlock;
		private readonly IMyCubeBlock Owner;

		private readonly Logger myLogger;
		private readonly Definition myDefinition;
		private readonly FastResourceLock myLock = new FastResourceLock();

		/// <summary>size of radar signature/signal and object</summary>
		private readonly SortedDictionary<float, LastSeen> detectedObjects = new SortedDictionary<float, LastSeen>();
		private readonly SortedDictionary<float, RadarEquipment> jamming_enemy = new SortedDictionary<float, RadarEquipment>();
		private readonly SortedDictionary<float, RadarEquipment> jamming_friendly = new SortedDictionary<float, RadarEquipment>();
		private readonly Dictionary<RadarEquipment, float> beingJammedBy = new Dictionary<RadarEquipment, float>();

		/// <summary>Power level specified by the player.</summary>
		private float PowerLevel_Target = 0f;
		/// <summary>Power level achieved.</summary>
		private float PowerLevel_Current = 0f;

		private float PowerRatio_Jammer = 0f;

		private int deliberateJamming = 0;

		#region Properties and Trivial Functions

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

		private bool CanConsiderFriendly(RadarEquipment other)
		{
			IMyCubeBlock block1 = CubeBlock ?? Owner;
			IMyCubeBlock block2 = other.CubeBlock ?? other.Owner;

			return block1.canConsiderFriendly(block2);
		}

		private string GetInstructions()
		{
			if (CubeBlock == null)
				return null;
			return CubeBlock.getInstructions();
		}

		#endregion

		public RadarEquipment(IMyCubeBlock block)
		{
			this.myLogger = new Logger("RadarEquipment", block);

			this.Entity = block;
			this.CubeBlock = block;
			this.TermBlock = block as IMyTerminalBlock;

			this.myDefinition = GetDefinition(block);

			AllRadarAndJam.Add(this);
			CubeBlock.OnClose += CubeBlock_OnClose;

			TermBlock.AppendingCustomInfo += AppendingCustomInfo;

			UpdateTargetPowerLevel();
			PowerLevel_Current = PowerLevel_Target;

			myLogger.debugLog("Radar equipment initialized, power level: " + PowerLevel_Current, "RadarEquipment()", Logger.severity.INFO);
		}

		/// <summary>
		/// For missiles
		/// </summary>
		public RadarEquipment(IMyEntity entity, IMyCubeBlock owner)
		{
			this.myLogger = new Logger("RadarEquipment", () => { return owner.CubeGrid.DisplayName; }, () => { return owner.DisplayNameText; }, () => { return entity.getBestName(); });

			this.Entity = entity;
			this.Owner = owner;

			// TODO: needs definition

			AllRadarAndJam.Add(this);

			Entity.OnClose += Entity_OnClose;

			// TODO: needs PowerLevel_Current

			myLogger.debugLog("Radar equipment initialized, power level: " + PowerLevel_Current, "RadarEquipment()", Logger.severity.INFO);
		}

		private void CubeBlock_OnClose(IMyEntity obj)
		{
			AllRadarAndJam.Remove(this);
			ClearJamming();
			TermBlock.AppendingCustomInfo -= AppendingCustomInfo;
		}

		private void Entity_OnClose(IMyEntity obj)
		{
			AllRadarAndJam.Remove(this);
			ClearJamming();
		}

		public void Update100()
		{
			if (myLock.TryAcquireExclusive())
			{
				// actions on main thread
				CheckCustomInfo();
				if (detectedObjects.Count > 0 && CubeBlock != null)
					Receiver.sendToAttached(CubeBlock, detectedObjects.Values);

				myThread.EnqueueAction(Update_OnThread);
			}
		}

		private void Update_OnThread()
		{
			try
			{
				if (!IsWorking)
				{
					if (PowerLevel_Current > 0)
						PowerLevel_Current += myDefinition.PowerDecrease;
					detectedObjects.Clear();
					ClearJamming();
					return;
				}

				UpdatePowerLevel();
				detectedObjects.Clear();

				if (IsJammer)
					JamRadar();

				if (IsRadar)
					ActiveDetection();

				if (myDefinition.PassiveDetect_Jamming > 0)
					PassiveDetection(false);

				if (myDefinition.PassiveDetect_Radar > 0)
					PassiveDetection(true);
			}
			catch (Exception ex)
			{ myLogger.alwaysLog("Exception: " + ex, "Update_OnThread()", Logger.severity.ERROR); }
			finally
			{ myLock.ReleaseExclusive(); }
		}

		private void UpdateTargetPowerLevel()
		{
			// get target power level
			{
				if (CubeBlock == null)
				{
					myLogger.debugLog("not updating target, no block", "UpdateTargetPowerLevel()");
					return;
				}

				Ingame.IMyBeacon asBeacon;
				Ingame.IMyRadioAntenna asRadio;

				// from name
				string instructions = GetInstructions();
				if (instructions == null || !float.TryParse(instructions, out PowerLevel_Target))
				{
					// from block
					asBeacon = CubeBlock as Ingame.IMyBeacon;
					if (asBeacon != null)
						PowerLevel_Target = asBeacon.Radius;
					else
					{
						asRadio = CubeBlock as Ingame.IMyRadioAntenna;
						if (asRadio != null)
							PowerLevel_Target = asRadio.Radius;
					}
				}
				else
				{
					myLogger.debugLog("making slider match name", "UpdateTargetPowerLevel()");
					// make sure slider matches name
					asBeacon = CubeBlock as Ingame.IMyBeacon;
					if (asBeacon != null)
					{
						myLogger.debugLog("PowerLevel_Target: " + PowerLevel_Target + ", asBeacon.Radius: " + asBeacon.Radius, "UpdateTargetPowerLevel()");
						if (PowerLevel_Target != asBeacon.Radius)
							asBeacon.SetValueFloat("Radius", PowerLevel_Target);
					}
					else
					{
						asRadio = CubeBlock as Ingame.IMyRadioAntenna;
						if (asRadio != null && PowerLevel_Target != asRadio.Radius)
						{
							myLogger.debugLog("PowerLevel_Target: " + PowerLevel_Target + ", asRadio.Radius: " + asRadio.Radius, "UpdateTargetPowerLevel()");
							asRadio.SetValueFloat("Radius", PowerLevel_Target);
						}
					}
				}
			}
		}

		private void UpdatePowerLevel()
		{
			UpdateTargetPowerLevel();

			if (PowerLevel_Current == PowerLevel_Target)
			{
				myLogger.debugLog("at target power level: " + PowerLevel_Current, "UpdatePowerLevel()", Logger.severity.TRACE);
				return;
			}

			// cap power level
			{
				if (PowerLevel_Target > myDefinition.MaxPowerLevel)
				{
					myLogger.debugLog("Reducing target power from " + PowerLevel_Target + " to " + myDefinition.MaxPowerLevel, "UpdatePowerLevel()", Logger.severity.INFO);
					PowerLevel_Target = myDefinition.MaxPowerLevel;

					string instructions = GetInstructions();
					if (instructions != null)
						TermBlock.SetCustomName(CubeBlock.DisplayNameText.Replace(instructions, myDefinition.MaxPowerLevel.ToString()));

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
				}
			}

			// adjust current power level
			if (PowerLevel_Current < 0)
				PowerLevel_Current = 0;
			PowerLevel_Current += myDefinition.PowerIncrease;
			if (PowerLevel_Current > PowerLevel_Target)
				PowerLevel_Current = PowerLevel_Target;
			myLogger.debugLog("PowerLevel_Target: " + PowerLevel_Target + ", PowerLevel_Current: " + PowerLevel_Current, "UpdatePowerLevel()", Logger.severity.TRACE);
		}

		private void JamRadar()
		{
			ClearJamming();

			MathHelper.Clamp(PowerRatio_Jammer, 0f, 1f);

			//float effectivePowerLevel
			//	= PowerRatio_Jammer > 0
			//	? PowerLevel_Current * PowerRatio_Jammer
			//	// the jammer should search for targets even while inactive
			//	// the search will happen at the level it would be stepped up to
			//	: IsRadar
			//		? PowerLevel_Current * jammerStep
			//		: PowerLevel_Current;

			float effectivePowerLevel = PowerLevel_Current;

			if (effectivePowerLevel <= 0)
			{
				myLogger.debugLog("no power for jamming", "JamRadar()");
				return;
			}

			int allowedTargets = MathHelper.Floor(myDefinition.MaxTargets_Jamming * PowerRatio_Jammer);

			myLogger.debugLog("jamming power level: " + effectivePowerLevel + ", allowedTargets: " + allowedTargets, "JamRadar()");

			// collect targets
			foreach (RadarEquipment otherDevice in AllRadarAndJam)
			{
				if (!otherDevice.IsRadar || !otherDevice.IsWorking)
					continue;

				if (SignalCannotReach(otherDevice.Entity, effectivePowerLevel * myDefinition.SignalEnhance))
					continue;

				bool friendly = CanConsiderFriendly(otherDevice);
				if (friendly && myDefinition.JamIncidental == 0f)
				{
					myLogger.debugLog("cannot jam a friendly: " + otherDevice.Entity.getBestName(), "JamRadar()", Logger.severity.TRACE);
					continue;
				}

				float distance = Vector3.Distance(Entity.GetPosition(), otherDevice.Entity.GetPosition()) / myDefinition.SignalEnhance;
				float signalStrength = effectivePowerLevel - distance;

				if (signalStrength > 0)
				{
					if (friendly)
					{
						myLogger.debugLog("adding friendly: " + otherDevice.Entity.getBestName(), "JamRadar()", Logger.severity.TRACE);
						jamming_friendly.Add(signalStrength, otherDevice);
					}
					else
					{
						myLogger.debugLog("adding enemy: " + otherDevice.Entity.getBestName(), "JamRadar()", Logger.severity.TRACE);
						jamming_enemy.Add(signalStrength, otherDevice);
					}
				}
			}

			// apply jamming
			if (jamming_enemy.Count == 0)
			{
				myLogger.debugLog("no targets to jam", "JamRadar()", Logger.severity.TRACE);
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
					myLogger.debugLog("jamming enemy: " + pair.Value.Entity.getBestName() + ", strength: " + pair.Key, "JamRadar()", Logger.severity.TRACE);
					pair.Value.beingJammedBy.Add(this, pair.Key);
					deliberateJamming++;
				}
				else
				{
					myLogger.debugLog("incidentally jamming enemy: " + pair.Value.Entity.getBestName() + ", strength: " + pair.Key * myDefinition.JamIncidental, "JamRadar()", Logger.severity.TRACE);
					pair.Value.beingJammedBy.Add(this, pair.Key * myDefinition.JamIncidental);
				}
			}

			PowerRatio_Jammer = (float)deliberateJamming / (float)myDefinition.MaxTargets_Jamming;
			myLogger.debugLog("PowerRatio_Jammer: " + PowerRatio_Jammer, "JamRadar()", Logger.severity.TRACE);

			//if (PowerRatio_Jammer == 0f || (deliberateJamming == myDefinition.MaxTargets_Jamming && PowerRatio_Jammer < 1f))
			//{
			//	if (IsRadar)
			//		PowerRatio_Jammer += jammerStep;
			//	else
			//		PowerRatio_Jammer = 1f;
			//	myLogger.debugLog("increased PowerRatio_Jammer to " + PowerRatio_Jammer, "JamRadar()", Logger.severity.DEBUG);
			//}

			foreach (var pair in jamming_friendly)
			{
				myLogger.debugLog("incidentally jamming friendly: " + pair.Value.Entity.getBestName() + ", strength: " + pair.Key * myDefinition.JamIncidental, "JamRadar()", Logger.severity.TRACE);
				pair.Value.beingJammedBy.Add(this, pair.Key * myDefinition.JamIncidental);
			}
		}

		private void ActiveDetection()
		{
			float effectivePowerLevel = PowerLevel_Radar;

			if (effectivePowerLevel <= 0)
			{
				myLogger.debugLog("no detection possible, effective power level: " + effectivePowerLevel, "ActiveDetection()", Logger.severity.DEBUG);
				return;
			}

			if (beingJammedBy.Count != 0)
			{
				float jamSum = 0;
				foreach (float jamStrength in beingJammedBy.Values)
					jamSum += jamStrength;

				jamSum -= myDefinition.JammingResistance;

				if (jamSum > 0)
					effectivePowerLevel -= jamSum;

				myLogger.debugLog("being jammed by " + beingJammedBy.Count + " jammers, power available: " + PowerLevel_Radar + ", effective power level: " + effectivePowerLevel, "ActiveDetection()", Logger.severity.TRACE);

				if (effectivePowerLevel <= 0)
				{
					myLogger.debugLog("no detection possible, effective power level: " + effectivePowerLevel, "ActiveDetection()", Logger.severity.DEBUG);
					return;
				}
			}

			HashSet<IMyEntity> allGrids = new HashSet<IMyEntity>();
			MyAPIGateway.Entities.GetEntities_Safe(allGrids, (entity) => { return entity is IMyCubeGrid; });
			foreach (IMyCubeGrid otherGrid in allGrids)
			{
				if (!otherGrid.Save)
					continue;

				if (SignalCannotReach(otherGrid, effectivePowerLevel * myDefinition.SignalEnhance))
					continue;

				float volume = otherGrid.LocalAABB.Volume();
				float reflectivity = (volume + myDefinition.Reflect_A) / (volume + myDefinition.Reflect_B);

				int workingDecoys = CubeGridCache.GetFor(otherGrid).CountByType(typeof(MyObjectBuilder_Decoy), (block) => { return block.IsWorking; });
				reflectivity += decoyStrength * workingDecoys;

				float distance = Vector3.Distance(Entity.GetPosition(), otherGrid.GetPosition()) / myDefinition.SignalEnhance;
				float radarSignature = (effectivePowerLevel - distance) * reflectivity - distance;

				if (radarSignature > 0)
				{
					myLogger.debugLog("object detected: " + otherGrid.getBestName(), "ActiveDetection()", Logger.severity.TRACE);
					AddDetectedObject(radarSignature, new LastSeen(otherGrid, false, new RadarInfo(volume)));
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

			foreach (RadarEquipment otherDevice in AllRadarAndJam)
			{
				if (!otherDevice.IsWorking)
					continue;

				float otherPowerLevel = radar ? otherDevice.PowerLevel_Radar : otherDevice.PowerLevel_Jammer;
				if (otherPowerLevel <= 0)
					continue;

				if (SignalCannotReach(otherDevice.Entity, detectionThreshold * myDefinition.SignalEnhance))
					continue;

				float distance = Vector3.Distance(Entity.GetPosition(), otherDevice.Entity.GetPosition()) / myDefinition.SignalEnhance;
				float signalStrength = otherPowerLevel - distance - detectionThreshold;

				int workingDecoys = otherDevice.CubeBlock == null ? 0
					: CubeGridCache.GetFor(otherDevice.CubeBlock.CubeGrid).CountByType(typeof(MyObjectBuilder_Decoy), (block) => { return block.IsWorking; });
				signalStrength += decoyStrength * workingDecoys;

				if (signalStrength > 0)
				{
					myLogger.debugLog("radar signal seen: " + otherDevice.Entity.getBestName(), "PassiveDetection()", Logger.severity.TRACE);
					AddDetectedObject(signalStrength, new LastSeen(otherDevice.Entity, true));
				}
			}
		}

		#region Signal Cannot Reach

		private bool SignalCannotReach(IMyEntity target, float compareDist)
		{
			return ReallyFar(target.GetPosition(), compareDist) || UnacceptableAngle(target) || Obstructed(target);
		}

		private bool ReallyFar(Vector3D target, float compareTo)
		{
			Vector3D position = Entity.GetPosition();
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

			MatrixD Transform = Entity.WorldMatrixNormalizedInv.RotationOnly();
			Vector3 directionToTarget = Vector3.Transform(target.GetPosition() - Entity.GetPosition(), Transform);
			directionToTarget.Normalize();

			float azimuth, elevation;
			Vector3.GetAzimuthAndElevation(directionToTarget, out azimuth, out elevation);

			return azimuth < myDefinition.MinAzimuth || azimuth > myDefinition.MaxAzimuth || elevation < myDefinition.MinElevation || elevation > myDefinition.MaxElevation;
		}

		/// <summary>
		/// Determines if there is an obstruction between radar and target.
		/// </summary>
		private bool Obstructed(IMyEntity target)
		{
			List<Line> lines = new List<Line>();
			lines.Add(new Line(Entity.GetPosition(), target.GetPosition(), false));

			HashSet<IMyEntity> entities = new HashSet<IMyEntity>();
			MyAPIGateway.Entities.GetEntities_Safe(entities, (entity) => { return entity is IMyCharacter || entity is IMyCubeGrid; });

			List<IMyEntity> ignore = new List<IMyEntity>();
			ignore.Add(Entity);
			ignore.Add(target);

			object obstruction;
			return RayCast.Obstructed(lines, entities, ignore, out obstruction);
		}

		#endregion

		private void AddDetectedObject(float signalStrength, LastSeen obj)
		{
			detectedObjects.AddIfBetter(signalStrength, obj, (int)myDefinition.MaxTargets_Tracking);
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
				if (PowerLevel_Radar != previous_PowerLevel_Radar)
				{
					needToRefresh = true;
					previous_PowerLevel_Radar = PowerLevel_Radar;
				}

			if (beingJammedBy.Count != previous_beingJammedBy_Count)
			{
				needToRefresh = true;
				previous_beingJammedBy_Count = beingJammedBy.Count;
			}

			if (detectedObjects.Count != previous_detectedObjects_Count)
			{
				needToRefresh = true;
				previous_detectedObjects_Count = detectedObjects.Count;
			}

			if (needToRefresh)
				TermBlock.RefreshCustomInfo();
		}

		private void AppendingCustomInfo(IMyTerminalBlock block, StringBuilder customInfo)
		{
			customInfo.AppendLine();

			// jammer

			if (IsJammer)
				if (PowerLevel_Current > 0)
					customInfo.AppendLine("Jammer power level: " + (int)PowerLevel_Current);

			if (deliberateJamming > 0)
			{
				customInfo.Append("Jamming " + deliberateJamming + " radar");
				if (deliberateJamming != 1)
					customInfo.Append('s');
				customInfo.AppendLine();
			}

			// radar

			if (IsRadar)
				if (PowerLevel_Radar > 0)
					customInfo.AppendLine("Radar power level: " + (int)PowerLevel_Radar);

			if (beingJammedBy.Count > 0)
			{
				customInfo.Append("Interference from " + beingJammedBy.Count + " source");
				if (beingJammedBy.Count != 1)
					customInfo.Append('s');
				customInfo.AppendLine();
			}

			// detected

			if (detectedObjects.Count > 0)
			{
				customInfo.Append("Detecting " + detectedObjects.Count + " object");
				if (detectedObjects.Count != 1)
					customInfo.Append('s');
				customInfo.AppendLine();
			}
		}

		#endregion

	}
}
