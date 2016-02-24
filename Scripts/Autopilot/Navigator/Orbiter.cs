using System;
using System.Collections.Generic;
using System.Text;
using Rynchodon.Autopilot.Data;
using Rynchodon.Autopilot.Movement;
using Sandbox.Game.Entities;
using Sandbox.ModAPI;
using VRage.ModAPI;
using VRageMath;

namespace Rynchodon.Autopilot.Navigator
{
	/// <summary>
	/// "Orbits" an entity. For a planet, this can be a real orbit.
	/// </summary>
	public class Orbiter : NavigatorMover, INavigatorRotator
  {

		private readonly Logger m_logger;
		private readonly PseudoBlock m_navBlock;
		private readonly GridFinder m_gridFinder;
		private IMyEntity value_orbitEntity;
		private float m_altitude;
		private float m_orbitSpeed;
		private Vector3 m_orbitAxis;

		private float m_fakeOrbitForce = float.NaN;

		private Vector3 m_faceDirection;
		private Vector3 m_orbitDirection;

		private IMyEntity OrbitEntity
		{
			get { return value_orbitEntity; }
			set
			{
				value_orbitEntity = value;
				if (value == null)
					return;
				m_altitude = (float)Vector3D.Distance(m_navBlock.WorldPosition, value_orbitEntity.GetCentre());
				Vector3D toTarget = value_orbitEntity.GetCentre() - m_navBlock.WorldPosition;
				m_orbitAxis = Vector3D.CalculatePerpendicularVector(toTarget);
				m_orbitAxis.Normalize();
			}
		}

		public Orbiter(Mover mover, AllNavigationSettings navSet, string entity)
			: base(mover, navSet)
		{
			this.m_logger = new Logger(GetType().Name, m_controlBlock.CubeBlock);
			this.m_navBlock = m_navSet.Settings_Current.NavigationBlock;

			switch (entity.LowerRemoveWhitespace())
			{
				case "asteroid":
					SetOrbitClosestVoxel(true);
					CalcFakeOrbitSpeedForce();
					break;
				case "planet":
					SetOrbitClosestVoxel(false);
					m_orbitSpeed = (float)Math.Sqrt((OrbitEntity as MyPlanet).GetGravityMultiplier(m_navBlock.WorldPosition) * 9.81f * m_altitude);
					if (m_orbitSpeed < 1f)
						CalcFakeOrbitSpeedForce();
					break;
				default:
					m_gridFinder = new GridFinder(navSet, mover.Block, entity, mustBeRecent: true);
					break;
			}

			m_navSet.Settings_Task_NavMove.NavigatorMover = this;
		}

		private void SetOrbitClosestVoxel(bool asteroid)
		{
			List<IMyVoxelBase> voxels = ResourcePool<List<IMyVoxelBase>>.Get();
			if (asteroid)
				MyAPIGateway.Session.VoxelMaps.GetInstances_Safe(voxels, voxel => voxel is IMyVoxelMap);
			else
				MyAPIGateway.Session.VoxelMaps.GetInstances_Safe(voxels, voxel => voxel is MyPlanet);

			double closest = double.MaxValue;
			foreach (IMyEntity ent in voxels)
			{
				double dist = Vector3D.DistanceSquared(m_navBlock.WorldPosition, ent.GetCentre());
				if (dist < closest)
				{
					closest = dist;
					OrbitEntity = ent;
				}
			}

			voxels.Clear();
			ResourcePool<List<IMyVoxelBase>>.Return(voxels);
		}

		private void CalcFakeOrbitSpeedForce()
		{
			float maxSpeed = m_navSet.Settings_Task_NavMove.SpeedTarget;
			float forceForMaxSpeed = m_navBlock.Grid.Physics.Mass * maxSpeed * maxSpeed / m_altitude;
			float maxForce = m_mover.myThrust.GetForceInDirection(Base6Directions.GetClosestDirection(m_navBlock.LocalMatrix.Forward)) * Mover.AvailableForceRatio;

			if (forceForMaxSpeed < maxForce)
			{
				m_logger.debugLog("hold onto your hats!", "CalcFakeOrbitSpeed()", Logger.severity.INFO);
				m_fakeOrbitForce = forceForMaxSpeed;
				m_orbitSpeed = maxSpeed;
			}
			else
			{
				m_logger.debugLog("choosing a more reasonable speed", "CalcFakeOrbitSpeed()", Logger.severity.INFO);
				m_fakeOrbitForce = maxForce;
				m_orbitSpeed = (float)Math.Sqrt(m_fakeOrbitForce / m_navBlock.Grid.Physics.Mass * m_altitude);
			}
		}

		public override void Move()
		{
			if (m_gridFinder != null)
			{
				m_logger.debugLog("updating grid finder", "Move()");
				m_gridFinder.Update();

				if (m_gridFinder.Grid == null)
				{
					m_logger.debugLog("no grid found", "Move()");
					OrbitEntity = null;
					m_mover.StopMove();
					return;
				}
				if (OrbitEntity == null)
				{
					m_logger.debugLog("found grid: " + m_gridFinder.Grid.Entity.DisplayName, "Move()");
					OrbitEntity = m_gridFinder.Grid.Entity;
					CalcFakeOrbitSpeedForce();
				}
			}

			Vector3 targetCentre = OrbitEntity.GetCentre();

			m_faceDirection = Vector3.Reject(targetCentre - m_navBlock.WorldPosition, m_orbitAxis);
			float alt = m_faceDirection.Normalize();
			m_orbitDirection = m_faceDirection.Cross(m_orbitAxis);
			Vector3 destination = targetCentre - m_faceDirection * m_altitude;
			float speed = alt > m_altitude ?
				Math.Max(1f, m_orbitSpeed - alt + m_altitude) : 
				m_orbitSpeed;

			m_logger.debugLog("my pos: " + m_navBlock.WorldPosition + ", targetCentre: " + targetCentre + ", alt: " + alt + ", orbit altitude: " + m_altitude + ", orbit speed: " + m_orbitSpeed + ", speed: " + speed, "Move()");
			m_logger.debugLog("m_faceDirection: " + m_faceDirection + ", m_orbitAxis: " + m_orbitAxis + ", m_orbitDirection: " + m_orbitDirection, "Move()");
			m_logger.debugLog("destination: " + destination + ", dest speed: " + (OrbitEntity.GetLinearVelocity() + m_orbitDirection * speed), "Move()");

			m_mover.CalcMove(m_navBlock, destination, OrbitEntity.GetLinearVelocity() + m_orbitDirection * speed);
		}

		public override void AppendCustomInfo(StringBuilder customInfo)
		{
			if (OrbitEntity == null)
			{
				customInfo.Append("Searching for: ");
				customInfo.AppendLine(m_gridFinder.m_targetGridName);
				return;
			}

			customInfo.Append("Orbiting: ");
			if (OrbitEntity is MyVoxelMap)
				customInfo.AppendLine("Asteroid");
			else if (OrbitEntity is MyPlanet)
				customInfo.AppendLine("Planet");
			else
				customInfo.AppendLine(OrbitEntity.DisplayName);

			customInfo.Append("Orbital speed: ");
			customInfo.Append(PrettySI.makePretty(m_orbitSpeed));
			customInfo.AppendLine("m/s");
		}

		public void Rotate()
		{
			if (OrbitEntity == null)
				m_mover.StopRotate();
			else
				m_mover.CalcRotate(m_navBlock, RelativeDirection3F.FromWorld(m_navBlock.Grid, m_faceDirection));
		}

	}
}
