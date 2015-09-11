#define LOG_ENABLED //remove on build

using System;
using Rynchodon.AntennaRelay;
using Sandbox.ModAPI;
using VRageMath;
using Rynchodon.Settings;

namespace Rynchodon.Autopilot.NavigationSettings
{
	internal class NavSettings
	{
		#region Enums

		public enum Moving : byte { NOT_MOVE, MOVING, STOP_MOVE, SIDELING, HYBRID }
		public enum Rotating : byte { NOT_ROTA, ROTATING, STOP_ROTA }
		public enum Rolling : byte { NOT_ROLL, ROLLING, STOP_ROLL }
		public enum TARGET : byte { OFF, MISSILE, ENEMY }
		public enum LANDING : byte { OFF, ORIENT, LINEUP, LAND, LOCKED, SEPARATE }
		/// <summary>Special Flying Instructions</summary>
		public enum SpecialFlying : byte
		{
			/// <summary>no special instructions</summary>
			None = 0,
			/// <summary>pathfinder can fly forwards/backwards, any move type allowed</summary>
			Line_Any = 1 << 0,
			/// <summary>pathfinder can fly forwards, sidel only</summary>
			Line_SidelForward = 1 << 1,
			/// <summary>
			/// <para>Only allow hybrid move state. Flying to different point than rotating to.</para>
			/// <para>This state should never be set directly.</para>
			/// </summary>
			HybridOnly = 1 << 2
		}
		public enum TypeOfWayDest : byte { NONE, COORDINATES, GRID, BLOCK, WAYPOINT, OFFSET, LAND }

		#region States

		public Moving moveState = Moving.NOT_MOVE;
		public Rotating rotateState = Rotating.NOT_ROTA;
		public Rolling rollState = Rolling.NOT_ROLL;
		public LANDING landingState = LANDING.OFF;
		public TARGET lockOnTarget = TARGET.OFF;
		public SpecialFlying SpecialFlyingInstructions = SpecialFlying.None;

		#endregion
		#endregion

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

		public DateTime waitUntil = DateTime.UtcNow;
		public DateTime waitUntilNoCheck = DateTime.UtcNow;

		public Vector3D? myWaypoint { get; private set; }
		private Vector3D? coordDestination = null;
		public GridDestination CurrentGridDest { get; private set; }
		private Vector3D? value_rotateToPoint = null;
		public Vector3D? rotateToPoint
		{
			get { return value_rotateToPoint; }
			set
			{
				if (value == value_rotateToPoint)
					return;

				value_rotateToPoint = value;
				if (value.HasValue)
					SpecialFlyingInstructions |= SpecialFlying.HybridOnly;
				else
					SpecialFlyingInstructions &= ~SpecialFlying.HybridOnly;
			}
		}

		public Vector3 destination_offset = Vector3.Zero;
		public Base6Directions.Direction? match_direction = null; // reset on reached dest
		public Base6Directions.Direction? match_roll = null;

		public IMyCubeBlock landLocalBlock = null; // for landing or docking (primary)
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
		public IMyCubeBlock landingSeparateBlock;

		/// <summary>Set by B command, before use of block is known</summary>
		public string tempBlockName = null;
		/// <summary>The name of the block to lock onto.</summary>
		public string lockOnBlock = null;

		/// <summary>How close to get to the destination.</summary>
		public int destinationRadius;
		/// <summary>Ignore enemies that are further than. 0 == infinite.</summary>
		public int lockOnRangeEnemy = 0;

		/// <summary>Indicates that the ship is currently a missile, not the same as lockOnTarget == Target.MISSILE</summary>
		public bool isAMissile = false;
		/// <summary>GridTargeter has found a target.</summary>
		public bool target_locked = false;
		/// <summary>Reached an EXIT instruction</summary>
		public bool EXIT = false;
		public bool jump_to_dest
		{
			get { return false; }
			set { }
		}
		/// <summary>Pathfinder should ignore asteroids.</summary>
		public bool ignoreAsteroids = false;

		public NavSettings(Navigator owner)
		{
			this.myNav = owner;
			speedCruise_internal = ServerSettings.GetSetting<float>(ServerSettings.SettingName.fMaxSpeed);
			speedSlow_internal = ServerSettings.GetSetting<float>(ServerSettings.SettingName.fMaxSpeed);

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
			speedCruise_external = ServerSettings.GetSetting<float>(ServerSettings.SettingName.fDefaultSpeed);
			speedSlow_external = ServerSettings.GetSetting<float>(ServerSettings.SettingName.fMaxSpeed);
			destinationRadius = 100;
		}

		/// <summary>
		/// reset some variables when adding or removing a waypoint or centreDestination
		/// </summary>
		private void onWayDestAddedRemoved()
		{
			myCubeGrid.throwIfNull_variable("myCubeGrid");
			//collisionUpdateSinceWaypointAdded = 0;
			clearSpeedInternal();
		}

		#region Speed

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
			set { value_speedSlow_external = MathHelper.Min(value, ServerSettings.GetSetting<float>(ServerSettings.SettingName.fMaxSpeed)); }
		}
		//public const float speedSlow_minimum = 0.25f;

