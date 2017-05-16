using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using Rynchodon.Autopilot.Data;
using Rynchodon.Threading;
using Rynchodon.Utility;
using Rynchodon.Utility.Vectors;
using Sandbox.Common.ObjectBuilders;
using Sandbox.Game.Entities;
using Sandbox.ModAPI;
using Sandbox.ModAPI.Interfaces;
using VRage;
using VRage.Game.ModAPI;
using VRage.ModAPI;
using VRageMath;

namespace Rynchodon.Autopilot.Movement
{
	/// <summary>
	/// Tracks the direction and power of a grids thrusters.
	/// </summary>
	public class ThrustProfiler
	{

		public struct ForceInDirection
		{
			public Base6Directions.Direction Direction;
			public float Force;

			public override string ToString()
			{
				return "Direction: " + Direction + ", Force: " + Force;
			}
		}

		private const float maxThrustOverrideValue = 100f, minThrustOverrideValue = 1f;
		private static ITerminalProperty<float> TP_ThrustOverride;

		private IMyCubeBlock m_autopilot;
		private IMyCubeGrid myGrid;
		private MyPlanet m_planetAtmos;
		private float m_airDensity;
		private float? m_gravStrength;
		private ulong m_nextUpdate;

		private FastResourceLock lock_thrustersInDirection = new FastResourceLock();

		private List<MyThrust>[] m_thrustersInDirection = new List<MyThrust>[6];
		private float[] m_totalThrustForce = new float[6];

		/// <summary>Direction with strongest thrusters.</summary>
		private ForceInDirection m_primaryForce = new ForceInDirection() { Direction = Base6Directions.Direction.Forward };
		/// <summary>Direction, perpendicular to primary, with strongest thrusters.</summary>
		private ForceInDirection m_secondaryForce = new ForceInDirection() { Direction = Base6Directions.Direction.Up };

		public IMyCubeGrid Grid { get { return myGrid; } }

		/// <summary>Forward is the direction with the strongest thrusters and upward is the direction, perpendicular to forward, that has the strongest thrusters.</summary>
		public StandardFlight Standard { get; private set; }

		/// <summary>Upward is the direction with the strongest thrusters and forward is the direction, perpendicular to upward, that has the strongest thrusters.</summary>
		public StandardFlight Gravity { get; private set; }

		/// <summary>Maximum force of thrusters in direction of Standard's forward.</summary>
		public float PrimaryForce { get { return m_primaryForce.Force; } }

		/// <summary>Maximum force of thrusters in direction of Standard's upward.</summary>
		public float SecondaryForce { get { return m_secondaryForce.Force; } }

		/// <summary>Gravitational acceleration in grid space.</summary>
		public DirectionGrid LocalGravity { get; private set; }

		/// <summary>Gravitational acceleration in world space.</summary>
		public DirectionWorld WorldGravity { get; private set; }

		/// <summary>Thrust ratio to conteract gravity.</summary>
		public DirectionGrid GravityReactRatio { get; private set; }

		/// <summary>The ship has thrusters that can operate effectively in atmosphere.</summary>
		public bool CapableAtmo { get; private set; }

		/// <summary>The ship has thrusters that can operate effectively in space.</summary>
		public bool CapableSpace { get; private set; }

		public float GravityStrength
		{
			get
			{
				if (!m_gravStrength.HasValue)
					m_gravStrength = WorldGravity.vector.Length();
				return m_gravStrength.Value;
			}
		}

		private Logable Log { get { return new Logable(m_autopilot); } }

		public ThrustProfiler(IMyCubeBlock autopilot)
		{
			if (autopilot == null)
				throw new NullReferenceException("autopilot");

			m_autopilot = autopilot;
			myGrid = autopilot.CubeGrid;
			Standard = new StandardFlight(autopilot, Base6Directions.Direction.Forward, Base6Directions.Direction.Up);
			Gravity = new StandardFlight(autopilot, Base6Directions.Direction.Up, Base6Directions.Direction.Forward);

			for (int i = 0; i < 6; i++)
				m_thrustersInDirection[i] = new List<MyThrust>();

			CubeGridCache cache = CubeGridCache.GetFor(myGrid);
			if (cache == null)
				return;

			foreach (MyThrust thrust in cache.BlocksOfType(typeof(MyObjectBuilder_Thrust)))
				newThruster(thrust);

			myGrid.OnBlockAdded += grid_OnBlockAdded;
			myGrid.OnBlockRemoved += grid_OnBlockRemoved;

			MyAPIGateway.Utilities.InvokeOnGameThread(ClearOverrides);
		}

