// line removed by build.py 
using System;
using System.Collections.Generic;
//using System.Linq;
//using System.Text;

//using Sandbox.Common;
//using Sandbox.Common.Components;
//using Sandbox.Common.ObjectBuilders;
//using Sandbox.Definitions;
//using Sandbox.Engine;
//using Sandbox.Game;
using Sandbox.ModAPI;
//using Sandbox.ModAPI.Ingame;
//using Sandbox.ModAPI.Interfaces;
using VRageMath;

using Rynchodon.AntennaRelay;

namespace Rynchodon.Autopilot
{
	internal class NavSettings
	{
		public enum Moving : byte { NOT_MOVE, MOVING, STOP_MOVE, SIDELING, HYBRID }
		public enum Rotating : byte { NOT_ROTA, ROTATING, STOP_ROTA }
		public enum Rolling : byte { NOT_ROLL, ROLLING, STOP_ROLL }

		public Moving moveState = Moving.NOT_MOVE;
		public Rotating rotateState = Rotating.NOT_ROTA;
		public Rolling rollState = Rolling.NOT_ROLL;

		public int destinationRadius;
		public bool isAMissile;
		public DateTime waitUntil;
		public DateTime waitUntilNoCheck;
		public string searchBlockName; // might want to remove this one
		public Queue<string> instructions;
		public TARGET lockOnTarget;
		public int lockOnRangeEnemy;
		public string lockOnBlock;
		public string tempBlockName;
		public bool noWayForward;
		public bool EXIT = false;
		public Vector3 destination_offset = Vector3.Zero;
		public Base6Directions.Direction? match_direction = null; // reset on reached dest
		public Base6Directions.Direction? match_roll = null;
		public Sandbox.ModAPI.IMyCubeBlock landLocalBlock = null; // for landing or docking (primary)
		private Base6Directions.Direction? landDirection_value;
		public Base6Directions.Direction? landDirection
		{
			get { return landDirection_value; }
			set
			{
				landDirection_value = value;
				if (value == null)
					landOffset_value = Vector3.Zero;
				else
					landOffset_value = Base6Directions.GetVector((Base6Directions.Direction)value);
			}
		}
		private Vector3 landOffset_value;
		public Vector3 landOffset { get { return landOffset_value; } }
		public Vector3D? landingSeparateWaypoint;
		public Sandbox.ModAPI.IMyCubeBlock landingSeparateBlock;
		//public bool forcedStopAtDest = true;

		private bool value__jump_to_dest = false;
		public bool jump_to_dest
		{
			get { return value__jump_to_dest; }
			set
			{
				bool allowed, fetched;
				if (MyAPIGateway.Session.CreativeMode)
					fetched = Settings.boolSettings.TryGetValue(Settings.BoolSetName.bAllowJumpCreative, out allowed);
				else
					fetched = Settings.boolSettings.TryGetValue(Settings.BoolSetName.bAllowJumpSurvival, out allowed);
				if (!fetched)
				{
					myLogger.log(Logger.severity.ERROR, "set_jump_to_dest()", "failed to get setting");
					allowed = false;
				}
				value__jump_to_dest = value && allowed;
			}
		}

		public enum TARGET : byte { OFF, MISSILE, ENEMY }
		public bool target_locked = false;

		public enum LANDING : byte { OFF, ORIENT, LINEUP, LAND, LOCKED, SEPARATE }
		public LANDING landingState = LANDING.OFF;

		private Navigator myNav;
		private GridDimensions myGridDims { get { return myNav.myGridDim;}}

		public NavSettings(Navigator owner)
		{
			this.myNav = owner;
			//this.myGridDims = owner.myGridDim;
			//if (logger != null)
			//logger.WriteLine("initialized navigation settings");
			instructions = new Queue<string>();
			isAMissile = false;
			waitUntil = DateTime.UtcNow;
			waitUntilNoCheck = DateTime.UtcNow;
			searchBlockName = null;
			lockOnTarget = TARGET.OFF;
			lockOnRangeEnemy = 0;
			lockOnBlock = null;
			tempBlockName = null;
			noWayForward = false;
			speedCruise_internal = Settings.floatSettings[Settings.FloatSetName.fMaxSpeed];
			speedSlow_internal = Settings.floatSettings[Settings.FloatSetName.fMaxSpeed];

			// private vars
			//waypoints = new Stack<Vector3D>();
			myWaypoint = null;
			coordDestination = null;
			CurrentGridDest = null;

			startOfCommands(); // for consistency
		}

