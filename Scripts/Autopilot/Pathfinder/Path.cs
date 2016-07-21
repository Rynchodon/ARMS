using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Sandbox.Game.Entities;
using VRage.Game.Entity;
using VRageMath;

namespace Rynchodon.Autopilot.Pathfinder
{
	public struct Path
	{
		public Vector3D CurrentPosition, Destination;
		public Vector3 AutopilotVelocity;
		public double Distance, Radius;

		public Path(ref Vector3D CurrentPosition, ref Vector3D Destination, ref Vector3 AutopilotVelocity, float Radius)
		{
			this.CurrentPosition = CurrentPosition;
			this.Destination = Destination;
			this.AutopilotVelocity = AutopilotVelocity;
			this.Radius = Radius;

			Vector3D.Distance(ref this.CurrentPosition, ref this.Destination, out this.Distance);
		}

		// If the sum of all repulsions ends up being the opposite direction of Destination - CurrentPosition, we are stuck and need to waypoint around.
		// it would be better if we could avoid creating a valley between repulsor obstructions in the first place.
		// Atmospheric ship might want to consider space as repulsor???
		public void Repulsion(MyEntity entity, out Vector3 repulse)
		{
			Logger.DebugLog("entity is null", Logger.severity.FATAL, condition: entity == null);
			Logger.DebugLog("entity is not top-most", Logger.severity.FATAL, condition: entity.Hierarchy.Parent != null);

			Vector3D centre = entity.GetCentre();
			float boundingRadius;
			float linearSpeed;

			MyPlanet planet = entity as MyPlanet;
			if (planet != null)
			{
				boundingRadius = planet.MaximumRadius;
				linearSpeed = AutopilotVelocity.Length();
			}
			else
			{
				boundingRadius = entity.PositionComp.LocalVolume.Radius;
				if (entity.Physics == null)
					linearSpeed = AutopilotVelocity.Length();
				else
					linearSpeed = (entity.Physics.LinearVelocity - AutopilotVelocity).LengthSquared();
			}
			boundingRadius += linearSpeed * 10f; // Adjustable

			Vector3 toCurrent = CurrentPosition - centre;
			float toCurrentLenSq = toCurrent.LengthSquared();

			if (toCurrentLenSq < boundingRadius * boundingRadius)
			{
				// Autopilot is too close to entity for repulsion, need to avoid it.
				Logger.DebugLog("autopilot is too close to entity for repulsion. entity: " + entity.nameWithId() + ", toCurrentLenSq:" + toCurrentLenSq, Logger.severity.FATAL);
				throw new NotImplementedException();
			}

			float maxRepulseDist = boundingRadius + boundingRadius; // Adjustable, might need to be different for planets than other entities.
			float maxRepulseDistSq = maxRepulseDist * maxRepulseDist;

			if (toCurrentLenSq > maxRepulseDistSq)
			{
				// Autopilot is outside the maximum bounds of repulsion.
				repulse = Vector3.Zero;
				return;
			}

			// If both entity and autopilot are near the destination, the repulsion radius must be reduced or we would never reach the destination.
			double toDestLenSq;
			Vector3D.DistanceSquared(ref centre, ref Destination, out toDestLenSq);
			toDestLenSq += Distance * Distance; // Adjustable

			if (toCurrentLenSq > toDestLenSq)
			{
				// Autopilot is outside the boundaries of repulsion.
				repulse = Vector3.Zero;
				return;
			}

			if (toDestLenSq < maxRepulseDistSq)
				maxRepulseDist = (float)Math.Sqrt(toDestLenSq);

			// maxRepulseDist / toCurrentLenSq can be adjusted
			Vector3.Multiply(ref toCurrent, maxRepulseDist / toCurrentLenSq, out repulse);
		}
	}
}
