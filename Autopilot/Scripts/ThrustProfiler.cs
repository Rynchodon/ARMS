#define LOG_ENABLED //remove on build

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

using Sandbox.Common.ObjectBuilders;
using Sandbox.Definitions;
using Sandbox.ModAPI;
using VRageMath;
using Ingame = Sandbox.ModAPI.Ingame;

namespace Rynchodon.Autopilot
{
	/// <summary>
	/// Tracks the direction and power of a grids thrusters.
	/// </summary>
	class ThrustProfiler
	{
		private Logger myLogger = null;
		[System.Diagnostics.Conditional("LOG_ENABLED")]
		private void log(string toLog, string method = null, Logger.severity level = Logger.severity.DEBUG)
		{ myLogger.log(level, method, toLog); }
		private void alwaysLog(string toLog, string method = null, Logger.severity level = Logger.severity.WARNING)
		{ myLogger.log(level, method, toLog); }

		private IMyCubeGrid myGrid;

		/// <summary>
		/// it is less expensive to remember some information about thrusters
		/// </summary>
		private class ThrusterProperties
		{
			/// <summary>from definition</summary>
			public float forceDamping;
			public Base6Directions.Direction forceDirect;
			//public bool isEnabled;

			public ThrusterProperties(float forceDamping, Base6Directions.Direction forceDirect)
			{
				this.forceDamping = forceDamping;
				this.forceDirect = forceDirect;
				//this.isEnabled = false;
			}

			public override string ToString()
			{ return "force = " + forceDamping + ", direction = " + forceDirect; } // +", enabled = " + isEnabled; }
		}

		private Dictionary<IMyThrust, ThrusterProperties> allMyThrusters = new Dictionary<IMyThrust, ThrusterProperties>();

		private Dictionary<Base6Directions.Direction, float> value_dampingProfile;
		/// <summary>
		/// direction shall be direction of force on ship (opposite of thruster direction)
		/// float shall be force of maximum damping
		/// <para>set to null to force a rebuild</para>
		/// </summary>
		private Dictionary<Base6Directions.Direction, float> dampingProfile
		{
			get
			{
				if (value_dampingProfile == null) // needs to be built
				{
					value_dampingProfile = new Dictionary<Base6Directions.Direction, float>();
					foreach (Base6Directions.Direction direction in Base6Directions.EnumDirections)
						value_dampingProfile.Add(direction, 0);

					foreach (var thruster in allMyThrusters)
						if (!thruster.Key.Closed && thruster.Key.IsWorking)
							value_dampingProfile[thruster.Value.forceDirect] += thruster.Value.forceDamping * thruster.Key.ThrustMultiplier;
				}
				return value_dampingProfile;
			}
			set { value_dampingProfile = value; }
		}

		//private static readonly MyObjectBuilderType thrusterID = (new MyObjectBuilder_Thrust()).TypeId;
		private static readonly MyObjectBuilderType moduleID = (new MyObjectBuilder_UpgradeModule()).TypeId;

		/// <summary>
		/// Thrusters which are disabled by Autopilot and should not be removed from profile.
		/// </summary>
		private HashSet<IMyThrust> thrustersDisabledByAutopilot = new HashSet<IMyThrust>();

		public ThrustProfiler(IMyCubeGrid grid)
		{
			if (grid == null)
			{ (new Logger("", "ThrustProfiler")).log(Logger.severity.FATAL, "..ctor", "null parameter"); }
			myLogger = new Logger(grid.DisplayName, "ThrustProfiler");
			myGrid = grid;

			init();
		}

		private void init()
		{
			log("initializing", "init()", Logger.severity.TRACE);
			//thrustProfile = new Dictionary<Base6Directions.Direction, float>();
			allMyThrusters = new Dictionary<IMyThrust, ThrusterProperties>();
			//foreach (Base6Directions.Direction direction in Base6Directions.EnumDirections)
			//	thrustProfile.Add(direction, 0);

			List<IMySlimBlock> thrusters = new List<IMySlimBlock>();
			myGrid.GetBlocks(thrusters, block => block.FatBlock != null && block.FatBlock is IMyThrust); // .BlockDefinition.TypeId == thrusterID);
			foreach (IMySlimBlock thrust in thrusters)
				newThruster(thrust);

			myGrid.OnBlockAdded += grid_OnBlockAdded;
			myGrid.OnBlockRemoved += grid_OnBlockRemoved;
			//enableAllThrusters(); // up to Navigator

			dampingProfile = null;
		}

		/// <summary>
		/// Creates properties for a thruster and adds it to allMyThrusters. If the thruster is working, calls enableDisableThruster(true).
		/// </summary>
		/// <param name="thruster">The new thruster</param>
		private void newThruster(IMySlimBlock thruster)
		{
			float dampingForce = 10 * (MyDefinitionManager.Static.GetCubeBlockDefinition(thruster.GetObjectBuilder()) as MyThrustDefinition).ForceMagnitude;
			ThrusterProperties properties = new ThrusterProperties(dampingForce, Base6Directions.GetFlippedDirection(thruster.FatBlock.Orientation.Forward));
			allMyThrusters.Add(thruster.FatBlock as IMyThrust, properties);
			thruster.FatBlock.IsWorkingChanged += block_IsWorkingChanged;
			//if (thruster.FatBlock.IsWorking)
			//	enableDisableThruster(properties, true);
			return;
		}

