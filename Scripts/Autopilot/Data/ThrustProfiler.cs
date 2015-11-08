using System;
using System.Collections.Generic;
using Sandbox.Definitions;
using Sandbox.ModAPI;
using VRageMath;

namespace Rynchodon.Autopilot.Data
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
			///// <summary>force * 10</summary>
			//public readonly float dampingForce;
			/// <summary>direction the thruster will move the ship in</summary>
			public readonly Base6Directions.Direction forceDirect;
			public readonly IMyThrust thruster;

			public ThrusterProperties(IMyThrust thruster)
			{
				thruster.throwIfNull_argument("thruster");

				this.thruster = thruster;
				this.force = (DefinitionCache.GetCubeBlockDefinition(thruster) as MyThrustDefinition).ForceMagnitude;
				//this.dampingForce = force * 10;
				this.forceDirect = Base6Directions.GetFlippedDirection(thruster.Orientation.Forward);
			}

			public override string ToString()
			{ return "force = " + force + ", direction = " + forceDirect; }
		}

		private IMyCubeGrid myGrid;
		private Dictionary<IMyThrust, ThrusterProperties> allMyThrusters;
		private Dictionary<Base6Directions.Direction, List<ThrusterProperties>> thrustersInDirection;

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

		///// <summary>
		///// get the damping force in a direction
		///// </summary>
		///// <param name="direction">the direction of force / acceleration</param>
		//public float GetDampingInDirection(Base6Directions.Direction direction)
		//{
		//	float dampingForce = 0;
		//	foreach (ThrusterProperties thruster in thrustersInDirection[direction])
		//		if (!thruster.thruster.Closed && thruster.thruster.IsWorking)
		//			dampingForce += thruster.dampingForce * thruster.thruster.ThrustMultiplier;

		//	return dampingForce;
		//}

		/// <summary>
		/// get the force in a direction
		/// </summary>
		/// <param name="direction">the direction of force / acceleration</param>
		public float GetForceInDirection(Base6Directions.Direction direction)
		{
			float force = 0;
			foreach (ThrusterProperties thruster in thrustersInDirection[direction])
				if (!thruster.thruster.Closed && thruster.thruster.IsWorking)
					force += thruster.force * thruster.thruster.ThrustMultiplier;

			return force;
		}

		/// <summary>
		/// Determines if there are working thrusters in every direction.
		/// </summary>
		public bool CanMoveAnyDirection()
		{
			foreach (List<ThrusterProperties> thrusterGroup in thrustersInDirection.Values)
			{
				bool found = false;
				foreach (ThrusterProperties prop in thrusterGroup)
					if (!prop.thruster.Closed && prop.thruster.IsWorking)
					{
						found = true;
						break;
					}
				if (!found)
					return false;
			}
			return true;
		}

	}
}