		/// <summary>
		/// variables that need to be reset at end of commands
		/// </summary>
		public void startOfCommands()
		{
			//if (myLogger != null)
			//	log("reached end of commands", "startOfCommands()", Logger.severity.TRACE);
			float setting;
			if (Settings.floatSettings.TryGetValue(Settings.FloatSetName.fDefaultSpeed, out setting))
				speedCruise_external = setting;
			else
				speedCruise_external = 100;
			speedSlow_external = Settings.floatSettings[Settings.FloatSetName.fMaxSpeed];
			destinationRadius = 100;
		}

		private void onWayDestAddedRemoved()
		{
			collisionUpdateSinceWaypointAdded = 0;
			if (myGridDims == null)
				myLogger.log(Logger.severity.FATAL, "onWayDestAddedRemoved()", "myGridDims == null");

			clearSpeedInternal();
		}

		//private Stack<Vector3D> waypoints;
		private Vector3D? myWaypoint;
		private Vector3D? coordDestination;

		//public LastSeen LastSeenDest { get; private set; }

		//public IMyCubeGrid gridDestination
		//{
		//	get
		//	{
		//		IMyCubeGrid value = LastSeenDest.Entity as IMyCubeGrid;
		//		if (value == null)
		//			myLogger.log("LastSeenDest.Entity is not a grid", "gridDestination", Logger.severity.FATAL);
		//		return value;
		//	}
		//}
		//public IMyCubeBlock closestBlock { get; private set; }

		/// <summary>
		/// to keep ship from moving until at least one collision check has happend.
		/// updated for new destination, not for new waypoint. updated for atWayDest
		/// </summary>
		public int collisionUpdateSinceWaypointAdded = 0;

		/// <summary>
		/// reset on Navigator.FullStop()
		/// </summary>
		public float speedCruise_internal { private get; set; }
		/// <summary>
		/// reset on Navigator.FullStop()
		/// </summary>
		public float speedSlow_internal { private get; set; }
		public float speedCruise_external { private get; set; }
		public float speedSlow_external { private get; set; }
		//public const float speedSlow_minimum = 0.25f;

		public float getSpeedCruise()
		{
			float minSetting = Math.Max(Math.Min(speedCruise_internal, speedCruise_external), 0.2f);
			float maxSetting = getSpeedSlow();
			//log("min of (" + speedCruise_internal + ", " + speedCruise_external + ", " + maxSetting + ")", "getCruiseSpeed", Logger.severity.TRACE);
			if (minSetting < maxSetting)
				return minSetting;
			else
				return maxSetting - 1;
		}
		public float getSpeedSlow()
		{
			//log("min of (" + speedSlow_internal + ", " + speedSlow_external +")", "getSpeedSlow", Logger.severity.TRACE);
			return Math.Max(Math.Min(speedSlow_internal, speedSlow_external), 0.3f);
		}
		public void clearSpeedInternal()
		{
			speedCruise_internal = Settings.floatSettings[Settings.FloatSetName.fMaxSpeed];
			speedSlow_internal = Settings.floatSettings[Settings.FloatSetName.fMaxSpeed];
		}

		private Logger myLogger = null;// = new Logger(myGrid.DisplayName, "Collision");
		[System.Diagnostics.Conditional("LOG_ENABLED")]
		private void log(string toLog, string method = null, Logger.severity level = Logger.severity.DEBUG)
		{
			if (myLogger == null)
				myLogger = new Logger(myGridDims.myGrid.DisplayName, "NavSettings");
			myLogger.log(level, method, toLog);
		}

		//public bool destinationIsFarEnough(double distance)
		//{
		//	if (isAMissile || destinationRadius == 0 || landLocalBlock != null || landingState != LANDING.OFF)
		//		return true;
		//	distance -= (double)myGridDims.getLongestDim();
		//	return (distance > 2 * destinationRadius);
		//}

