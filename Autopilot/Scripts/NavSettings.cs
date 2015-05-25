#define LOG_ENABLED //remove on build

using System;
using System.Collections.Generic;

using Sandbox.ModAPI;
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
		public TARGET lockOnTarget;
		public int lockOnRangeEnemy;
		public string lockOnBlock;
		public string tempBlockName;
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

		public bool jump_to_dest
		{
			get { return false; }
			set { }
		}

		public enum TARGET : byte { OFF, MISSILE, ENEMY }
		public bool target_locked = false;

		public enum LANDING : byte { OFF, ORIENT, LINEUP, LAND, LOCKED, SEPARATE }
		public LANDING landingState = LANDING.OFF;

		private Navigator myNav;
		private IMyCubeGrid myCubeGrid
		{
			get
			{
				if (myNav == null)
					return null;
				return myNav.myGrid;
			}
		}

		public bool ignoreAsteroids = false;

		/// <summary>Special Flying Instructions</summary>
		public enum SpecialFlying : byte
		{
			/// <summary>no special instructions</summary>
			None,
			/// <summary>pathfinder can fly forwards/backwards, any move type allowed</summary>
			Line_Any,
			/// <summary>pathfinder can fly forwards, sidel only</summary>
			Line_SidelForward
		}

		public SpecialFlying SpecialFlyingInstructions = SpecialFlying.None;

		public NavSettings(Navigator owner)
		{
			this.myNav = owner;
			isAMissile = false;
			waitUntil = DateTime.UtcNow;
			waitUntilNoCheck = DateTime.UtcNow;
			lockOnTarget = TARGET.OFF;
			lockOnRangeEnemy = 0;
			lockOnBlock = null;
			tempBlockName = null;
			speedCruise_internal = Settings.GetSetting<float>(Settings.SettingName.fMaxSpeed);
			speedSlow_internal = Settings.GetSetting<float>(Settings.SettingName.fMaxSpeed);

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
			speedCruise_external = Settings.GetSetting<float>(Settings.SettingName.fDefaultSpeed);
			speedSlow_external = Settings.GetSetting<float>(Settings.SettingName.fMaxSpeed);
			destinationRadius = 100;
		}

		/// <summary>
		/// reset some variables when adding or removing a waypoint or centreDestination
		/// </summary>
		private void onWayDestAddedRemoved()
		{
			//collisionUpdateSinceWaypointAdded = 0;
			clearSpeedInternal();

			myCubeGrid.throwIfNull_variable("myCubeGrid");
		}

		public Vector3D? myWaypoint { get; private set; }
		private Vector3D? coordDestination;

		/// <summary>
		/// reset on Navigator.FullStop()
		/// </summary>
		public float speedCruise_internal { private get; set; }
		/// <summary>
		/// reset on Navigator.FullStop()
		/// </summary>
		public float speedSlow_internal { private get; set; }
		public float speedCruise_external; //{ private get; set; }
		private float value_speedSlow_external;
		public float speedSlow_external
		{
			get { return value_speedSlow_external; }
			set { value_speedSlow_external = MathHelper.Min(value, Settings.GetSetting<float>(Settings.SettingName.fMaxSpeed));}
		}
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
			//myLogger.debugLog("entered clearSpeedInternal()", "clearSpeedInternal()", Logger.severity.TRACE);
			speedCruise_internal = Settings.GetSetting<float>(Settings.SettingName.fMaxSpeed);
			speedSlow_internal = Settings.GetSetting<float>(Settings.SettingName.fMaxSpeed);
		}

		private Logger myLogger = new Logger(null, "NavSettings");
		[System.Diagnostics.Conditional("LOG_ENABLED")]
		private void log(string toLog, string method = null, Logger.severity level = Logger.severity.DEBUG)
		{ myLogger.log(level, method, toLog); }

		/// <summary>
		/// sets coordinates as centreDestination
		/// </summary>
		/// <param name="coordinates"></param>
		public void setDestination(Vector3D coordinates)
		{
			myLogger.debugLog("entered setDestination(" + coordinates + ")", "setDestination()");
			onWayDestAddedRemoved();
			coordDestination = coordinates;
		}
		/// <summary>
		/// sets a grid as a centreDestination
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
		/// sets a waypoint; a coordinate that will be flown to before the centreDestination
		/// </summary>
		/// <param name="waypoint"></param>
		/// <param name="forced"></param>
		public void setWaypoint(Vector3D waypoint)
		{
			onWayDestAddedRemoved();
			myWaypoint = waypoint;
		}

		/// <summary>
		/// tests if a waypoint is far enough away (further than destinationRadius)
		/// </summary>
		public bool waypointFarEnough(Vector3D waypoint)
		{ return myCubeGrid.WorldAABB.Distance(waypoint) > destinationRadius; }

		/// <summary>
		/// removes the waypoint if one exists, otherwise removes the centreDestination
		/// </summary>
		public void atWayDest()
		{
			atWayDest(getTypeOfWayDest());
		}

		/// <summary>
		/// removes one waypoint or centreDestination of the specified type
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
					jump_to_dest = false;
					ignoreAsteroids = false;
					SpecialFlyingInstructions = SpecialFlying.None;
					goto case TypeOfWayDest.WAYPOINT;
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
		public Vector3D? getWayDest(bool getWaypoint = true)
		{ return getWayDest(getTypeOfWayDest(getWaypoint)); }

		/// <summary>
		/// get waypoint or destination of specified type
		/// </summary>
		public Vector3D? getWayDest(TypeOfWayDest type)
		{
			switch (type)
			{
				case TypeOfWayDest.BLOCK:
					CurrentGridDest.throwIfNull_variable("CurrentGridDest");
					return CurrentGridDest.GetBlockPos();
				case TypeOfWayDest.OFFSET:
				case TypeOfWayDest.LAND:
					CurrentGridDest.throwIfNull_variable("CurrentGridDest");
					return CurrentGridDest.GetBlockPos() + offsetToWorld();
				case TypeOfWayDest.COORDINATES:
					return coordDestination;
				case TypeOfWayDest.GRID:
					CurrentGridDest.throwIfNull_variable("CurrentGridDest");
					return CurrentGridDest.GetGridPos();
				case TypeOfWayDest.WAYPOINT:
					return myWaypoint;
				case TypeOfWayDest.NULL:
					return null;
				default:
					log("unknown type " + type, "getWayDest()", Logger.severity.ERROR);
					return null;
			}
		}

		public enum TypeOfWayDest : byte { NULL, COORDINATES, GRID, BLOCK, WAYPOINT, OFFSET, LAND }
		public TypeOfWayDest getTypeOfWayDest(bool getWaypoint = true)
		{
			if (getWaypoint && myWaypoint != null)
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
						useLandOffset = landOffset * (myCubeGrid.GetLongestDim() * 1.5f + 15);
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
