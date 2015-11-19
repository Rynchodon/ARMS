using System;
using System.Collections.Generic;
using Sandbox.Definitions;
using Sandbox.Game.Entities;
using Sandbox.ModAPI;
using VRageMath;

namespace Rynchodon.Autopilot.Data
{
	/// <summary>
	/// Tracks the direction and power of a grids thrusters.
	/// </summary>
	public class ThrustProfiler
	{
		private Logger myLogger = null;

		private IMyCubeGrid myGrid;
		private Dictionary<Base6Directions.Direction, List<MyThrust>> thrustersInDirection = new Dictionary<Base6Directions.Direction, List<MyThrust>>();
		private float m_airDensity;
		private ulong m_nextUpdate_gravityAndAir;

		public ThrustProfiler(IMyCubeGrid grid)
		{
			if (grid == null)
				throw new NullReferenceException("grid");

			myLogger = new Logger("ThrustProfiler", () => grid.DisplayName);
			myGrid = grid;

			foreach (Base6Directions.Direction direction in Base6Directions.EnumDirections)
				thrustersInDirection.Add(direction, new List<MyThrust>());

			List<IMySlimBlock> thrusters = new List<IMySlimBlock>();
			myGrid.GetBlocks(thrusters, block => block.FatBlock != null && block.FatBlock is IMyThrust);
			foreach (IMySlimBlock thrust in thrusters)
				newThruster(thrust);

			myGrid.OnBlockAdded += grid_OnBlockAdded;
			myGrid.OnBlockRemoved += grid_OnBlockRemoved;
		}

		/// <summary>Gravitation acceleration</summary>
		public Vector3 m_worldGravity { get; private set; }
		/// <summary>Gravitational acceleration transformed to local.</summary>
		public Vector3 m_localGravity { get; private set; }
		/// <summary>Thrust ratio to conteract gravity.</summary>
		public Vector3 m_gravityReactRatio { get; private set; }

		/// <summary>
		/// Creates properties for a thruster and adds it to allMyThrusters. If the thruster is working, calls enableDisableThruster(true).
		/// </summary>
		/// <param name="thruster">The new thruster</param>
		private void newThruster(IMySlimBlock thruster)
		{
			thrustersInDirection[Base6Directions.GetFlippedDirection(thruster.FatBlock.Orientation.Forward)].Add(thruster.FatBlock as MyThrust);
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

				MyThrust asThrust = removed.FatBlock as MyThrust;
				if (asThrust == null)
					return;

				thrustersInDirection[Base6Directions.GetFlippedDirection(asThrust.Orientation.Forward)].Remove(asThrust);
				myLogger.debugLog("removed thruster = " + removed.FatBlock.DefinitionDisplayNameText + "/" + asThrust.DisplayNameText, "grid_OnBlockRemoved()", Logger.severity.DEBUG);
				return;
			}
			catch (Exception e)
			{ myLogger.alwaysLog("Exception: " + e, "grid_OnBlockRemoved()", Logger.severity.ERROR); }
		}

		/// <summary>
		/// get the force in a direction
		/// </summary>
		/// <param name="direction">the direction of force / acceleration</param>
		public float GetForceInDirection(Base6Directions.Direction direction, bool adjustForGravity = true)
		{
			float force = 0;
			foreach (MyThrust thruster in thrustersInDirection[direction])
				if (!thruster.Closed && thruster.IsWorking)
				{
					float thrusterForce = thruster.BlockDefinition.ForceMagnitude * (thruster as IMyThrust).ThrustMultiplier;
					if (thruster.BlockDefinition.NeedsAtmosphereForInfluence)
						if (m_airDensity <= thruster.BlockDefinition.MinPlanetaryInfluence)
							thrusterForce *= thruster.BlockDefinition.EffectivenessAtMinInfluence;
						else if (m_airDensity >= thruster.BlockDefinition.MaxPlanetaryInfluence)
							thrusterForce *= thruster.BlockDefinition.EffectivenessAtMaxInfluence;
						else
						{
							float effectRange = thruster.BlockDefinition.EffectivenessAtMaxInfluence - thruster.BlockDefinition.EffectivenessAtMinInfluence;
							float influenceRange = thruster.BlockDefinition.MaxPlanetaryInfluence - thruster.BlockDefinition.MinPlanetaryInfluence;
							float effectiveness = (m_airDensity - thruster.BlockDefinition.MinPlanetaryInfluence) * effectRange / influenceRange + thruster.BlockDefinition.EffectivenessAtMinInfluence;
							//myLogger.debugLog("For thruster " + thruster.DisplayNameText + ", effectiveness: " + effectiveness + ", max force: " + thrusterForce + ", effect range: " + effectRange + ", influence range: " + influenceRange, "GetForceInDirection()");
							thrusterForce *= effectiveness;
						}
					force += thrusterForce;
				}

			if (adjustForGravity)
			{
				float change = Base6Directions.GetVector(direction).Dot(m_localGravity) * myGrid.Physics.Mass;
				myLogger.debugLog("For direction " + direction + ", and force " + force + ", Gravity adjusts available force by " + change + ", after adjustment: " + (force + change), "GetForceInDirection()");
				force += change;
			}

			return Math.Max(force, 1f); // a minimum of 1 N prevents dividing by zero
		}

