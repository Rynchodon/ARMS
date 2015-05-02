// skip file on build

using System;
using VRageMath;
using System.Collections.Generic;
//using System.Linq;
//using System.Text;

using Sandbox.ModAPI;
using Ingame = Sandbox.ModAPI.Ingame;

namespace Rynchodon.Autopilot
{
	public class GridDimensions
	{
		private Logger myLogger = null;
		[System.Diagnostics.Conditional("LOG_ENABLED")]
		private void log(string toLog, string method = null, Logger.severity level = Logger.severity.DEBUG)
		{
			myLogger.log(level, method, toLog);
		}

		public Sandbox.ModAPI.IMyCubeGrid myGrid { get { return myRC.CubeGrid; } }
		public Sandbox.ModAPI.IMyCubeBlock myRC { get; private set; }
		//public List<IMyEntity> attachedEntities = new List<IMyEntity>();

		// local metres
		public Vector3 centre_grid { get; private set; }
		public Vector3 rc_pos_grid { get; private set; }
		public float width { get; private set; }
		public float height { get; private set; }
		public float length { get; private set; }
		public float distance_to_front_from_centre { get; private set; } // for collision
		// decided to use myGrid.WorldAABB(dest) public float distance_to_front_from_RC { get; private set; } // for navigation

		// world metres
		public Vector3D centre_world { get { return myGrid.WorldAABB.Center; } }
		public Vector3D getRCworld() { return myRC.GetPosition(); }

		internal GridDimensions(Sandbox.ModAPI.IMyCubeBlock RC)
		{
			this.myRC = RC;
			myLogger = new Logger(myGrid.DisplayName, "GridDimensions");

			Vector3 start = new Vector3(), end = new Vector3();
			Vector3[] allCorners = myGrid.LocalAABB.GetCorners();
			bool firstCorner = true;
			foreach (Vector3 corner in allCorners)
			{
				// 0,0,0 may not be part of ship, so start with a corner
				if (firstCorner)
				{
					start = corner; end = corner;
					firstCorner = false;
					continue;
				}
				//log("\tcorner: " + corner.ToString());
				if (corner.X > start.X)
					start.X = corner.X;
				else if (corner.X < end.X)
					end.X = corner.X;

				if (corner.Y > start.Y)
					start.Y = corner.Y;
				else if (corner.Y < end.Y)
					end.Y = corner.Y;

				if (corner.Z > start.Z)
					start.Z = corner.Z;
				else if (corner.Z < end.Z)
					end.Z = corner.Z;
			}
			log("ship bounds are " + end.X + ":" + start.X + ", " + end.Y + ":" + start.Y + ", " + end.Z + ":" + start.Z);
			centre_grid = myGrid.LocalAABB.Center;
			rc_pos_grid = myRC.Position * myGrid.GridSize;
			log("middle is " + centre_grid + ", RC is " + rc_pos_grid, "..ctor", Logger.severity.DEBUG);

			width = getDimension(myRC.Orientation.Left, start, end);
			height = getDimension(myRC.Orientation.Up, start, end);
			length = getDimension(myRC.Orientation.Forward, start, end);

			distance_to_front_from_centre = calcDistToEdge(myRC.Orientation.Forward, centre_grid, start, end);
			//distance_to_front_from_RC = calcDistToEdge(myRC.Orientation.Forward, rc_pos_grid, start, end);
		}

		///// <summary>
		///// subtracts the distance to front of grid
		///// </summary>
		///// <param name="target"></param>
		///// <returns></returns>
		//public double getRCdistanceTo(Vector3D target)
		//{
		//	return (target - getRCworld()).Length() - distance_to_front_from_RC;
		//}

		///// <summary>
		///// subtracts the distance to front of grid
		///// </summary>
		///// <param name="target"></param>
		///// <returns></returns>
		//public double getRCdistanceTo(Sandbox.ModAPI.IMyCubeBlock target)
		//{
		//	return getRCdistanceTo(target.GetPosition());
		//}

		///// <summary>
		///// subtracts the distance to front of grid
		///// </summary>
		///// <param name="target"></param>
		///// <returns></returns>
		//public double getRCdistanceTo(BoundingBoxD target)
		//{
		//	return target.Distance(getRCworld()) - distance_to_front_from_RC;
		//}

		///// <summary>
		///// subtracts the distance to front of grid
		///// </summary>
		///// <param name="target"></param>
		///// <returns></returns>
		//public double getRCdistanceTo(Sandbox.ModAPI.IMyCubeGrid target)
		//{
		//	return getRCdistanceTo(target.WorldAABB);
		//}

		private float getDimension(Base6Directions.Direction direction, Vector3 start, Vector3 end)
		{
			Vector3 unit = Base6Directions.GetVector(direction);
			float startC = start.Dot(unit);
			float endC = end.Dot(unit);

			return Math.Abs(startC - endC);
		}

		/// <summary>
		/// how far is the edge of the ship in the given direction
		/// </summary>
		/// <param name="direction"></param>
		/// <returns></returns>
		private float getLimit(Base6Directions.Direction direction, Vector3 start, Vector3 end)
		{
			Vector3 unit = Base6Directions.GetVector(direction);
			float minC = start.Dot(unit);
			float maxC = end.Dot(unit);

			if (minC > maxC)
				return minC;
			else
				return maxC;
		}

		private float calcDistToEdge(Base6Directions.Direction direction, Vector3 from, Vector3 start, Vector3 end)
		{
			float coordinate = centre_grid.Dot(Base6Directions.GetVector(direction));
			float limit = getLimit(direction, start, end);

			float distanceToEdge = limit - coordinate;
			if (distanceToEdge < 0)
			{
				myLogger.log(Logger.severity.ERROR, "calcDistToEdge(" + direction + ")", "Error distanceToEdge=" + distanceToEdge + "(" + limit + " - " + coordinate + ") < 0");
				log("middleMetres=" + centre_grid + ", dir=" + Base6Directions.GetVector(direction) + ", coordinate=" + coordinate, "calcDistToEdge(" + direction + ")");
			}

			log("distance to edge " + direction + " is " + distanceToEdge, "calcDistToEdge(" + direction + ")");
			return distanceToEdge;
		}

		public float getLongestDim() { return Math.Max(Math.Max(width, height), length); }
	}
}