		///// <summary>
		///// Add or remove a thruster from thrustProfile, if appropriate.
		///// </summary>
		//private void enableDisableThruster(ThrusterProperties properties, bool enable)
		//{
		//	if (properties.isEnabled == enable)
		//	{
		//		if (enable)
		//			log("already enabled. " + properties, "enableDisableThruster()", Logger.severity.DEBUG);
		//		else
		//			log("already disabled. " + properties, "enableDisableThruster()", Logger.severity.DEBUG);
		//		return;
		//	}
		//	properties.isEnabled = enable;

		//	float change = properties.forceDampingOld;
		//	Base6Directions.Direction direction = properties.forceDirect;

		//	if (!enable)
		//		change = -change;

		//	thrustProfile[direction] += change;
		//	//log("thrust power change = " + change + ", total power = " + thrustProfile[direction] + ", direction = " + direction, "enableDisableThruster()", Logger.severity.TRACE);
		//}

		/// <summary>
		/// if added is a thruster, call newThruster()
		/// </summary>
		/// <param name="added">block that was added</param>
		private void grid_OnBlockAdded(IMySlimBlock added)
		{
			try
			{
				if (added.FatBlock == null)
					return;

				if (added.FatBlock.BlockDefinition.TypeId == moduleID)
				{
					dampingProfile = null;
					added.FatBlock.IsWorkingChanged += block_IsWorkingChanged;
					return;
				}

				if (added.FatBlock is IMyThrust)
				{
					newThruster(added);
					dampingProfile = null;
				}
			}
			catch (Exception e)
			{ myLogger.log(Logger.severity.ERROR, "grid_OnBlockAdded()", "Exception: " + e); }
		}

		/// <summary>
		/// if removed is a thruster, remove it from allMyThrusters, unregister for block_IsWorkingChanged, and call enableDisableThruster(properties, false)
		/// </summary>
		/// <remarks>
		/// if a working block is destroyed, block_IsWorkingChange() is called first
		/// </remarks>
		/// <param name="removed">block that was removed</param>
		private void grid_OnBlockRemoved(IMySlimBlock removed)
		{
			try
			{
				if (removed.FatBlock == null) 
					return;
				if (removed.FatBlock.BlockDefinition.TypeId == moduleID)
				{
					dampingProfile = null;
					removed.FatBlock.IsWorkingChanged -= block_IsWorkingChanged;
					return;
				}

				IMyThrust asThrust = removed.FatBlock as IMyThrust;
				if (asThrust == null)
					return;

				ThrusterProperties properties;
				if (allMyThrusters.TryGetValue(asThrust, out properties))
				{
					removed.FatBlock.IsWorkingChanged -= block_IsWorkingChanged;
					//enableDisableThruster(properties, false);
					allMyThrusters.Remove(asThrust);
					log("removed thruster = " + removed.FatBlock.DefinitionDisplayNameText + ", " + properties, "grid_OnBlockRemoved()", Logger.severity.DEBUG);
					dampingProfile = null;
					return;
				}
				myLogger.debugLog("could not get properties for thruster", "grid_OnBlockRemoved()", Logger.severity.ERROR);
			}
			catch (Exception e)
			{ myLogger.log(Logger.severity.ERROR, "grid_OnBlockRemoved()", "Exception: " + e); }
		}

		/// <summary>
		/// <para>if block is in allMyThrusters, calls enableDisableThruster(properties, changed.IsWorking)</para>
		/// <para>ignores blocks disabled by Autopilot</para>
		/// </summary>
		/// <remarks>
		/// <para>Only registered for thrusters.</para>
		/// <para>if a block is detached, grid_OnBlockRemoved() is called first</para>
		/// </remarks>
		/// <param name="changed">block whose state has changed</param>
		private void block_IsWorkingChanged(IMyCubeBlock changed)
		{
			try
			{
				if (changed.BlockDefinition.TypeId == moduleID)
				{
					dampingProfile = null;
					return;
				}

				IMyThrust asThrust = changed as IMyThrust;
				if (asThrust == null)
					return;

				ThrusterProperties properties;
				if (allMyThrusters.TryGetValue(asThrust, out properties))
				{
					if (!asThrust.IsWorking && thrustersDisabledByAutopilot.Contains(asThrust))
					{
						myLogger.debugLog("thruster disabled by Autopilot: " + changed.DisplayNameText, "block_IsWorkingChanged()", Logger.severity.TRACE);
						return;
					}
					log("changed block = " + changed.DefinitionDisplayNameText + ", IsWorking = " + changed.IsWorking + ", " + properties, "block_IsWorkingChanged()", Logger.severity.DEBUG);
					//enableDisableThruster(properties, changed.IsWorking);
					dampingProfile = null;
					return;
				}
				myLogger.debugLog("could not get properties, assuming block has been detached", "block_IsWorkingChanged()", Logger.severity.INFO);
			}
			catch (Exception e)
			{ myLogger.log(Logger.severity.ERROR, "block_IsWorkingChanged()", "Exception: " + e); }
		}

