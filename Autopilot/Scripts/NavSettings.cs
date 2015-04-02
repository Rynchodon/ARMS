#define LOG_ENABLED //remove on build

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
		private GridDimensions myGridDims
		{
			get
			{
				if (myNav == null)
					return null;
				return myNav.myGridDim;
			}
		}

		public bool ignoreAsteroids = false;

		public NavSettings(Navigator owner)
		{
			this.myNav = owner;
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
			myWaypoint = null;
			coordDestination = null;
			CurrentGridDest = null;

			startOfCommands(); // for consistency

			if (myNav != null && myNav.myGrid != null)
				myLogger = new Logger(myNav.myGrid.DisplayName, "NavSettings");
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

		/// <summary>
		/// reset some variables when adding or removing a waypoint or destination
		/// </summary>
		private void onWayDestAddedRemoved()
		{
			collisionUpdateSinceWaypointAdded = 0;
			clearSpeedInternal();

			if (myGridDims == null)
			{
				myLogger.log(Logger.severity.FATAL, "onWayDestAddedRemoved()", "myGridDims == null");
				VRage.Exceptions.ThrowIf<NullReferenceException>(true);
			}
		}

		private Vector3D? myWaypoint;
		private Vector3D? coordDestination;

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
			myLogger.debugLog("entered clearSpeedInternal()", "clearSpeedInternal()", Logger.severity.TRACE);
			speedCruise_internal = Settings.floatSettings[Settings.FloatSetName.fMaxSpeed];
			speedSlow_internal = Settings.floatSettings[Settings.FloatSetName.fMaxSpeed];
		}

		private Logger myLogger = new Logger(null, "NavSettings");
		[System.Diagnostics.Conditional("LOG_ENABLED")]
		private void log(string toLog, string method = null, Logger.severity level = Logger.severity.DEBUG)
		{ myLogger.log(level, method, toLog); }

		/// <summary>
		/// sets coordinates as destination
		/// </summary>
		/// <param name="coordinates"></param>
		public void setDestination(Vector3D coordinates)
		{
			myLogger.debugLog("entered setDestination(" + coordinates + ")", "setDestination()");
			onWayDestAddedRemoved();
			coordDestination = coordinates;
		}
		/// <summary>
		/// sets a grid as a destination
		/// </summary>
		/// <param name="ls"></param>
		/// <param name="destBlock"></param>
		/// <param name="seenBy"></param>
		public void setDestination(LastSeen ls, IMyCubeBlock destBlock, IMyCubeBlock seenBy)
		{
			myLogger.debugLog("entered setDestination()", "setDestination()");
			onWayDestAddedRemoved();
			CurrentGridDest = new GridDestination(ls, destBlock, seenBy, myNav);
		}
		/// <summary>
		/// sets a waypoint; a coordinate that will be flown to before the destination
		/// </summary>
		/// <param name="waypoint"></param>
		/// <param name="forced"></param>
		/// <returns></returns>
		public bool addWaypoint(Vector3D waypoint, bool forced = false)
		{
			onWayDestAddedRemoved();
			if (forced)
			{
				myWaypoint = waypoint;
				return true;
			}
				myWaypoint = waypoint;
			return true;
		}

		/// <summary>
		/// removes the waypoint if one exists, otherwise removes the destination
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
			ignoreAsteroids = false;
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
					jump_to_dest = false;
					return;
				case TypeOfWayDest.WAYPOINT:
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
					return myWaypoint;
				default:
					log("unknown type " + getTypeOfWayDest(), "getWayDest()", Logger.severity.ERROR);
					return null;
			}
		}

		public enum TypeOfWayDest : byte { NULL, COORDINATES, GRID, BLOCK, WAYPOINT, OFFSET, LAND }
		public TypeOfWayDest getTypeOfWayDest()
		{
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

			//log("grabbed vectors: " + Base6Directions.GetVector(CurrentGridDest.Block.Orientation.Left) + ", " + Base6Directions.GetVector(CurrentGridDest.Block.Orientation.Up) + ", " + Base6Directions.GetVector(CurrentGridDest.Block.Orientation.Forward), "offsetToWorld()");
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