		/// <summary>
		/// Determines if there are working thrusters in every direction.
		/// </summary>
		public bool CanMoveAnyDirection()
		{
			foreach (List<MyThrust> thrusterGroup in thrustersInDirection.Values)
			{
				bool found = false;
				foreach (MyThrust prop in thrusterGroup)
					if (!prop.Closed && prop.IsWorking)
					{
						found = true;
						break;
					}
				if (!found)
					return false;
			}
			return true;
		}

		public void UpdateGravityAndAir()
		{
			if (Globals.UpdateCount < m_nextUpdate_gravityAndAir)
				return;

			Vector3D position = myGrid.GetPosition();
			m_worldGravity = Vector3.Zero;
			m_airDensity = 0f;
			List<IMyVoxelBase> allPlanets = MyAPIGateway.Session.VoxelMaps.GetInstances_Safe(voxel => voxel is MyPlanet);
			foreach (MyPlanet planet in allPlanets)
				if (planet.IsPositionInRangeGrid(position))
				{
					m_worldGravity += planet.GetWorldGravityGrid(position);
					if (planet.HasAtmosphere)
						m_airDensity += planet.GetAirDensity(position);
				}

			if (m_worldGravity.LengthSquared() < 0.01f)
			{
				myLogger.debugLog("Not in gravity well", "UpdateGravityAndAir()");
				m_localGravity = Vector3.Zero;
				m_gravityReactRatio = Vector3.Zero;
				m_nextUpdate_gravityAndAir = Globals.UpdateCount + ShipController_Autopilot.UpdateFrequency;
				return;
			}

			m_localGravity = Vector3.Transform(m_worldGravity, myGrid.WorldMatrixNormalizedInv.GetOrientation());

			Vector3 gravityReactRatio = Vector3.Zero;
			if (m_localGravity.X > 0)
				gravityReactRatio.X = -m_localGravity.X * myGrid.Physics.Mass / GetForceInDirection(Base6Directions.Direction.Left, false);
			else
				gravityReactRatio.X = -m_localGravity.X * myGrid.Physics.Mass / GetForceInDirection(Base6Directions.Direction.Right, false);
			if (m_localGravity.Y > 0)
				gravityReactRatio.Y = -m_localGravity.Y * myGrid.Physics.Mass / GetForceInDirection(Base6Directions.Direction.Down, false);
			else
				gravityReactRatio.Y = -m_localGravity.Y * myGrid.Physics.Mass / GetForceInDirection(Base6Directions.Direction.Up, false);
			if (m_localGravity.Z > 0)
				gravityReactRatio.Z = -m_localGravity.Z * myGrid.Physics.Mass / GetForceInDirection(Base6Directions.Direction.Forward, false);
			else
				gravityReactRatio.Z = -m_localGravity.Z * myGrid.Physics.Mass / GetForceInDirection(Base6Directions.Direction.Backward, false);
			m_gravityReactRatio = gravityReactRatio;

			myLogger.debugLog("Gravity: " + m_worldGravity + ", local: " + m_localGravity + ", react: " + gravityReactRatio + ", air density: " + m_airDensity, "UpdateGravityAndAir()");
			m_nextUpdate_gravityAndAir = Globals.UpdateCount + ShipController_Autopilot.UpdateFrequency;
		}

	}
}