		#region Public Methods

		/// <summary>
		/// scales a movement vector by available thrust force
		/// </summary>
		/// <param name="displacement">displacement vector</param>
		/// <param name="remote">controlling remote</param>
		/// <returns>scaled vector</returns>
		public RelativeVector3F scaleByForce(RelativeVector3F displacement, IMyCubeBlock remote)
		{
			Vector3 displacementGrid = displacement.getLocal();

			// get force-determinant direction
			// for each thrusting direction, compare needed thrust to max available
			float minForce = float.MaxValue;
			foreach (Base6Directions.Direction direction in Base6Directions.EnumDirections) //Enum.GetValues(typeof(Base6Directions.Direction)))
			{
				float movementInDirection = displacementGrid.Dot(Base6Directions.GetVector(direction));
				if (movementInDirection > 0)
				{
					minForce = Math.Min(minForce, dampingProfile[direction]);
				}
			}

			// scale thrust to min
			Vector3 scaledMovement = Vector3.Zero;
			foreach (Base6Directions.Direction direction in Base6Directions.EnumDirections) //Enum.GetValues(typeof(Base6Directions.Direction)))
			{
				float movementInDirection = displacementGrid.Dot(Base6Directions.GetVector(direction));
				if (movementInDirection > 0)
				{
					float scaleFactor = minForce / dampingProfile[direction];
					scaledMovement += movementInDirection * scaleFactor * Base6Directions.GetVector(direction);
				}
			}

			return RelativeVector3F.createFromLocal(scaledMovement, remote.CubeGrid);
		}

		/// <summary>
		/// Finds a distance that will always be greater than the distance required to stop.
		/// </summary>
		/// <remarks>
		/// <para>the approximation that is used by this method is: stopping distance = speed * speed / max acceleration</para>
		/// <para>max acceleration is the smaller of available force / mass (exact) and speed / 2 (approximation)</para>
		/// <para>where there is sufficient thrust, this could be simplified to stopping distance = 2 * speed</para>
		/// </remarks>
		/// <returns>A distance larger than the distance required to stop</returns>
		public float getStoppingDistance()
		{
			RelativeVector3F velocity = RelativeVector3F.createFromWorld(myGrid.Physics.LinearVelocity, myGrid);

			float maxStopDistance = 0;
			foreach (Base6Directions.Direction direction in Base6Directions.EnumDirections) //Enum.GetValues(typeof(Base6Directions.Direction)))
			{
				float velocityInDirection = velocity.getLocal().Dot(Base6Directions.GetVector(direction));
				if (velocityInDirection < -0.1) // direction is opposite of velocityGrid
				{
					float acceleration = Math.Min(Math.Abs(dampingProfile[direction] / myGrid.Physics.Mass), Math.Abs(velocityInDirection / 2));
					float stoppingDistance = velocityInDirection * velocityInDirection / acceleration;
					maxStopDistance = Math.Max(stoppingDistance, maxStopDistance);
				}
			}

			return maxStopDistance;
		}

		/// <summary>
		/// toggle thrusters in a direction off, this shall not affect stopping distance.
		/// </summary>
		/// <param name="direction">direction of force on ship</param>
		public void disableThrusters(Base6Directions.Direction direction)
		{
			foreach (KeyValuePair<IMyThrust, ThrusterProperties> singleThrustProfile in allMyThrusters)
				if (singleThrustProfile.Value.forceDirect == direction)
				{
					thrustersDisabledByAutopilot.Add(singleThrustProfile.Key);
					singleThrustProfile.Key.RequestEnable(false);
				}
		}

		/// <summary>
		/// enable all the thrusters of this grid. disables any thruster with an override
		/// </summary>
		public void enableAllThrusters()
		{
			foreach (IMyCubeBlock thruster in allMyThrusters.Keys)
			{
				Ingame.IMyThrust ingame = thruster as Ingame.IMyThrust;
				if (ingame.ThrustOverride > 0)
					ingame.RequestEnable(false);
				else
					ingame.RequestEnable(true);
			}

			thrustersDisabledByAutopilot = new HashSet<IMyThrust>();
		}

		/// <summary>
		/// Are there any thrusters that are currently disabled by Autopilot?
		/// </summary>
		/// <returns>true iff any thrusters are disabled by Autopilot</returns>
		public bool disabledThrusters()
		{ return thrustersDisabledByAutopilot.Count > 0; }

		#endregion
	}
}
