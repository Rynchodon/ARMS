#define LOG_ENABLED //remove on build

using System;
using System.Collections.Generic;
using Sandbox.Definitions;
using Sandbox.ModAPI;
using VRage.ObjectBuilders;
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

		/// <summary>
		/// it is less expensive to remember some information about thrusters
		/// </summary>
		private class ThrusterProperties
		{
			/// <summary>from definition</summary>
			public readonly float force;
			/// <summary>force * 10</summary>
			public readonly float dampingForce;
			/// <summary>direction of force</summary>
			public readonly Base6Directions.Direction forceDirect;
			public readonly IMyThrust thruster;

			public ThrusterProperties(IMyThrust thruster)
			{
				thruster.throwIfNull_argument("thruster");

				this.thruster = thruster;
				this.force = (DefinitionCache.GetCubeBlockDefinition(thruster) as MyThrustDefinition).ForceMagnitude;
				this.dampingForce = force * 10;
				this.forceDirect = Base6Directions.GetFlippedDirection(thruster.Orientation.Forward);
			}

			public override string ToString()
			{ return "force = " + force + ", direction = " + forceDirect; }
		}

		private IMyCubeGrid myGrid;
		private Dictionary<IMyThrust, ThrusterProperties> allMyThrusters;
		private Dictionary<Base6Directions.Direction, List<ThrusterProperties>> thrustersInDirection;

		/// <summary>
		/// Thrusters which are disabled by Autopilot and should not be removed from profile.
		/// </summary>
		private HashSet<IMyThrust> thrustersDisabledByAutopilot = new HashSet<IMyThrust>();

		public ThrustProfiler(IMyCubeGrid grid)
		{
			if (grid == null)
				throw new NullReferenceException("grid");

			myLogger = new Logger("ThrustProfiler", () => grid.DisplayName);
			myLogger = new Logger(grid.DisplayName, "ThrustProfiler");
			myGrid = grid;

			init();
		}

		private void init()
		{
			myLogger.debugLog("initializing", "init()", Logger.severity.TRACE);
			allMyThrusters = new Dictionary<IMyThrust, ThrusterProperties>();

			thrustersInDirection = new Dictionary<Base6Directions.Direction, List<ThrusterProperties>>();
			foreach (Base6Directions.Direction direction in Base6Directions.EnumDirections)
				thrustersInDirection.Add(direction, new List<ThrusterProperties>());

			List<IMySlimBlock> thrusters = new List<IMySlimBlock>();
			myGrid.GetBlocks(thrusters, block => block.FatBlock != null && block.FatBlock is IMyThrust); // .BlockDefinition.TypeId == thrusterID);
			foreach (IMySlimBlock thrust in thrusters)
				newThruster(thrust);

			myGrid.OnBlockAdded += grid_OnBlockAdded;
			myGrid.OnBlockRemoved += grid_OnBlockRemoved;
		}

		/// <summary>
		/// Creates properties for a thruster and adds it to allMyThrusters. If the thruster is working, calls enableDisableThruster(true).
		/// </summary>
		/// <param name="thruster">The new thruster</param>
		private void newThruster(IMySlimBlock thruster)
		{
			float dampingForce = 10 * (DefinitionCache.GetCubeBlockDefinition(thruster.FatBlock) as MyThrustDefinition).ForceMagnitude;
			ThrusterProperties properties = new ThrusterProperties(thruster.FatBlock as IMyThrust);
			allMyThrusters.Add(thruster.FatBlock as IMyThrust, properties);
			thrustersInDirection[properties.forceDirect].Add(properties);
			return;
		}

		/// <summary>
		/// if added is a thruster, call newThruster()
		/// </summary>
		/// <param name="added">block that was added</param>
		private void grid_OnBlockAdded(IMySlimBlock added)
		{
			MainLock.MainThread_ReleaseExclusive();
			try
			{
				if (added.FatBlock == null)
					return;

				if (added.FatBlock is IMyThrust)
					newThruster(added);
			}
			catch (Exception e)
			{ myLogger.alwaysLog("Exception: " + e, "grid_OnBlockAdded()", Logger.severity.ERROR); }
			finally
			{ MainLock.MainThread_AcquireExclusive(); }
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

				IMyThrust asThrust = removed.FatBlock as IMyThrust;
				if (asThrust == null)
					return;

				ThrusterProperties properties;
				if (allMyThrusters.TryGetValue(asThrust, out properties))
				{
					allMyThrusters.Remove(asThrust);
					thrustersInDirection[properties.forceDirect].Remove(properties);
					myLogger.debugLog("removed thruster = " + removed.FatBlock.DefinitionDisplayNameText + ", " + properties, "grid_OnBlockRemoved()", Logger.severity.DEBUG);
					return;
				}
				myLogger.debugLog("could not get properties for thruster", "grid_OnBlockRemoved()", Logger.severity.ERROR);
			}
			catch (Exception e)
			{ myLogger.alwaysLog("Exception: " + e, "grid_OnBlockRemoved()", Logger.severity.ERROR); }
		}

		/// <summary>
		/// get the damping force in a direction
		/// </summary>
		private float GetDampingInDirection(Base6Directions.Direction direction)
		{
			float dampingForce = 0;
			foreach (ThrusterProperties thruster in thrustersInDirection[direction])
				if (!thruster.thruster.Closed && (thruster.thruster.IsWorking || thrustersDisabledByAutopilot.Contains(thruster.thruster)))
					dampingForce += thruster.dampingForce * thruster.thruster.ThrustMultiplier;

			return dampingForce;
		}

		/// <summary>
		/// get the force in a direction
		/// </summary>
		private float GetForceInDirection(Base6Directions.Direction direction)
		{
			float force = 0;
			foreach (ThrusterProperties thruster in thrustersInDirection[direction])
				if (!thruster.thruster.Closed && (thruster.thruster.IsWorking || thrustersDisabledByAutopilot.Contains(thruster.thruster)))
					force += thruster.force * thruster.thruster.ThrustMultiplier;

			return force;
		}

		#region Public Methods

		/// <summary>
		/// scales a movement vector by available thrust force
		/// </summary>
		/// <param name="displacement">displacement vector</param>
		/// <param name="remote">controlling remote</param>
		/// <returns>scaled vector</returns>
		public RelativeDirection3F scaleByForce(RelativeDirection3F displacement, IMyCubeBlock remote)
		{
			allMyThrusters.throwIfNull_variable("allMyThrusters");

			Vector3 displacementGrid = displacement.ToLocal();

			Dictionary<Base6Directions.Direction, float> directionalForces = new Dictionary<Base6Directions.Direction, float>();

			// find determinant thrust
			float minForce = float.MaxValue;
			foreach (Base6Directions.Direction direction in Base6Directions.EnumDirections)
			{
				float movementInDirection = displacementGrid.Dot(Base6Directions.GetVector(direction));
				if (movementInDirection > 0)
				{
					float inDirection = GetForceInDirection(direction);
					directionalForces.Add(direction, inDirection);
					minForce = Math.Min(minForce, inDirection);
				}
			}

			// scale thrust to min
			Vector3 scaledMovement = Vector3.Zero;
			foreach (Base6Directions.Direction direction in Base6Directions.EnumDirections)
			{
				float movementInDirection = displacementGrid.Dot(Base6Directions.GetVector(direction));
				if (movementInDirection > 0)
				{
					float forceInDirection = directionalForces[direction];
					float scaleFactor = minForce / forceInDirection;
					scaledMovement += movementInDirection * scaleFactor * Base6Directions.GetVector(direction);
				}
			}

			return RelativeDirection3F.FromLocal(remote.CubeGrid, scaledMovement);
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
			allMyThrusters.throwIfNull_variable("allMyThrusters");

			RelativeVector3F velocity = RelativeVector3F.createFromWorld(myGrid.Physics.LinearVelocity, myGrid);

			float maxStopDistance = 0;
			foreach (Base6Directions.Direction direction in Base6Directions.EnumDirections) //Enum.GetValues(typeof(Base6Directions.Direction)))
			{
				float velocityInDirection = velocity.getLocal().Dot(Base6Directions.GetVector(direction));
				if (velocityInDirection < -0.1) // direction is opposite of velocityGrid
				{
					float acceleration = Math.Min(Math.Abs(GetDampingInDirection(direction) / myGrid.Physics.Mass), Math.Abs(velocityInDirection / 2));
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
			if (allMyThrusters == null)
				return;

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
			if (allMyThrusters == null)
				return;

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
