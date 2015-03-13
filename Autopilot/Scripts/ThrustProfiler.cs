#define LOG_ENABLED //remove on build

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

//using Sandbox.Common;
//using Sandbox.Common.Components;
using Sandbox.Common.ObjectBuilders;
using Sandbox.Common.ObjectBuilders.Definitions;
using Sandbox.Definitions;
//using Sandbox.Engine;
//using Sandbox.Game;
using Sandbox.ModAPI;
//using Ingame = Sandbox.ModAPI.Ingame;
//using Sandbox.ModAPI.Interfaces;
using VRageMath;

namespace Rynchodon.Autopilot
{
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
		/// need to remember information about thrusters, so they can close without affect this class
		/// </summary>
		private class ThrusterProperties
		{
			public float forceDamping;
			public Base6Directions.Direction forceDirect;
			public bool isEnabled;

			public ThrusterProperties(float forceDamping, Base6Directions.Direction forceDirect)
			{
				this.forceDamping = forceDamping;
				this.forceDirect = forceDirect;
				this.isEnabled = false;
			}

			public override string ToString()
			{ return "force = " + forceDamping + ", direction = " + forceDirect + ", enabled = " + isEnabled; }
		}

		private Dictionary<IMyCubeBlock, ThrusterProperties> allMyThrusters = new Dictionary<IMyCubeBlock, ThrusterProperties>();

		/// <summary>
		/// direction shall be direction of force on ship (opposite of thruster direction)
		/// float shall be force of maximum damping
		/// </summary>
		private Dictionary<Base6Directions.Direction, float> thrustProfile;

		private static MyObjectBuilderType thrusterID = (new MyObjectBuilder_Thrust()).TypeId;

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
			thrustProfile = new Dictionary<Base6Directions.Direction, float>();
			allMyThrusters = new Dictionary<IMyCubeBlock, ThrusterProperties>();
			foreach (Base6Directions.Direction direction in Base6Directions.EnumDirections)
				thrustProfile.Add(direction, 0);

			List<IMySlimBlock> thrusters = new List<IMySlimBlock>();
			myGrid.GetBlocks(thrusters, block => block.FatBlock != null && block.FatBlock.BlockDefinition.TypeId == thrusterID);
			foreach (IMySlimBlock thrust in thrusters)
				newThruster(thrust);