		public float getSpeedCruise()
		{
			float minSetting = Math.Max(Math.Min(speedCruise_internal, speedCruise_external), 0.2f);
			float maxSetting = getSpeedSlow();
			//myLogger.debugLog("min of (" + speedCruise_internal + ", " + speedCruise_external + ", " + maxSetting + ")", "getCruiseSpeed", Logger.severity.TRACE);
			if (minSetting < maxSetting)
				return minSetting;
			else
				return maxSetting - 1;
		}
		public float getSpeedSlow()
		{
			//myLogger.debugLog("min of (" + speedSlow_internal + ", " + speedSlow_external +")", "getSpeedSlow", Logger.severity.TRACE);
			return Math.Max(Math.Min(speedSlow_internal, speedSlow_external), 0.3f);
		}
		public void clearSpeedInternal()
		{
			//myLogger.debugLog("entered clearSpeedInternal()", "clearSpeedInternal()", Logger.severity.TRACE);
			speedCruise_internal = ServerSettings.GetSetting<float>(ServerSettings.SettingName.fMaxSpeed);
			speedSlow_internal = ServerSettings.GetSetting<float>(ServerSettings.SettingName.fMaxSpeed);
		}

		#endregion

		private Logger myLogger = new Logger(null, "NavSettings");

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
		/// removes the waypoint if one exists, otherwise removes the destination
		/// </summary>
		public void atWayDest()
		{ atWayDest(getTypeOfWayDest()); }

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
					myLogger.debugLog("clearing destination variables", "atWayDest()", Logger.severity.TRACE);
					CurrentGridDest = null;
					coordDestination = null;
					rotateToPoint = null;
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
				case TypeOfWayDest.NONE:
					// sometimes destinations get cleared twice, so ignore
					return;
				default:
					myLogger.debugLog("unknown type " + typeToRemove, "atWayDest()", Logger.severity.ERROR);
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
				case TypeOfWayDest.NONE:
					return null;
				default:
					myLogger.debugLog("unknown type " + type, "getWayDest()", Logger.severity.ERROR);
					return null;
			}
		}

		public TypeOfWayDest getTypeOfWayDest(bool getWaypoint = true)
		{
			if (getWaypoint && myWaypoint != null)
				return TypeOfWayDest.WAYPOINT;

			if (CurrentGridDest != null)
			{
				if (CurrentGridDest.ValidBlock())
				{
					if (landLocalBlock != null)
						return TypeOfWayDest.LAND;
					if (destination_offset != Vector3.Zero)
						return TypeOfWayDest.OFFSET;
					else
						return TypeOfWayDest.BLOCK;
				}
				else if (CurrentGridDest.ValidGrid())
					return TypeOfWayDest.GRID;
			}

			if (coordDestination != null)
				return TypeOfWayDest.COORDINATES;

			return TypeOfWayDest.NONE;
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
						//myLogger.debugLog("landingState=" + landingState + ", useLandOffset=" + useLandOffset + ", landOffset=" + landOffset, "offsetToWorld()", Logger.severity.TRACE);
						break;
					case LANDING.LAND:
						useLandOffset = Vector3.Zero;
						//myLogger.debugLog("landingState=" + landingState + ", useLandOffset=" + useLandOffset + ", landOffset=" + landOffset, "offsetToWorld()", Logger.severity.TRACE);
						break;
				}
			}

			Vector3D world = (destination_offset.X + useLandOffset.X) * CurrentGridDest.Block.WorldMatrix.Right;
			world += (destination_offset.Y + useLandOffset.Y) * CurrentGridDest.Block.WorldMatrix.Up;
			world += (destination_offset.Z + useLandOffset.Z) * CurrentGridDest.Block.WorldMatrix.Backward;

			//myLogger.debugLog("grabbed vectors: " + Base6Directions.GetVector(CurrentGridDest.Block.Orientation.Left) + ", " + Base6Directions.GetVector(CurrentGridDest.Block.Orientation.Up) + ", " + Base6Directions.GetVector(CurrentGridDest.Block.Orientation.Forward), "offsetToWorld()");
			//myLogger.debugLog("destination_offset=" + destination_offset + ", landingState=" + landingState + ", landOffset is " + landOffset + ", useLandOffset is " + useLandOffset + ", world vector is " + world, "offsetToWorld()", Logger.severity.TRACE);
			return world;
		}

		public void getOrientationOfDest(out Vector3D? direction, out Vector3D? roll)
		{
			switch (getTypeOfWayDest())
			{
				case TypeOfWayDest.BLOCK:
				case TypeOfWayDest.OFFSET:
				case TypeOfWayDest.LAND:
					myLogger.debugLog("match_direction=" + match_direction + ", match_roll=" + match_roll, "getOrientationOfDest()");
					if (match_direction == null) { direction = null; roll = null; return; }

					// do direction
					direction = CurrentGridDest.Block.WorldMatrix.GetDirectionVector((Base6Directions.Direction)match_direction);

					if (match_roll == null) { roll = null; return; }

					// do roll
					roll = CurrentGridDest.Block.WorldMatrix.GetDirectionVector((Base6Directions.Direction)match_roll);

					return;
				default:
					{
						myLogger.debugLog("orientation not supported for " + getTypeOfWayDest(), "getOrientationOfDest()", Logger.severity.DEBUG);
						direction = null; roll = null; return;
					}
			}
		}

		public string GridDestName
		{
			get
			{
				if (CurrentGridDest == null || CurrentGridDest.Grid == null)
					return null;
				return CurrentGridDest.Grid.DisplayName;
			}
		}

		public string BlockDestName
		{
			get
			{
				if (CurrentGridDest == null || CurrentGridDest.Block == null)
					return null;
				return CurrentGridDest.Block.DisplayNameText;
			}
		}

	}
}
