using System;
using System.Collections.Generic;
using System.Text;
using Rynchodon.Autopilot.Data;
using Rynchodon.Autopilot.Movement;
using Rynchodon.Autopilot.Pathfinding;
using Rynchodon.Utility;
using Sandbox.Game.Entities;
using Sandbox.ModAPI;
using VRage.Game.ModAPI;
using VRage.ModAPI;
using VRageMath;

namespace Rynchodon.Autopilot.Navigator
{
	/// <summary>
	/// "Orbits" an entity. For a planet, this can be a real orbit.
	/// </summary>
	public class Orbiter : NavigatorMover, INavigatorRotator
  {

		public readonly string m_orbitEntity_name;

		/// <summary>Distance from point being orbited. If orbiting a grid in gravity, the orbited point will usually be above the grid. </summary>
		public float Altitude;
		public float OrbitSpeed { get; private set; }

		private readonly GridFinder m_gridFinder;
		private IMyEntity value_orbitEntity;
		private Vector3 m_orbitAxis;
		private Vector3 m_targetPositionOffset = Vector3.Zero;
		private bool m_flyTo = true;

		private Vector3 m_faceDirection;

		private Logable Log
		{ get { return new Logable(m_controlBlock?.CubeBlock); } }
		private IMyEntity OrbitEntity
		{
			get { return value_orbitEntity; }
			set
			{
				value_orbitEntity = value;
				if (value == null)
				{
					Altitude = 0f;
					m_targetPositionOffset = Vector3.Zero;
					return;
				}

				m_navSet.Settings_Task_NavMove.DestinationEntity = value;

				// if orbiting a grid near the surface of a planet, avoid going below it
				if (value is IMyCubeGrid)
				{
					Vector3D navBlockPos = m_navBlock.WorldPosition;
					double distSquared;
					MyPlanet closest = MyPlanetExtensions.GetClosestPlanet(navBlockPos, out distSquared);
					Log.DebugLog(() => "distance to closest: " + Math.Sqrt(distSquared) + ", MaximumRadius: " + closest.MaximumRadius, condition: closest != null);
					if (closest != null && distSquared < closest.MaximumRadius * closest.MaximumRadius)
					{
						Vector3D targetCentre = value.GetCentre();
						m_orbitAxis = Vector3D.Normalize(targetCentre - closest.GetCentre());

						const float heightRatio = 0.866f; // sin(60°), target can be at most 60° below horizontal
						float maxAxisHeight = Altitude < 1f ? (float)Vector3D.Distance(navBlockPos, targetCentre) * heightRatio : Altitude * heightRatio;

						Line orbitAxis = new Line(Vector3.Zero, m_orbitAxis * maxAxisHeight);
						Vector3D closestPoint = orbitAxis.ClosestPoint(navBlockPos - targetCentre) + targetCentre;
						m_targetPositionOffset = closestPoint - targetCentre;

						if (Altitude < 1f)
							Altitude = (float)Vector3D.Distance(navBlockPos, closestPoint);
						else
							Altitude = (float)Math.Sqrt(Altitude * Altitude - Vector3D.DistanceSquared(closestPoint, targetCentre));

						Log.DebugLog("near a planet, circling at constant distance to planet, altitude: " + Altitude + ", axis: " + m_orbitAxis + ", target centre: " +
							targetCentre + ", closestPoint: " + closestPoint + ", target offset: " + m_targetPositionOffset, Logger.severity.DEBUG);
						return;
					}
				}
				if (Altitude < 1f)
					Altitude = (float)Vector3D.Distance(m_navBlock.WorldPosition, value_orbitEntity.GetCentre());
				Vector3D toTarget = value_orbitEntity.GetCentre() - m_navBlock.WorldPosition;
				toTarget.Normalize();
				m_orbitAxis = VRage.Utils.MyUtils.GetRandomPerpendicularVector(ref toTarget);
				m_orbitAxis.Normalize();

				Log.DebugLog("altitude: " + Altitude + ", axis: " + m_orbitAxis, Logger.severity.DEBUG);
			}
		}

