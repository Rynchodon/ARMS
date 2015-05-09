#define LOG_ENABLED //remove on build

using System;
using Sandbox.ModAPI;
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
		/// <summary>created from lander class</summary>
		private bool Lander;

		public MovementMeasure(Navigator owner, Vector3D? targetDirection = null, bool Lander = false)
		{
			this.owner = owner;
			this.targetDirection = targetDirection;
			this.Lander = Lander;

			myLogger = new Logger(owner.myGrid.DisplayName, "MovementMeasure");

			lazy_rotationLengthSquared = new Lazy<double>(() => { return pitch * pitch + yaw * yaw + roll * roll; });
			lazy_currentWaypoint = new Lazy<Vector3D>(() => { return (Vector3D)owner.CNS.getWayDest(); });
			lazy_movementSpeed = new Lazy<float>(() => { return owner.myGrid.Physics.LinearVelocity.Length(); });

			lazy_navBlockPosition = new Lazy<Vector3D>(() => { return owner.getNavigationBlock().GetPosition(); });
			lazy_displacementToPoint = new Lazy<RelativeVector3F>(() => { return RelativeVector3F.createFromWorld(navBlockPos - currentWaypoint, owner.myGrid); });
			lazy_distToWayDest = new Lazy<double>(() => {
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
		private float value__pitch, value__yaw, value__roll;
		private bool isValid__pitchYaw = false;

		/// <summary>
		/// radians to pitch to reach target
		/// </summary>
		public float pitch
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
		public float yaw
		{
			get
			{
				if (!isValid__pitchYaw)
					buildPitchYaw();
				return value__yaw;
			}
		}
		/// <summary>
		/// radians to roll to reach target
		/// </summary>
		public float roll
		{
			get
			{
				if (!isValid__pitchYaw)
					buildPitchYaw();
				return value__roll;
			}
		}

		private void buildPitchYaw()
		{
			if (Lander)
			{
				buildPitchYaw_forLander();
				return;
			}

			isValid__pitchYaw = true;

			Vector3D dirNorm;
			if (targetDirection == null)
			{
				Vector3D displacement = currentWaypoint - owner.currentRCblock.GetPosition();
				dirNorm = Vector3D.Normalize(displacement);
			}
			else
				dirNorm = (Vector3D)targetDirection;

			IMyCubeBlock NavBlock = owner.getNavigationBlock();
			IMyCubeBlock RemBlock = owner.currentRCblock;

			RelativeVector3F direction = RelativeVector3F.createFromWorld(dirNorm, owner.myGrid);

			Vector3 navDirection = direction.getBlock(owner.getNavigationBlock());
			//myLogger.debugLog("navDirection = " + navDirection, "buildPitchYaw()");

			Vector3 NavRight = NavBlock.LocalMatrix.Right;
			Vector3 RemFrNR = Base6Directions.GetVector(RemBlock.LocalMatrix.GetClosestDirection(ref NavRight));

			Vector3 NavUp = NavBlock.LocalMatrix.Up;
			Vector3 RemFrNU = Base6Directions.GetVector(RemBlock.LocalMatrix.GetClosestDirection(ref NavUp));

			//myLogger.debugLog("NavRight = " + NavRight + ", NavUp = " + NavUp, "buildPitchYaw()");
			//myLogger.debugLog("RemFrNR = " + RemFrNR + ", RemFrNU = " + RemFrNU, "buildPitchYaw()");

			float right = navDirection.X, down = -navDirection.Y, forward = -navDirection.Z;
			float pitch = (float)Math.Atan2(down, forward), yaw = (float)Math.Atan2(right, forward);

			Vector3 mapped = pitch * RemFrNR + yaw * RemFrNU;

			//myLogger.debugLog("mapped " + new Vector3(pitch, yaw, 0) + " to " + mapped, "buildPitchYaw()");

			value__pitch = mapped.X;
			value__yaw = mapped.Y;
			value__roll = mapped.Z;
		}

		private void buildPitchYaw_forLander()
		{
			myLogger.debugLog("Entered buildPitchYaw_forLander()", "buildPitchYaw_forLander()");

			isValid__pitchYaw = true;

			Vector3D dirNorm;
			if (targetDirection == null)
			{
				Vector3D displacement = currentWaypoint - owner.currentRCblock.GetPosition();
				dirNorm = Vector3D.Normalize(displacement);
			}
			else
				dirNorm = (Vector3D)targetDirection;

			double right = owner.currentRCblock.WorldMatrix.Right.Dot(dirNorm);
			double down = owner.currentRCblock.WorldMatrix.Down.Dot(dirNorm);
			double forward = owner.currentRCblock.WorldMatrix.Forward.Dot(dirNorm);

			value__pitch = (float)Math.Atan2(down, forward);
			value__yaw = (float)Math.Atan2(right, forward);
			value__roll = 0;
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
		/// from nav block to way/dest or, if centreDestination is a block or a grid, distToDestGrid
		/// </summary>
		public double distToWayDest { get { return lazy_distToWayDest.Value; } }

		private Lazy<double> lazy_distToDestGrid;
		/// <summary>
		/// shortest distance between AABB
		/// </summary>
		private double distToDestGrid { get { return lazy_distToDestGrid.Value; } }

		private double shortestDistanceToDestGrid()
		{ return owner.myGrid.WorldAABB.Distance(owner.CNS.CurrentGridDest.Grid.WorldAABB); }


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