		//public bool waypointIsFarEnough(Vector3D waypoint)
		//{
		//	double distanceSquared = (myGridDims.myRC.GetPosition() - waypoint).LengthSquared();
		//	double longestDim = myGridDims.getLongestDim();
		//	return distanceSquared - longestDim * longestDim > destinationRadius * destinationRadius;
		//}

		//private bool destinationIsFarEnough(BoundingBoxD destination)
		//{
		//	return destinationIsFarEnough(myGridDims.getRCdistanceTo(destination));
		//}

		public void setDestination(Vector3D coordinates)
		{
			onWayDestAddedRemoved();
			coordDestination = coordinates;
		}
		//public void setDestination(Sandbox.ModAPI.IMyCubeGrid grid)
		//{
		//	onWayDestAddedRemoved();
		//	gridDestination = grid;
		//}
		//public void setDestination(Sandbox.ModAPI.IMyCubeBlock block)
		//{
		//	onWayDestAddedRemoved();
		//	gridDestination = block.CubeGrid;
		//	closestBlock = block;
		//}
		///// <summary>
		///// if block is not null use it, otherwise use grid
		///// </summary>
		//public void setDestination(Sandbox.ModAPI.IMyCubeBlock block, Sandbox.ModAPI.IMyCubeGrid grid)
		//{
		//	if (block == null)
		//		setDestination(grid);
		//	else
		//		setDestination(block);
		//}
		public void setDestination(LastSeen ls, IMyCubeBlock destBlock, IMyCubeBlock seenBy)
		{
			onWayDestAddedRemoved();
			CurrentGridDest = new GridDestination(ls, destBlock, seenBy, myNav);
		}
		public bool addWaypoint(Vector3D waypoint, bool forced = false)
		{
			onWayDestAddedRemoved();
			if (forced)
			{
				//waypoints.Push(waypoint);
				myWaypoint = waypoint;
				return true;
			}
			//bool farEnough = waypointIsFarEnough(waypoint);
			//if (farEnough)
				//waypoints.Push(waypoint);
				myWaypoint = waypoint;
			return true;
		}

		/// <summary>
		/// removes one waypoint or destination
		/// </summary>
		public void atWayDest()
		{
			atWayDest(getTypeOfWayDest());
		}

		/// <summary>
		/// removes one waypoint or destination of the specified type
		/// </summary>
		/// <param name="typeToRemove"></param>
		public void atWayDest(TypeOfWayDest typeToRemove)
		{
			onWayDestAddedRemoved();
			switch (typeToRemove)
			{
				case TypeOfWayDest.BLOCK:
				case TypeOfWayDest.COORDINATES:
				case TypeOfWayDest.GRID:
				case TypeOfWayDest.OFFSET:
				case TypeOfWayDest.LAND:
					log("clearing destination variables", "atWayDest()", Logger.severity.TRACE);
					CurrentGridDest = null;
					coordDestination = null;
					destination_offset = Vector3.Zero;
					match_direction = null;
					match_roll = null;
					landLocalBlock = null;
					landDirection = null;
					//landOffset = Vector3.Zero;
					jump_to_dest = false;
					return;
				case TypeOfWayDest.WAYPOINT:
					//waypoints.Pop();
					myWaypoint = null;
					return;
				default:
					log("unknown type " + typeToRemove, "atWayDest()", Logger.severity.ERROR);
					return;
			}
		}

		/// <summary>
		/// get next waypoint or destination
		/// </summary>
		/// <returns></returns>
		public Vector3D? getWayDest()
		{
			switch (getTypeOfWayDest())
			{
				case TypeOfWayDest.BLOCK:
					return CurrentGridDest.GetBlockPos();
				case TypeOfWayDest.OFFSET:
				case TypeOfWayDest.LAND:
					return CurrentGridDest.GetBlockPos() + offsetToWorld();
				case TypeOfWayDest.COORDINATES:
					return coordDestination;
				case TypeOfWayDest.GRID:
					return CurrentGridDest.GetGridPos();
				case TypeOfWayDest.WAYPOINT:
					//return waypoints.Peek();
					return myWaypoint;
				default:
					log("unknown type " + getTypeOfWayDest(), "getWayDest()", Logger.severity.ERROR);
					return null;
			}
		}