		public Orbiter(Pathfinder pathfinder, string entity)
			: base(pathfinder)
		{
			switch (entity.LowerRemoveWhitespace())
			{
				case "asteroid":
					SetOrbitClosestVoxel(true);
					CalcFakeOrbitSpeedForce();
					Log.DebugLog("Orbiting asteroid: " + OrbitEntity.getBestName(), Logger.severity.INFO);
					break;
				case "planet":
					SetOrbitClosestVoxel(false);
					OrbitSpeed = (float)Math.Sqrt((OrbitEntity as MyPlanet).GetGravityMultiplier(m_navBlock.WorldPosition) * 9.81f * Altitude);
					if (OrbitSpeed < 1f)
						CalcFakeOrbitSpeedForce();
					Log.DebugLog("Orbiting planet: " + OrbitEntity.getBestName(), Logger.severity.INFO);
					break;
				default:
					m_gridFinder = new GridFinder(pathfinder.NavSet, m_mover.Block, entity, mustBeRecent: true);
					Log.DebugLog("Searching for a grid: " + entity, Logger.severity.INFO);
					break;
			}

			m_navSet.Settings_Task_NavMove.NavigatorMover = this;
		}

		/// <summary>
		/// Creates an Orbiter for a specific entity, fake orbit only.
		/// Does not add itself to navSet.
		/// </summary>
		/// <param name="entity">The entity to be orbited</param>
		/// <param name="distance">The distance between the orbiter and the orbited entity</param>
		/// <param name="name">What to call the orbited entity</param>
		public Orbiter(Pathfinder pathfinder, AllNavigationSettings navSet, IMyEntity entity, float distance, string name)
			: base(pathfinder)
		{
			this.m_orbitEntity_name = name;

			Altitude = distance;
			OrbitEntity = entity;

			CalcFakeOrbitSpeedForce();
			Log.DebugLog("Orbiting: " + OrbitEntity.getBestName(), Logger.severity.INFO);
		}

		private void SetOrbitClosestVoxel(bool asteroid)
		{
			double closest = double.MaxValue;
			IMyEntity closestEntity = null;
			foreach (MyVoxelBase ent in asteroid ? (IEnumerable<MyVoxelBase>)Globals.AllVoxelMaps() : (IEnumerable<MyVoxelBase>)Globals.AllPlanets())
			{
				double dist = Vector3D.DistanceSquared(m_navBlock.WorldPosition, ent.GetCentre());
				if (dist < closest)
				{
					//Log.DebugLog("closer than closest: " + ent + ", dist: " + Math.Sqrt(dist) + ", closest: " + Math.Sqrt(closest));
					closest = dist;
					closestEntity = ent;
				}
				//Log.DebugLog("further than closest: " + ent + ", dist: " + Math.Sqrt(dist) + ", closest: " + Math.Sqrt(closest));
			}
			OrbitEntity = closestEntity;
		}

		private void CalcFakeOrbitSpeedForce()
		{
			float maxSpeed = m_navSet.Settings_Task_NavMove.SpeedTarget;
			float forceForMaxSpeed = m_navBlock.Grid.Physics.Mass * maxSpeed * maxSpeed / Altitude;
			m_mover.Thrust.Update();
			float maxForce = m_mover.Thrust.GetForceInDirection(Base6Directions.GetClosestDirection(m_navBlock.LocalMatrix.Forward)) * Mover.AvailableForceRatio;

			Log.DebugLog("maxSpeed: " + maxSpeed + ", mass: " + m_navBlock.Grid.Physics.Mass + ", m_altitude: " + Altitude + ", forceForMaxSpeed: " + forceForMaxSpeed + ", maxForce: " + maxForce, Logger.severity.INFO);

			if (forceForMaxSpeed < maxForce)
			{
				OrbitSpeed = maxSpeed;
				Log.DebugLog("hold onto your hats!", Logger.severity.INFO);
			}
			else
			{
				OrbitSpeed = (float)Math.Sqrt(maxForce / m_navBlock.Grid.Physics.Mass * Altitude);
				Log.DebugLog("choosing a more reasonable speed, m_orbitSpeed: " + OrbitSpeed, Logger.severity.INFO);
			}
		}