		/// <summary>
		/// Adds thruster to thrustersInDirection
		/// </summary>
		/// <param name="thruster">The new thruster</param>
		private void newThruster(MyThrust thruster)
		{
			Log.DebugLog("thruster == null", Logger.severity.ERROR, condition: thruster == null);

			if (TP_ThrustOverride == null)
				TP_ThrustOverride = thruster.GetProperty("Override") as ITerminalProperty<float>;

			using (lock_thrustersInDirection.AcquireExclusiveUsing())
				m_thrustersInDirection[(int)Base6Directions.GetFlippedDirection(thruster.Orientation.Forward)].Add(thruster);
			if (TP_ThrustOverride.GetValue(thruster) != 0f)
				TP_ThrustOverride.SetValue(thruster, 0f);
		}

		/// <summary>
		/// if added is a thruster, call newThruster()
		/// </summary>
		/// <param name="added">block that was added</param>
		private void grid_OnBlockAdded(IMySlimBlock added)
		{
			MyThrust thrust = added.FatBlock as MyThrust;

			if (thrust == null)
				return;

			try { newThruster(thrust); }
			catch (Exception e)
			{ Log.AlwaysLog("Exception: " + e, Logger.severity.ERROR); }
		}

		/// <summary>
		/// if removed is a thruster, remove it from thrustersInDirection
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