			myGrid.OnBlockAdded += grid_OnBlockAdded;
			myGrid.OnBlockRemoved += grid_OnBlockRemoved;
		}

		private void newThruster(IMySlimBlock thruster)
		{
			float dampingForce = 10 * (MyDefinitionManager.Static.GetCubeBlockDefinition(thruster.GetObjectBuilder()) as MyThrustDefinition).ForceMagnitude;
			ThrusterProperties properties = new ThrusterProperties(dampingForce, Base6Directions.GetFlippedDirection(thruster.FatBlock.Orientation.Forward));
			allMyThrusters.Add(thruster.FatBlock, properties);
			thruster.FatBlock.IsWorkingChanged += block_IsWorkingChanged;
			if (thruster.FatBlock.IsWorking)
				enableDisableThruster(properties, true);
			return;
		}

		private void enableDisableThruster(ThrusterProperties properties, bool enable)
		{
			if (properties.isEnabled == enable)
			{
				if (enable)
					log("already enabled. " + properties, "enableDisableThruster()", Logger.severity.DEBUG);
				else
					log("already disabled. " + properties, "enableDisableThruster()", Logger.severity.DEBUG);
				return;
			}
			properties.isEnabled = enable;

			float change = properties.forceDamping;
			Base6Directions.Direction direction = properties.forceDirect;

			if (!enable)
				change = -change;

			thrustProfile[direction] += change;

			//log("thrust power change = " + change + ", total power = " + thrustProfile[direction] + ", direction = " + direction, "enableDisableThruster()", Logger.severity.TRACE);
			//Tforce.addForce( change);
		}

		private void grid_OnBlockAdded(IMySlimBlock added)
		{
			try
			{
				if (added.FatBlock == null || added.FatBlock.BlockDefinition.TypeId != thrusterID) // not a thruster
					return;
				newThruster(added);
				return;
			}
			catch (Exception e)
			{ myLogger.log(Logger.severity.ERROR, "grid_OnBlockAdded()", "Exception: " + e); }
			init();
		}

		// if a working block is destroyed, block_IsWorkingChange() is called first
		private void grid_OnBlockRemoved(IMySlimBlock removed)
		{
			try
			{
				if (removed.FatBlock == null || removed.FatBlock.BlockDefinition.TypeId != thrusterID) // not a thruster
					return;
				ThrusterProperties properties;
				if (allMyThrusters.TryGetValue(removed.FatBlock, out properties))
				{
					enableDisableThruster(properties, false);
					allMyThrusters.Remove(removed.FatBlock);
					log("removed thruster = " + removed.FatBlock.DefinitionDisplayNameText + ", " + properties, "grid_OnBlockRemoved()", Logger.severity.DEBUG);
					return;
				}
				myLogger.log(Logger.severity.ERROR, "grid_OnBlockRemoved()", "could not get properties");
			}
			catch (Exception e)
			{ myLogger.log(Logger.severity.ERROR, "grid_OnBlockRemoved()", "Exception: " + e); }
			init();
		}

		// if a block is detached, grid_OnBlockRemoved() is called first
		private void block_IsWorkingChanged(IMyCubeBlock changed)
		{
			try
			{
				ThrusterProperties properties;
				if (allMyThrusters.TryGetValue(changed, out properties))
				{
					enableDisableThruster(properties, changed.IsWorking);
					log("changed block = " + changed.DefinitionDisplayNameText + ", IsWorking = " + changed.IsWorking + ", " + properties, "block_IsWorkingChanged()", Logger.severity.DEBUG);
					return;
				}
				myLogger.log(Logger.severity.INFO, "block_IsWorkingChanged()", "could not get properties, assuming block has been removed");
				return;
			}
			catch (Exception e)
			{ myLogger.log(Logger.severity.ERROR, "block_IsWorkingChanged()", "Exception: " + e); }
			init();
		}

		/// <summary>
		/// scales a movement vector by available thrust force
		/// </summary>
		/// <param name="movement"></param>
		/// <returns></returns>
		public RelativeVector3F scaleByForce(RelativeVector3F displacement, IMyCubeBlock remote)
		{
			//log("entered scaleVector("+movement+")", "scaleVector()", Logger.severity.TRACE);
			Vector3 displacementGrid = displacement.getGrid();
			//log("displacementWorld=" + displacement.getWorld() + ", displacementGrid=" + displacementGrid, "scaleByForce()", Logger.severity.TRACE);

			// get force-determinant direction
			// for each thrusting direction, compare needed thrust to max available
			float minForce = float.MaxValue;
			foreach (Base6Directions.Direction direction in Base6Directions.EnumDirections) //Enum.GetValues(typeof(Base6Directions.Direction)))
			{
				//log("direction = " + direction + ", blockDirection = " + blockDirection, "scaleByForce()", Logger.severity.TRACE);
				float movementInDirection = displacementGrid.Dot(Base6Directions.GetVector(direction));
				//log("directions=" + direction + ", movementInDirection=" + movementInDirection + ", displacementGrid=" + displacementGrid + ", dirVector=" + Base6Directions.GetVector(direction), "scaleByForce()", Logger.severity.TRACE);
				if (movementInDirection > 0)
				{
					minForce = Math.Min(minForce, thrustProfile[direction]);
					//log("direction is "+direction+" min force is "+minForce, "scaleByForce()", Logger.severity.TRACE);
				}
			}

			// scale thrust to min
			Vector3 scaledMovement = Vector3.Zero;
			foreach (Base6Directions.Direction direction in Base6Directions.EnumDirections) //Enum.GetValues(typeof(Base6Directions.Direction)))
			{
				//Base6Directions.Direction blockDirection = GridWorld.reverseGetBlockDirection(remote, direction);
				float movementInDirection = displacementGrid.Dot(Base6Directions.GetVector(direction));
				if (movementInDirection > 0)
				{
					float scaleFactor = minForce / thrustProfile[direction];
					scaledMovement += movementInDirection * scaleFactor * Base6Directions.GetVector(direction);
					//log("direction is " + direction + " scaled movement is " + scaledMovement, "scaleByForce()", Logger.severity.TRACE);
				}
			}

			return RelativeVector3F.createFromGrid(scaledMovement, remote.CubeGrid);
		}

		/// <summary>
		/// the approximation that is used by this method is: stopping distance = speed * speed / (max acceleration * 2)
		/// </summary>
		/// <returns></returns>
		public float getStoppingDistance()
		{
			RelativeVector3F velocity = RelativeVector3F.createFromWorld(myGrid.Physics.LinearVelocity, myGrid);
			//Vector3 velocityWorld = myGrid.Physics.LinearVelocity;
			//Vector3 velocityGrid = GridWorld.relative_worldToGrid(myGrid, velocityWorld);

			//log("velocityWorld=" + velocityWorld + ", velocityGrid=" + velocityGrid, "getStoppingDistance()", Logger.severity.TRACE);

			float maxStopDistance = 0;
			foreach (Base6Directions.Direction direction in Base6Directions.EnumDirections) //Enum.GetValues(typeof(Base6Directions.Direction)))
			{
				float velocityInDirection = velocity.getGrid().Dot(Base6Directions.GetVector(direction));
				//log("velocityInDirection=" + velocityInDirection, "getStoppingDistance()", Logger.severity.TRACE);
				if (velocityInDirection < -0.1) // direction is opposite of velocityGrid
				{
					float acceleration = Math.Min(Math.Abs(thrustProfile[direction] / myGrid.Physics.Mass), Math.Abs(velocityInDirection / 2));
					//log("thrustProfile[direction].force = " + thrustProfile[direction].getForce() + ", max acceleration = " + thrustProfile[direction].getForce() / myGrid.Physics.Mass + ", half speed = " + velocityInDirection / 2 + ", acceleration = " + acceleration + ", myGrid.Physics.Mass = " + myGrid.Physics.Mass, "getStoppingDistance()", Logger.severity.TRACE);
					float stoppingDistance = velocityInDirection * velocityInDirection / (acceleration * 2);
					maxStopDistance = Math.Max(stoppingDistance, maxStopDistance);
					//log("velocityInDirection=" + velocityInDirection + ", acceleration=" + acceleration + ", stoppingDistance=" + stoppingDistance + ", maxStopDistance=" + maxStopDistance, "getStoppingDistance()", Logger.severity.TRACE);
				}
			}

			//log("maxStopDistance=" + maxStopDistance, "getStoppingDistance()", Logger.severity.TRACE);
			return maxStopDistance;
		}
	}
}