		public override void Move()
		{
			if (m_gridFinder != null)
			{
				//Log.DebugLog("updating grid finder", "Move()");
				m_gridFinder.Update();

				// if grid finder picks a new entity, have to set OrbitEntity to null first or altitude will be incorrect
				if (m_gridFinder.Grid == null || OrbitEntity != m_gridFinder.Grid)
				{
					Log.DebugLog("no grid found");
					OrbitEntity = null;
					m_mover.StopMove();
					return;
				}
				if (OrbitEntity == null)
				{
					Log.DebugLog("found grid: " + m_gridFinder.Grid.Entity.DisplayName);
					OrbitEntity = m_gridFinder.Grid.Entity;
					CalcFakeOrbitSpeedForce();
				}
			}

			Vector3D targetCentre = OrbitEntity.GetCentre() + m_targetPositionOffset;

			m_faceDirection = Vector3.Reject(targetCentre - m_navBlock.WorldPosition, m_orbitAxis);
			float alt = m_faceDirection.Normalize();
			Vector3 orbitDirection = m_faceDirection.Cross(m_orbitAxis);
			Destination dest = new Destination(OrbitEntity, m_targetPositionOffset - m_faceDirection * Altitude);

			float speed = alt > Altitude ?
				Math.Max(1f, OrbitSpeed - alt + Altitude) : 
				OrbitSpeed;

			Vector3 addVelocity = orbitDirection * speed;
			if (!m_flyTo && !(OrbitEntity is MyPlanet))
			{
				Vector3 fakeOrbitVelocity; Vector3.Multiply(ref m_faceDirection, OrbitSpeed * OrbitSpeed / Altitude, out fakeOrbitVelocity);
				Vector3.Add(ref addVelocity, ref fakeOrbitVelocity, out addVelocity);
			}

			m_pathfinder.MoveTo(destinations: dest, addToVelocity: addVelocity);
		}

		public override void AppendCustomInfo(StringBuilder customInfo)
		{
			if (OrbitEntity == null)
			{
				customInfo.Append("Searching for: ");
				customInfo.AppendLine(m_gridFinder.m_targetGridName);
				return;
			}

			if (m_flyTo)
				customInfo.Append("Orbiter moving to: ");
			else
				customInfo.Append("Orbiting: ");
			if (OrbitEntity is MyVoxelMap)
				customInfo.AppendLine("Asteroid");
			else if (OrbitEntity is MyPlanet)
				customInfo.AppendLine("Planet");
			else if (m_orbitEntity_name != null)
				customInfo.AppendLine(m_orbitEntity_name);
			else if (m_navBlock.Block.canConsiderFriendly(OrbitEntity))
				customInfo.AppendLine(OrbitEntity.DisplayName);
			else
				customInfo.AppendLine("Enemy");

			customInfo.Append("Orbital speed: ");
			customInfo.Append(PrettySI.makePretty(OrbitSpeed));
			customInfo.AppendLine("m/s");
		}

		public void Rotate()
		{
			if (OrbitEntity == null)
			{
				m_flyTo = true;
				m_mover.StopRotate();
			}
			else if (OrbitEntity is MyPlanet)
			{
				m_flyTo = false;
				m_mover.CalcRotate(m_navBlock, RelativeDirection3F.FromWorld(m_navBlock.Grid, m_faceDirection));
			}
			else if (m_navSet.DistanceLessThan(OrbitSpeed))
			{
				if (m_flyTo)
				{
					CalcFakeOrbitSpeedForce();
					m_flyTo = false;
				}
				m_mover.CalcRotate(m_navBlock, RelativeDirection3F.FromWorld(m_navBlock.Grid, m_faceDirection));
			}
			else
			{
				m_flyTo = true;
				m_mover.CalcRotate();
			}
		}

	}
}