				using (lock_thrustersInDirection.AcquireExclusiveUsing())
					m_thrustersInDirection[(int)Base6Directions.GetFlippedDirection(asThrust.Orientation.Forward)].Remove(asThrust);
				Log.DebugLog("removed thruster = " + removed.FatBlock.DefinitionDisplayNameText + "/" + asThrust.DisplayNameText, Logger.severity.DEBUG);
				return;
			}
			catch (Exception e)
			{ Log.AlwaysLog("Exception: " + e, Logger.severity.ERROR); }
		}

		/// <summary>
		/// get the force in a direction
		/// </summary>
		/// <param name="direction">the direction of force / acceleration</param>
		public float GetForceInDirection(Base6Directions.Direction direction, bool adjustForGravity = false)
		{
			float force = m_totalThrustForce[(int)direction];

			if (adjustForGravity)
			{
				float change = Base6Directions.GetVector(direction).Dot(LocalGravity) * myGrid.Physics.Mass;
				//Log.DebugLog("For direction " + direction + ", and force " + force + ", Gravity adjusts available force by " + change + ", after adjustment: " + (force + change), "GetForceInDirection()");
				force += change;
			}

			//Log.DebugLog("direction: " + direction + "(" + (int)direction + ")" + ", force: " + force);
			return force;

			//return Math.Max(force, 1f); // a minimum of 1 N prevents dividing by zero
		}

		public void Update()
		{
			// sometimes called from Game Thread when world is loaded, has not been an issue so far
			//Log.DebugLog("Not on autopilot thread: " + ThreadTracker.ThreadName + ", from: " + callerPath + "." + callerMember, Logger.severity.ERROR, condition: !ThreadTracker.ThreadName.StartsWith("Autopilot"));

			if (Globals.UpdateCount < m_nextUpdate)
				return;
			m_nextUpdate = Globals.UpdateCount + ShipAutopilot.UpdateFrequency;

			//for (int i = m_totalThrustForce.Length - 1; i >= 0; i--)
			//	m_totalThrustForce[i] = 0f;

			CapableAtmo = false; CapableSpace = false;

			Vector3D position = myGrid.GetPosition();
			bool first = true;
			Vector3 worldGravity = Vector3.Zero;
			m_planetAtmos = null;
			m_airDensity = 0f;

			foreach (MyPlanet planet in Globals.AllPlanets())
				if (planet.IsPositionInGravityWell(position))
				{
					if (first)
					{
						first = false;
						m_gravStrength = planet.GetGravityMultiplier(position) * 9.81f;
						Vector3 direction = planet.GetWorldGravityNormalized(ref position);
						worldGravity = m_gravStrength.Value * direction;
					}
					else
					{
						worldGravity += planet.GetWorldGravity(position);
						m_gravStrength = null;
					}
					if (planet.HasAtmosphere)
					{
						m_airDensity += planet.GetAirDensity(position);
						m_planetAtmos = planet;
					}
				}

			CalcForceInDirection(Base6Directions.Direction.Forward);
			CalcForceInDirection(Base6Directions.Direction.Backward);
			CalcForceInDirection(Base6Directions.Direction.Up);
			CalcForceInDirection(Base6Directions.Direction.Down);
			CalcForceInDirection(Base6Directions.Direction.Left);
			CalcForceInDirection(Base6Directions.Direction.Right);

			if (worldGravity.LengthSquared() < 0.01f)
			{
				//Log.DebugLog("Not in gravity well", "Update()");
				WorldGravity = Vector3.Zero;
				LocalGravity = Vector3.Zero;
				m_gravStrength = 0f;
				return;
			}
			WorldGravity = worldGravity;
			LocalGravity = new DirectionGrid() { vector = Vector3.Transform(worldGravity, myGrid.WorldMatrixNormalizedInv.GetOrientation()) };

			Vector3 gravityReactRatio = Vector3.Zero;
			if (LocalGravity.vector.X > 0)
				gravityReactRatio.X = -LocalGravity.vector.X * myGrid.Physics.Mass / GetForceInDirection(Base6Directions.Direction.Left);
			else
				gravityReactRatio.X = -LocalGravity.vector.X * myGrid.Physics.Mass / GetForceInDirection(Base6Directions.Direction.Right);
			if (LocalGravity.vector.Y > 0)
				gravityReactRatio.Y = -LocalGravity.vector.Y * myGrid.Physics.Mass / GetForceInDirection(Base6Directions.Direction.Down);
			else
				gravityReactRatio.Y = -LocalGravity.vector.Y * myGrid.Physics.Mass / GetForceInDirection(Base6Directions.Direction.Up);
			if (LocalGravity.vector.Z > 0)
				gravityReactRatio.Z = -LocalGravity.vector.Z * myGrid.Physics.Mass / GetForceInDirection(Base6Directions.Direction.Forward);
			else
				gravityReactRatio.Z = -LocalGravity.vector.Z * myGrid.Physics.Mass / GetForceInDirection(Base6Directions.Direction.Backward);
			GravityReactRatio = gravityReactRatio;

			Log.DebugLog("Gravity: " + WorldGravity + ", local: " + LocalGravity + ", react: " + gravityReactRatio + ", air density: " + m_airDensity);
		}

		private float GetThrusterMaxForce(MyThrust thruster)
		{
			float thrusterForce = thruster.BlockDefinition.ForceMagnitude * (thruster as IMyThrust).ThrustMultiplier;
			if (thruster.BlockDefinition.EffectivenessAtMaxInfluence != 1f || thruster.BlockDefinition.EffectivenessAtMinInfluence != 1f)
			{
				if (!CapableAtmo && thruster.BlockDefinition.EffectivenessAtMaxInfluence > thruster.BlockDefinition.EffectivenessAtMinInfluence)
					CapableAtmo = true;

				if (!CapableSpace && thruster.BlockDefinition.EffectivenessAtMinInfluence > thruster.BlockDefinition.EffectivenessAtMaxInfluence)
					CapableSpace = true;

				if (m_airDensity <= thruster.BlockDefinition.MinPlanetaryInfluence)
					thrusterForce *= thruster.BlockDefinition.EffectivenessAtMinInfluence;
				else if (m_airDensity >= thruster.BlockDefinition.MaxPlanetaryInfluence)
					thrusterForce *= thruster.BlockDefinition.EffectivenessAtMaxInfluence;
				else
				{
					float effectRange = thruster.BlockDefinition.EffectivenessAtMaxInfluence - thruster.BlockDefinition.EffectivenessAtMinInfluence;
					float influenceRange = thruster.BlockDefinition.MaxPlanetaryInfluence - thruster.BlockDefinition.MinPlanetaryInfluence;
					float effectiveness = (m_airDensity - thruster.BlockDefinition.MinPlanetaryInfluence) * effectRange / influenceRange + thruster.BlockDefinition.EffectivenessAtMinInfluence;
					//Log.DebugLog("for thruster " + thruster.DisplayNameText + ", effectiveness: " + effectiveness + ", max force: " + thrusterForce + ", effect range: " + effectRange + ", influence range: " + influenceRange);
					thrusterForce *= effectiveness;
				}
			}
			else
			{
				CapableAtmo = true;
				CapableSpace = true;
			}
			return thrusterForce;
		}

		private float CalcForceInDirection(Base6Directions.Direction direction)
		{
			float force = 0;
			using (lock_thrustersInDirection.AcquireSharedUsing())
				foreach (MyThrust thruster in m_thrustersInDirection[(int)direction])
					if (!thruster.Closed && thruster.IsWorking)
						force += GetThrusterMaxForce(thruster);

			m_totalThrustForce[(int)direction] = force;

			if (direction == m_primaryForce.Direction)
			{
				//Log.DebugLog("updating primary force, direction: " + direction + ", force: " + force, "CalcForceInDirection()");
				m_primaryForce.Force = force;
			}
			else if (force > m_primaryForce.Force * 1.1f)
			{
				Log.DebugLog("stronger than primary force, direction: " + direction + ", force: " + force + ", acceleration: " + force / myGrid.Physics.Mass + ", primary: " + m_primaryForce, Logger.severity.DEBUG);
				m_secondaryForce = m_primaryForce;
				m_primaryForce.Direction = direction;
				m_primaryForce.Force = force;

				if (m_secondaryForce.Direction == Base6Directions.GetFlippedDirection(m_primaryForce.Direction))
					m_secondaryForce = new ForceInDirection() { Direction = Base6Directions.GetPerpendicular(m_primaryForce.Direction) };

				Log.DebugLog("secondary: " + m_secondaryForce);

				Standard.SetMatrixOrientation(m_primaryForce.Direction, m_secondaryForce.Direction);
				Gravity.SetMatrixOrientation(m_secondaryForce.Direction, m_primaryForce.Direction);
			}
			else if (direction == m_secondaryForce.Direction)
			{
				//Log.DebugLog("updating secondary force, direction: " + direction + ", force: " + force, "CalcForceInDirection()");
				m_secondaryForce.Force = force;
			}
			else if (force > m_secondaryForce.Force * 1.1f && direction != Base6Directions.GetFlippedDirection(m_primaryForce.Direction))
			{
				Log.DebugLog("stronger than secondary force, direction: " + direction + ", force: " + force + ", acceleration: " + force / myGrid.Physics.Mass + ", secondary: " + m_secondaryForce, Logger.severity.DEBUG);
				m_secondaryForce.Direction = direction;
				m_secondaryForce.Force = force;
				Standard.SetMatrixOrientation(m_primaryForce.Direction, m_secondaryForce.Direction);
				Gravity.SetMatrixOrientation(m_secondaryForce.Direction, m_primaryForce.Direction);
			}

			//Log.DebugLog("direction: " + direction + "(" + (int)direction + ")" + ", force: " + force);
			return force;
		}

		#region Override

		/// <summary>
		/// Set the overrides of thrusters to match MoveForceRatio. Should be called on game thread.
		/// </summary>
		public void SetOverrides(ref DirectionGrid MoveForceRatio)
		{
			if (MoveForceRatio.vector.X >= 0f)
			{
				SetOverrides(Base6Directions.Direction.Right, MoveForceRatio.vector.X);
				ClearOverrides(Base6Directions.Direction.Left);
			}
			else
			{
				ClearOverrides(Base6Directions.Direction.Right);
				SetOverrides(Base6Directions.Direction.Left, -MoveForceRatio.vector.X);
			}

			if (MoveForceRatio.vector.Y >= 0f)
			{
				SetOverrides(Base6Directions.Direction.Up, MoveForceRatio.vector.Y);
				ClearOverrides(Base6Directions.Direction.Down);
			}
			else
			{
				ClearOverrides(Base6Directions.Direction.Up);
				SetOverrides(Base6Directions.Direction.Down, -MoveForceRatio.vector.Y);
			}

			if (MoveForceRatio.vector.Z >= 0f)
			{
				SetOverrides(Base6Directions.Direction.Backward, MoveForceRatio.vector.Z);
				ClearOverrides(Base6Directions.Direction.Forward);
			}
			else
			{
				ClearOverrides(Base6Directions.Direction.Backward);
				SetOverrides(Base6Directions.Direction.Forward, -MoveForceRatio.vector.Z);
			}
		}

		/// <summary>
		/// Set all overrides to zero. Should be called on game thread.
		/// </summary>
		public void ClearOverrides()
		{
			ClearOverrides(Base6Directions.Direction.Right);
			ClearOverrides(Base6Directions.Direction.Left);
			ClearOverrides(Base6Directions.Direction.Up);
			ClearOverrides(Base6Directions.Direction.Down);
			ClearOverrides(Base6Directions.Direction.Backward);
			ClearOverrides(Base6Directions.Direction.Forward);
		}

		/// <summary>
		/// Sets the overrides in a direction to match a particular force ratio.
		/// </summary>
		private void SetOverrides(Base6Directions.Direction direction, float ratio)
		{
			float force = GetForceInDirection(direction) * ratio;

			// no need to lock thrustersInDirection, it is updated on game thread
			foreach (MyThrust thruster in m_thrustersInDirection[(int)direction])
				if (!thruster.Closed && thruster.IsWorking)
				{
					if (force <= 0f)
					{
						if (TP_ThrustOverride.GetValue(thruster) != 0f)
							TP_ThrustOverride.SetValue(thruster, 0f);
						continue;
					}

					float maxForce = GetThrusterMaxForce(thruster);
					if (maxForce > force)
					{
						float overrideValue = Math.Max(force / maxForce * maxThrustOverrideValue, minThrustOverrideValue);
						if (TP_ThrustOverride.GetValue(thruster) != overrideValue)
							TP_ThrustOverride.SetValue(thruster, overrideValue);
						//Log.DebugLog("direction: " + direction + ", thruster: " + thruster.DisplayNameText + ", add partial force " + force + " of " + maxForce + ", overrideValue: " + overrideValue, "SetOverrides()");
						force = 0f;
					}
					else
					{
						if (TP_ThrustOverride.GetValue(thruster) != maxThrustOverrideValue)
							TP_ThrustOverride.SetValue(thruster, maxThrustOverrideValue);
						force -= maxForce;
						//Log.DebugLog("direction: " + direction + ", thruster at full force: " + thruster.DisplayNameText, "SetOverrides()");
					}
				}
		}

		/// <summary>
		/// Clears all overrides in a particular direcion.
		/// </summary>
		private void ClearOverrides(Base6Directions.Direction direction)
		{
			// no need to lock thrustersInDirection, it is updated on game thread
			foreach (MyThrust thruster in m_thrustersInDirection[(int)direction])
				if (!thruster.Closed && thruster.IsWorking && TP_ThrustOverride.GetValue(thruster) != 0f)
					TP_ThrustOverride.SetValue(thruster, 0f);
		}

		#endregion Override

		/// <summary>
		/// Determines if the ship has enough force to accelerate in the specified direction. Checks against gravity.
		/// </summary>
		/// <param name="accelertation">The minimum acceleration required, in m/s/s</param>
		public bool CanMoveDirection(Base6Directions.Direction direction, float acceleration = 1f)
		{
			Update();
			return GetForceInDirection(direction, true) > Grid.Physics.Mass * acceleration;
		}

		/// <summary>
		/// Determines if the ship has enough force to accelerate forward. Checks against gravity.
		/// </summary>
		/// <param name="acceleration">The ammount of acceleration required, in m/s/s</param>
		public bool CanMoveForward(float acceleration = 1f)
		{
			Update();
			return GetForceInDirection(Base6Directions.GetDirection(Standard.LocalMatrix.Forward), true) > Grid.Physics.Mass * acceleration;
		}

		/// <summary>
		/// Determines if the ship has enough force to move in any direction. Checks against gravity.
		/// </summary>
		/// <param name="acceleration">The minimum acceleration required, in m/s/s</param>
		public bool CanMoveAnyDirection(float acceleration = 1f)
		{
			Update();

			float force = Grid.Physics.Mass * acceleration;
			foreach (Base6Directions.Direction direction in Base6Directions.EnumDirections)
				if (GetForceInDirection(direction, true) < force)
				{
					Log.DebugLog("Limited thrust in direction: " + direction);
					return false;
				}

			return true;
		}

	}
}