		public enum TypeOfWayDest : byte { NULL, COORDINATES, GRID, BLOCK, WAYPOINT, OFFSET, LAND }
		public TypeOfWayDest getTypeOfWayDest()
		{
			//if (waypoints.Count > 0)
			if (myWaypoint != null)
				return TypeOfWayDest.WAYPOINT;

			if (CurrentGridDest != null)
			{
				if (CurrentGridDest.Block != null)
				{
					if (landLocalBlock != null)
						return TypeOfWayDest.LAND;
					if (destination_offset != Vector3.Zero)
						return TypeOfWayDest.OFFSET;
					else
						return TypeOfWayDest.BLOCK;
				}
				else
					return TypeOfWayDest.GRID;
			}

			if (coordDestination != null)
				return TypeOfWayDest.COORDINATES;

			return TypeOfWayDest.NULL;
		}

		private Vector3D offsetToWorld()
		{
			if (CurrentGridDest.Block == null || (destination_offset == Vector3.Zero && landOffset == Vector3.Zero))
				return Vector3D.Zero;

			Vector3 useLandOffset = Vector3.Zero;
			if (landOffset != Vector3.Zero)
			{
				switch (landingState)
				{
					case LANDING.OFF:
						useLandOffset = landOffset * (myGridDims.getLongestDim() * 1.5f + 15);
						break;
					case LANDING.LINEUP:
					case LANDING.SEPARATE:
						useLandOffset = landOffset * 10f;
						//log("landingState=" + landingState + ", useLandOffset=" + useLandOffset + ", landOffset=" + landOffset, "offsetToWorld()", Logger.severity.TRACE);
						break;
					case LANDING.LAND:
						useLandOffset = Vector3.Zero;
						//log("landingState=" + landingState + ", useLandOffset=" + useLandOffset + ", landOffset=" + landOffset, "offsetToWorld()", Logger.severity.TRACE);
						break;
				}
			}

			Vector3D world = (destination_offset.X + useLandOffset.X) * CurrentGridDest.Block.WorldMatrix.Right;
			world += (destination_offset.Y + useLandOffset.Y) * CurrentGridDest.Block.WorldMatrix.Up;
			world += (destination_offset.Z + useLandOffset.Z) * CurrentGridDest.Block.WorldMatrix.Backward;

			//log("grabbed vectors: "+ Base6Directions.GetVector(closestBlock.Orientation.Left)+", "+Base6Directions.GetVector(closestBlock.Orientation.Up)+", "+Base6Directions.GetVector(closestBlock.Orientation.Forward));
			//log("destination_offset=" + destination_offset + ", landingState=" + landingState + ", landOffset is " + landOffset + ", useLandOffset is " + useLandOffset + ", world vector is " + world, "offsetToWorld()", Logger.severity.TRACE);
			return world;
		}

		public void getOrientationOfDest(out Vector3D? direction, out Vector3D? roll)
		{
			switch (getTypeOfWayDest())
			{
				case TypeOfWayDest.BLOCK:
				case TypeOfWayDest.OFFSET:
				case TypeOfWayDest.LAND:
					log("match_direction=" + match_direction + ", match_roll=" + match_roll);
					if (match_direction == null) { direction = null; roll = null; return; }

					// do direction
					direction = CurrentGridDest.Block.WorldMatrix.GetDirectionVector((Base6Directions.Direction)match_direction);

					if (match_roll == null) { roll = null; return; }

					// do roll
					roll = CurrentGridDest.Block.WorldMatrix.GetDirectionVector((Base6Directions.Direction)match_roll);

					return;
				default:
					{
						log("orientation not supported for " + getTypeOfWayDest(), "getOrientationOfDest()", Logger.severity.DEBUG);
						direction = null; roll = null; return;
					}
			}
		}

		public string GridDestName
		{ get { return CurrentGridDest.Grid.DisplayName; } }

		public string BlockDestName
		{ get { return CurrentGridDest.Block.DisplayNameText; } }

		public GridDestination CurrentGridDest { get; private set; }
	}
}
