#define LOG_ENABLED //remove on build

using System;
//using System.Collections.Generic;
//using System.Linq;
//using System.Text;

using VRageMath;

namespace Rynchodon.Autopilot
{
	/// <summary>
	/// caches and lazy initializes
	/// </summary>
	internal class MovementMeasure
	{
		private Navigator owner;
		private Vector3D? targetDirection;
		public MovementMeasure(Navigator owner, Vector3D? targetDirection = null)
		{
			this.owner = owner;
			this.targetDirection = targetDirection;

			lazy_rotationLengthSquared = new Lazy<double>(() => { return pitch * pitch + yaw * yaw; });
			lazy_currentWaypoint = new Lazy<Vector3D>(() => { return (Vector3D)owner.CNS.getWayDest(); });
			lazy_movementSpeed = new Lazy<float>(() => { return owner.myGrid.Physics.LinearVelocity.Length(); });

			lazy_navBlockPosition = new Lazy<Vector3D>(() => { return owner.getNavigationBlock().GetPosition(); });
			lazy_displacementToPoint = new Lazy<RelativeVector3F>(() => { return RelativeVector3F.createFromWorld(navBlockPos - currentWaypoint, owner.myGrid); });
			lazy_distToWayDest = new Lazy<double>(() =>
			{
				switch (owner.CNS.getTypeOfWayDest())
				{
					case NavSettings.TypeOfWayDest.BLOCK:
					case NavSettings.TypeOfWayDest.GRID:
						return distToDestGrid;
					default:
						return displacement.getWorld().Length();
				}
			});

			lazy_distToDestGrid = new Lazy<double>(shortestDistanceToDestGrid);
		}

		// these are all built together so we will not be using lazy
		private double value__pitch, value__yaw, value__pitchPower, value__yawPower;
		private bool isValid__pitchYaw = false;

		/// <summary>
		/// radians to pitch to reach target
		/// </summary>
		public double pitch
		{
			get
			{
				if (!isValid__pitchYaw)
					buildPitchYaw();
				return value__pitch;
			}
		}
		/// <summary>
		/// radians to yaw to reach target
		/// </summary>
		public double yaw
		{
			get
			{
				if (!isValid__pitchYaw)
					buildPitchYaw();
				return value__yaw;
			}
		}
		/// <summary>
		/// power to apply in pitch
		/// </summary>
		public double pitchPower
		{
			get
			{
				if (!isValid__pitchYaw)
					buildPitchYaw();
				return value__pitchPower;
			}
		}
		/// <summary>
		/// power to apply in yaw
		/// </summary>
		public double yawPower
		{
			get
			{
				if (!isValid__pitchYaw)
					buildPitchYaw();
				return value__yawPower;
			}
		}

		private void buildPitchYaw()
		{
			isValid__pitchYaw = true;

			Vector3D dirNorm;
			if (targetDirection == null)
			{
				Vector3D displacement = currentWaypoint - owner.myGridDim.getRCworld();
				dirNorm = Vector3D.Normalize(displacement);
			}
			else
				dirNorm = (Vector3D)targetDirection;
			//log("facing = " + owner.currentRCblock.WorldMatrix.Forward, "buildPitchYaw()", Logger.severity.TRACE);
			//log("dirNorm = " + dirNorm, "buildPitchYaw()", Logger.severity.TRACE);
			double right = owner.currentRCblock.WorldMatrix.Right.Dot(dirNorm);
			double down = owner.currentRCblock.WorldMatrix.Down.Dot(dirNorm);
			double forward = owner.currentRCblock.WorldMatrix.Forward.Dot(dirNorm);
			//log("dir vects = " + right + ", " + down + ", " + forward, "buildPitchYaw()", Logger.severity.TRACE);
			value__pitch = Math.Atan2(down, forward);
			value__yaw = Math.Atan2(right, forward);
			//log("pitch = " + value__pitch + ", yaw = " + value__yaw, "buildPitchYaw()", Logger.severity.TRACE);
			switch (owner.CNS.moveState)
			{
				case NavSettings.Moving.MOVING:
				case NavSettings.Moving.HYBRID:
					value__pitchPower = value__pitch * owner.inflightRotatingPower;
					value__yawPower = value__yaw * owner.inflightRotatingPower;
					//log("power multiplier = " + owner.inflightRotatingPower, "buildPitchYaw()", Logger.severity.TRACE);
					break;
				default:
					value__pitchPower = value__pitch * owner.rotationPower;
					value__yawPower = value__yaw * owner.rotationPower;
					//log("power multiplier = " + owner.rotationPower, "buildPitchYaw()", Logger.severity.TRACE);
					break;
			}
			//log("pitch power = " + value__pitchPower + ", yaw power = " + value__yawPower, "buildPitchYaw()", Logger.severity.TRACE);
		}

		private Lazy<double> lazy_rotationLengthSquared;
		/// <summary>
		/// rotation length squared
		/// </summary>
		public double rotLenSq { get { return lazy_rotationLengthSquared.Value; } }

		private Lazy<Vector3D> lazy_currentWaypoint;
		public Vector3D currentWaypoint { get { return lazy_currentWaypoint.Value; } }

		private Lazy<float> lazy_movementSpeed;
		public float movementSpeed { get { return lazy_movementSpeed.Value; } }


		private Lazy<Vector3D> lazy_navBlockPosition;
		public Vector3D navBlockPos { get { return lazy_navBlockPosition.Value; } }

		private Lazy<RelativeVector3F> lazy_displacementToPoint;
		/// <summary>
		/// from nav block to way/dest point
		/// </summary>
		public RelativeVector3F displacement { get { return lazy_displacementToPoint.Value; } }

		private Lazy<double> lazy_distToWayDest;
		/// <summary>
		/// from nav block to way/dest or, if destination is a block or a grid, distToDestGrid
		/// </summary>
		public double distToWayDest { get { return lazy_distToWayDest.Value; } }

		private Lazy<double> lazy_distToDestGrid;
		/// <summary>
		/// shortest distance between any corner and opposite BoundingBoxD
		/// </summary>
		private double distToDestGrid { get { return lazy_distToDestGrid.Value; } }

		private double shortestDistanceToDestGrid()
		{
			BoundingBoxD grid1 = owner.myGrid.WorldAABB, grid2 = owner.CNS.CurrentGridDest.Grid.WorldAABB;
			double shortestDistance = double.MaxValue;
			foreach (Vector3D corner in grid1.GetCorners())
				shortestDistance = Math.Min(shortestDistance, grid2.Distance(corner));
			foreach (Vector3D corner in grid2.GetCorners())
				shortestDistance = Math.Min(shortestDistance, grid1.Distance(corner));
			return shortestDistance;
		}


		private Logger myLogger;
		[System.Diagnostics.Conditional("LOG_ENABLED")]
		private void log(string toLog, string method = null, Logger.severity level = Logger.severity.DEBUG)
		{ alwaysLog(toLog, method, level); }
		private void alwaysLog(string toLog, string method = null, Logger.severity level = Logger.severity.DEBUG)
		{
			if (myLogger == null) myLogger = new Logger(owner.myGrid.DisplayName, "MovementMeasure");
			myLogger.log(level, method, toLog);
		}
	}
}
