using System;
using System.Collections.Generic;
using System.Linq;
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
	public class Radar
	{
		public class Definition
		{
			#region Public Fields

			/// <summary>Iff false, block is a radar jammer.</summary>
			public bool IsRadar = true;

			/// <summary>Iff greater than 0, power level will be forced down to this number.</summary>
			public float MaxPowerLevel = 50000;

			/// <summary>Power change per 100 updates while on.</summary>
			public float PowerIncrease = 1000;

			/// <summary>Power change per 100 updates while off.</summary>
			public float PowerDecrease = -2000;

			/// <summary>
			/// <para>Affects how much of the signal is reflected back.</para>
			/// <para>Reflected signal = signal * (volume + A) / (volume + B)</para>
			/// </summary>
			public float Reflect_A = 1000, Reflect_B = 20000;

			/// <summary>Multiplier for the distance a signal carries. For passive collection, value from receiving radar will be used.</summary>
			public float SignalEnhance = 1;

			/// <summary>Not implemented. How well the signal can penetrate a solid object.</summary>
			public float Penetration = 0;

			/// <summary>Reduces the effective strength of electronic jamming by this amount.</summary>
			public float JammingResistance = 0;

			// not sure if this makes any senese
			///// <summary>Not implemented. Resistance to mechanical jamming.</summary>
			//public float JammingResistance_Mechanical = 0;

			/// <summary>Strength of jamming signal necessary to passively determine the location of a radar jammer. 0 = no passive detection.</summary>
			public float PassiveDetect_Jamming = 0;

			/// <summary>Strength of radar signal necessary to passively determine the location of a radar. 0 = no passive detection.</summary>
			public float PassiveDetect_Radar = 0;

			/// <summary>Maximum targets that can be tracked, 0 = infinite.</summary>
			public float MaximumTargets = 0;

			/// <summary>How much of the jamming effect is applied to friendly radar.</summary>
			public float JamFriendly = 0.1f;

			/// <summary>Iff false, angles are ignored.</summary>
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

		private const float decoyStrength = 0.1f;
		private static readonly Logger staticLogger = new Logger("N/A", "Radar");
		private static readonly ThreadManager myThread = new ThreadManager();
		private static readonly Dictionary<string, Definition> AllDefinitions = new Dictionary<string, Definition>();
		private static readonly List<Radar> AllRadarAndJam = new List<Radar>();

		public static bool IsRadar(IMyCubeBlock block)
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
				AllDefinitions.Add(ID, result);
				return result;
			}

			// parse description
			string[] properties = def.DescriptionString.Split(new char[] { }, StringSplitOptions.RemoveEmptyEntries);
			foreach (string prop in properties)
			{

				if (prop == "IsJammer")
				{
					result.IsRadar = false;
					continue;
				}

				string[] propValue = prop.Split('=');
				if (propValue.Length != 2)
				{
					staticLogger.alwaysLog("for " + block.BlockDefinition.ToString() + ", incorrect format for property: \"" + prop + '"', "GetDefinition()", Logger.severity.WARNING);
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
					case "PassiveDetect_Jamming":
						result.PassiveDetect_Jamming = value;
						continue;
					case "PassiveDetect_Radar":
						result.PassiveDetect_Radar = value;
						continue;
					case "MaximumTargets":
						result.MaximumTargets = value;
						continue;
					case "JamFriendly":
						result.JamFriendly = value;
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

			staticLogger.debugLog("parsed description for " + ID, "GetDefinition()", Logger.severity.INFO);
			staticLogger.debugLog("serialized definition:\n" + MyAPIGateway.Utilities.SerializeToXML(result), "GetDefinition()", Logger.severity.TRACE);
			AllDefinitions.Add(ID, result);
			return result;
		}

		#endregion

		private readonly Logger myLogger;
		private readonly IMyCubeBlock CubeBlock;
		private readonly Definition myDefinition;
		private readonly FastResourceLock myLock = new FastResourceLock();

		/// <summary>size of radar signature/signal and object</summary>
		private readonly SortedDictionary<float, LastSeen> detectedObjects;
		private readonly HashSet<Radar> jamming;
		private readonly Dictionary<Radar, float> beingJammedBy;

		private float PowerLevel_Target = 0f;
		private float PowerLevel_Current = 0f;
		private float PowerLevel_Effective = 0f;

		public Radar(IMyCubeBlock block)
		{
			myLogger = new Logger("Radar", block);
			CubeBlock = block;
			myDefinition = GetDefinition(CubeBlock);

			if (myDefinition.IsRadar)
			{
				detectedObjects = new SortedDictionary<float, LastSeen>();
				beingJammedBy = new Dictionary<Radar, float>();
			}
			else
			{
				jamming = new HashSet<Radar>();
			}

			AllRadarAndJam.Add(this);
			CubeBlock.OnClose += CubeBlock_OnClose;
			(CubeBlock as IMyTerminalBlock).AppendingCustomInfo += AppendingCustomInfo;

			UpdateTargetPowerLevel();
			PowerLevel_Current = PowerLevel_Target;
			myLogger.debugLog("Radar initialized, power level: " + PowerLevel_Current, "Radar()", Logger.severity.INFO);
		}

		private void CubeBlock_OnClose(IMyEntity obj)
		{
			AllRadarAndJam.Remove(this);
			StopJamming();
			(CubeBlock as IMyTerminalBlock).AppendingCustomInfo -= AppendingCustomInfo;
		}

		public void Update100()
		{
			if (myLock.TryAcquireExclusive())
			{
				CheckCustomInfo();
				if (detectedObjects != null && detectedObjects.Count > 0)
					Receiver.sendToAttached(CubeBlock, detectedObjects.Values);
				myThread.EnqueueAction(Update_OnThread);
			}
		}

		private void Update_OnThread()
		{
			try
			{
				if (!CubeBlock.IsWorking)
				{
					if (PowerLevel_Current > 0)
						PowerLevel_Current += myDefinition.PowerDecrease;
					detectedObjects.Clear();
					StopJamming();
					return;
				}

				UpdatePowerLevel();

				if (myDefinition.IsRadar)
				{
					detectedObjects.Clear();
					ActiveDetection();
					PassiveDetection();
				}
				else // is a jammer
					JamRadar();
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
				Ingame.IMyBeacon asBeacon;
				Ingame.IMyRadioAntenna asRadio;

				// from name
				string instructions = CubeBlock.getInstructions();
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
				if (myDefinition.MaxPowerLevel > 0 && PowerLevel_Target > myDefinition.MaxPowerLevel)
				{
					myLogger.debugLog("Reducing target power from " + PowerLevel_Target + " to " + myDefinition.MaxPowerLevel, "UpdatePowerLevel()", Logger.severity.INFO);
					PowerLevel_Target = myDefinition.MaxPowerLevel;

					string instructions = CubeBlock.getInstructions();
					if (instructions != null)
						(CubeBlock as IMyTerminalBlock).SetCustomName(CubeBlock.DisplayNameText.Replace(instructions, myDefinition.MaxPowerLevel.ToString()));

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

		private void ActiveDetection()
		{
			PowerLevel_Effective = PowerLevel_Current;
			if (beingJammedBy.Count != 0)
			{
				float jamSum = 0;
				foreach (float jamStrength in beingJammedBy.Values)
					jamSum += jamStrength;

				jamSum -= myDefinition.JammingResistance;

				if (jamSum > 0)
					PowerLevel_Effective -= jamSum;

				myLogger.debugLog("being jammed by " + beingJammedBy.Count + " jammers, current power level: " + PowerLevel_Current + ", effective power level: " + PowerLevel_Effective, "ActiveDetection()", Logger.severity.DEBUG);
			}

			if (PowerLevel_Effective <= 0)
			{
				myLogger.debugLog("no detection possible, effective power level: " + PowerLevel_Effective, "ActiveDetection()", Logger.severity.DEBUG);
				return;
			}

			HashSet<IMyEntity> allGrids = new HashSet<IMyEntity>();
			MyAPIGateway.Entities.GetEntities_Safe(allGrids, (entity) => { return entity is IMyCubeGrid; });
			foreach (IMyCubeGrid ent in allGrids)
			{
				if (!ent.Save)
					continue;

				Vector3D target = ent.GetPosition();
				if (ReallyFar(target, PowerLevel_Effective * myDefinition.SignalEnhance) || UnacceptableAngle(target) || Obstructed(ent))
					continue;

				//if (SignalCannotReach(ent, PowerLevel_Effective * myDefinition.SignalEnhance, ent.getBestName(), "radar signal"))
				//	continue;

				int workingDecoys = 0;
				ReadOnlyList<IMyCubeBlock> decoys = CubeGridCache.GetFor(ent).GetBlocksOfType(typeof(MyObjectBuilder_Decoy));
				if (decoys != null)
					foreach (IMyCubeBlock d in decoys)
						if (d.IsWorking)
							workingDecoys++;

				float volume = ent.LocalAABB.Volume();
				float reflectivity = (volume + myDefinition.Reflect_A) / (volume + myDefinition.Reflect_B);

				reflectivity += decoyStrength * workingDecoys;

				float distance = Vector3.Distance(CubeBlock.GetPosition(), target) / myDefinition.SignalEnhance;
				float radarSignature = (PowerLevel_Effective - distance) * reflectivity - distance;

				if (radarSignature > 0)
				{
					myLogger.debugLog("object detected: " + ent.getBestName(), "ActiveDetection()", Logger.severity.TRACE);
					AddDetectedObject(radarSignature, new LastSeen(ent, false, new RadarInfo(volume)));
				}
			}
		}

		private void JamRadar()
		{
			if (PowerLevel_Current <= 0)
				return;

			StopJamming();

			foreach (Radar otherRadar in AllRadarAndJam)
			{
				if (!otherRadar.myDefinition.IsRadar || !otherRadar.CubeBlock.IsWorking)
					continue;

				Vector3D target = otherRadar.CubeBlock.GetPosition();
				if (ReallyFar(target, PowerLevel_Current * myDefinition.SignalEnhance) || UnacceptableAngle(target) || Obstructed(otherRadar.CubeBlock))
					continue;

				bool friendly = CubeBlock.canConsiderFriendly(otherRadar.CubeBlock);
				if (friendly && myDefinition.JamFriendly == 0f)
				{
					myLogger.debugLog("cannot jam a friendly: " + otherRadar.CubeBlock.getBestName(), "ActiveDetection()", Logger.severity.TRACE);
					continue;
				}

				float distance = Vector3.Distance(CubeBlock.GetPosition(), target) / myDefinition.SignalEnhance;
				float signalStrength = PowerLevel_Current - distance;
				if (friendly)
					signalStrength *= myDefinition.JamFriendly;

				if (signalStrength > 0)
				{
					myLogger.debugLog("jamming: " + otherRadar.CubeBlock.getBestName(), "ActiveDetection()", Logger.severity.TRACE);
					jamming.Add(otherRadar);
					otherRadar.beingJammedBy.Add(this, signalStrength);
				}
			}
		}

		private void PassiveDetection()
		{
			if (myDefinition.PassiveDetect_Radar > 0f || myDefinition.PassiveDetect_Jamming > 0f)
				foreach (Radar otherRadar in AllRadarAndJam)
				{
					if (!otherRadar.CubeBlock.IsWorking)
						continue;

					float detection = otherRadar.myDefinition.IsRadar ? myDefinition.PassiveDetect_Radar : myDefinition.PassiveDetect_Jamming;

					Vector3D target = otherRadar.CubeBlock.GetPosition();
					if (ReallyFar(target, detection * myDefinition.SignalEnhance) || UnacceptableAngle(target) || Obstructed(otherRadar.CubeBlock))
						continue;

					float distance = Vector3.Distance(CubeBlock.GetPosition(), target) / myDefinition.SignalEnhance;
					float signalStrength = otherRadar.PowerLevel_Current - distance - detection;

					if (signalStrength > 0)
					{
						myLogger.debugLog("radar signal seen: " + otherRadar.CubeBlock.getBestName(), "ActiveDetection()", Logger.severity.TRACE);
						AddDetectedObject(signalStrength, new LastSeen(otherRadar.CubeBlock, true));
					}
				}
		}

		//// for debuggin, will be removed
		//private bool SignalCannotReach(IMyEntity target, float compareDist, string targetName, string signalName)
		//{
		//	if (ReallyFar(target.GetPosition(), compareDist))
		//	{
		//		myLogger.debugLog(signalName + ": target really far " + targetName, "SignalCanReach()");
		//		return true;
		//	}

		//	if (UnacceptableAngle(target.GetPosition()))
		//	{
		//		myLogger.debugLog(signalName + ": unacceptable angle " + targetName, "SignalCanReach()");
		//		return true;
		//	}

		//	if (Obstructed(target))
		//	{
		//		myLogger.debugLog(signalName + ": obstructed " + targetName, "SignalCanReach()");
		//		return true;
		//	}

		//	return false;
		//}

		private bool ReallyFar(Vector3D target, float compareTo)
		{
			Vector3D position = CubeBlock.GetPosition();
			return Math.Abs(position.X - target.X) > compareTo
					|| Math.Abs(position.Y - target.Y) > compareTo
					|| Math.Abs(position.Z - target.Z) > compareTo;
		}

		/// <summary>
		/// Determines if the position does not fall within the angle limits of the radar.
		/// </summary>
		private bool UnacceptableAngle(Vector3D target)
		{
			if (!myDefinition.EnforceAngle)
				return false;

			Vector3 RotateToDirection = Vector3.Normalize(RelativeDirection3F.FromWorld(CubeBlock.CubeGrid, target).ToBlockNormalized(CubeBlock));

			float azimuth, elevation;
			Vector3.GetAzimuthAndElevation(RotateToDirection, out azimuth, out elevation);

			return azimuth < myDefinition.MinAzimuth || azimuth > myDefinition.MaxAzimuth || elevation < myDefinition.MinElevation || elevation > myDefinition.MaxElevation;
		}

		/// <summary>
		/// Determines if there is an obstruction between radar and target.
		/// </summary>
		private bool Obstructed(IMyEntity target)
		{
			List<Line> lines = new List<Line>();
			lines.Add(new Line(CubeBlock.GetPosition(), target.GetPosition(), false));

			HashSet<IMyEntity> entities = new HashSet<IMyEntity>();
			MyAPIGateway.Entities.GetEntities_Safe(entities, (entity) => { return entity is IMyCharacter || entity is IMyCubeGrid; });

			List<IMyEntity> ignore = new List<IMyEntity>();
			ignore.Add(CubeBlock);
			ignore.Add(target);

			object obstruction;
			return RayCast.Obstructed(lines, entities, ignore, out obstruction);
		}

		private void AddDetectedObject(float signalStrength, LastSeen obj)
		{
			if (myDefinition.MaximumTargets > 0)
				detectedObjects.AddIfBetter(signalStrength, obj, (int)myDefinition.MaximumTargets);
			else
				detectedObjects.AddIncrement(signalStrength, obj);
		}

		private void StopJamming()
		{
			if (myDefinition.IsRadar || jamming.Count == 0)
				return;

			foreach (Radar jammed in jamming)
				jammed.beingJammedBy.Remove(this);

			jamming.Clear();
		}

		#region Custom Info

		private float previous_PowerLevel_Current;
		private int previous_beingJammedBy_Count;
		private int previous_detectedObjects_Count;
		private int previous_jamming_Count;

		private void CheckCustomInfo()
		{
			bool needToRefresh = false;

			if (PowerLevel_Current != previous_PowerLevel_Current)
			{
				needToRefresh = true;
				previous_PowerLevel_Current = PowerLevel_Current;
			}

			if (myDefinition.IsRadar)
			{
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
			}
			else // is a jammer
			{
				if (jamming.Count != previous_jamming_Count)
				{
					needToRefresh = true;
					previous_jamming_Count = jamming.Count;
				}
			}

			if (needToRefresh)
				(CubeBlock as IMyTerminalBlock).RefreshCustomInfo();
		}

		private void AppendingCustomInfo(IMyTerminalBlock block, StringBuilder customInfo)
		{
			customInfo.AppendLine("Current power level: " + PowerLevel_Current);

			if (myDefinition.IsRadar)
			{
				if (beingJammedBy.Count > 0)
					customInfo.AppendLine("Interferance from " + beingJammedBy.Count + " source(s)");

				customInfo.AppendLine("Detecting " + detectedObjects.Count + " objects");
			}
			else
			{
				if (jamming.Count > 0)
					customInfo.AppendLine("Jamming " + jamming.Count + " radar(s)");
			}
		}

		#endregion

	}
}
