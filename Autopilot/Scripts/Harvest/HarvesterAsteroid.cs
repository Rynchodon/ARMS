#define LOG_ENABLED //remove on build

using System;
using System.Collections.Generic;
using Rynchodon.Autopilot.NavigationSettings;
using Sandbox.Common.ObjectBuilders;
using Sandbox.ModAPI;
using VRage;
using VRageMath;
using Ingame = Sandbox.ModAPI.Ingame;

namespace Rynchodon.Autopilot.Harvest
{
	/// <summary>
	/// Controls the movement of a ship for purposes of harvesting from an asteroid.
	/// </summary>
	/// It is critical that there is always a destination until Harvester is finished.
	internal class HarvesterAsteroid
	{
		private static bool CreativeMode = MyAPIGateway.Session.CreativeMode;

		private const float FullAmount_Abort = 0.9f, FullAmount_Return = 0.1f;
		private const float rotLenSq_rotate = 0.00762f; // 5°
		private const float rotLenSq_offCourse = 0.685f; // 15°

		private readonly Navigator myNav;
		private readonly CubeGridCache myCache;
		public IMyCubeGrid myCubeGrid { get { return myNav.myGrid; } }
		public NavSettings CNS { get { return myNav.CNS; } }
		public IMyCubeBlock NavigationDrill { get; private set; }

		private static readonly TimeSpan StuckAfter = new TimeSpan(0, 0, 10);
		private DateTime StuckAt = DateTime.MaxValue;
		private bool IsStuck = false;

		private Logger myLogger;

		private Navigator.ReportableState value_HarvestState;
		public Navigator.ReportableState HarvestState
		{
			get
			{
				if (IsStuck)
					return Navigator.ReportableState.H_Stuck;
				return value_HarvestState;
			}
			private set { value_HarvestState = value; }
		}

		private bool Tunnel = false;

		/// <summary>target point inside asteroid</summary>
		private Vector3 targetVoxel;

		/// <summary>determined from a ray cast from nav drill to target</summary>
		private Vector3 closestVoxel;

		public HarvesterAsteroid(Navigator myNav)
		{
			this.myNav = myNav;
			this.myCache = CubeGridCache.GetFor(myCubeGrid);
			this.myLogger = new Logger("HarvesterAsteroid", () => myCubeGrid.DisplayName, () => { return CNS.moveState + ":" + CNS.rotateState; });
			HarvestState = Navigator.ReportableState.H_Ready;
		}

		#region Public Functions

		/// <summary>Start harvesting.</summary>
		/// <returns>true iff harvesting could be started</returns>
		public void Start()
		{
			LogEntered("Start()");
			HarvestState = Navigator.ReportableState.H_Ready;

			if (DrillFullness(GetDrills()) > FullAmount_Return)
			{
				myLogger.debugLog("drills are already full", "Start()");
				Finished();
				return;
			}

			try
			{
				IEnumerator<IMyCubeBlock> drillEnum = GetDrills().GetEnumerator();
				drillEnum.MoveNext();
				NavigationDrill = drillEnum.Current as IMyCubeBlock; // while harvester is random, drill choice does not matter (assuming all drills face the same way)

				CNS_Store();
				CNS.setDestination(GetClosestAsteroid().WorldVolume.Center); // need an initial destination or we keep parsing instructions

				if (CreativeMode)
				{
					myLogger.debugLog("Disabling use converyors for drills", "Start()");
					foreach (Ingame.IMyShipDrill drill in GetDrills())
						if (drill.UseConveyorSystem)
							drill.GetActionWithName("UseConveyor").Apply(drill);
				}

				myLogger.debugLog("Started harvester", "Start()");
				SetNextStage(Ready, false);
				return;
			}
			catch (Exception ex)
			{
				myLogger.debugLog("Failed to start, Ex: " + ex, "Start()");
				return;
			}
		}

		///<summary>Continue harvesting</summary>
		/// <returns>true iff harvesting actions were performed</returns>
		public bool Run()
		{
			if (CurrentAction == null)
			{
				//myLogger.debugLog("no action to take", "Run()");
				return false;
			}

			//myLogger.debugLog("Linear Speed Squared = " + myCubeGrid.Physics.LinearVelocity.LengthSquared() + ", Angular Speed Squared = " + myCubeGrid.Physics.AngularVelocity.LengthSquared(), "Run()");
			if (myCubeGrid.Physics.LinearVelocity.LengthSquared() > 0.0625f || myCubeGrid.Physics.AngularVelocity.LengthSquared() > 0.0625)
			{
				StuckAt = DateTime.UtcNow + StuckAfter;
				IsStuck = false;
			}
			else if (!IsStuck && DateTime.UtcNow > StuckAt)
			{
				myLogger.debugLog("Is now stuck", "Run()");
				IsStuck = true;
			}

			SetDrills();

			//myLogger.debugLog("Entered Run()", "Run()");
			CurrentAction();
			return true;
		}

		#endregion
		#region Stage Action

		private Vector3D ReadyPoint;

		/// <summary>
		/// Prepare for harvesting
		/// </summary>
		/// <remarks>
		/// <para>If drills are full, change stage to MoveAway.</para>
		/// <para>Get closest asteroid, pick a random point.</para>
		/// <para>Set CNS settings</para>
		/// <para>Enables Drills.</para>
		/// </remarks>>
		private void Ready()
		{
			LogEntered("Ready()");
			HarvestState = Navigator.ReportableState.H_Ready;

			if (DrillFullness(GetDrills()) > FullAmount_Return)
			{
				myLogger.debugLog("Drills are full: " + DrillFullness(GetDrills()) + " > " + FullAmount_Return, "Ready()");
				SetNextStage(StartMoveAway, false);
				return;
			}

			Vector3D? target = GetUsefulTarget(GetClosestAsteroid().WorldVolume);
			if (!target.HasValue)
			{
				myLogger.debugLog("failed to get a useful point inside asteroid", "Ready()");
				SetNextStage(Ready, false);
				return;
			}
			targetVoxel = (Vector3)target;
			CNS_RestorePrevious();
			CNS.setDestination(closestVoxel);
			CNS.ignoreAsteroids = true;
			ReadyPoint = NavigationDrill.GetPosition();

			myLogger.debugLog("Ready", "Ready()");
			SetNextStage(ApproachAsteroid, false);
		}

		/// <summary>
		/// if outside of asteroid move using closestVoxel
		/// </summary>
		private void ApproachAsteroid()
		{
			LogEntered("ApproachAsteroid()");
			bool inside = IsInsideAsteroid();
			if (inside || myNav.MM.distToWayDest < 10)
			{
				if (inside)
					myLogger.debugLog("inside asteroid", "ApproachAsteroid()");
				else
					myLogger.debugLog("Reached point", "ApproachAsteroid()");
				myNav.fullStop("close to asteroid");
				CNS_SetHarvest();
				CNS.setDestination(targetVoxel);
				CNS.SpecialFlyingInstructions = NavSettings.SpecialFlying.Line_Any;

				SetNextStage(RotateToWaypoint, false);
				return;
			}

			myNav.collisionCheckMoveAndRotate();
		}

		/// <summary>
		/// Rotate to face drill towards waypoint
		/// </summary>
		private void RotateToWaypoint()
		{
			LogEntered("RotateToWaypoint()");

			if (CNS.rotateState == NavSettings.Rotating.NOT_ROTA && myNav.MM.rotLenSq < rotLenSq_rotate)
			{
				myLogger.debugLog("Finished rotating", "RotateToWaypoint()");

				SetNextStage(StartHarvest, true);
				return;
			}
			if (IsStuck)
			{
				myLogger.debugLog("harvester is stuck", "RotateToWaypoint()");
				SetNextStage(StartTunnelThrough, true);
				return;
			}

			myNav.calcAndRotate();
		}

		/// <summary>
		/// Starts harvesting.
		/// </summary>
		private void StartHarvest()
		{
			LogEntered("StartHarvest()");
			HarvestState = Navigator.ReportableState.Harvest;
			CNS.setDestination(targetVoxel);

			if (!VoxelsBetweenNavAndPoint((Vector3D)CNS.getWayDest()))
			{
				myLogger.debugLog("no voxels between nav drill and destination", "StartHarvest()");
				SetNextStage(Ready, false);
				return;
			}
			if (myNav.MM.rotLenSq > rotLenSq_offCourse)
			{
				myLogger.debugLog("too far from correct direction", "StartHarvest()");
				SetNextStage(StartTunnelThrough, false);
				return;
			}

			if (IsInsideAsteroid())
			{
				myLogger.debugLog("now inside asteroid", "StartHarvest()");
				SetNextStage(Harvest, true);
			}

			myNav.collisionCheckMoveAndRotate();
		}

		/// <summary>
		/// Harvest the asteroid
		/// </summary>
		/// <remarks>
		/// <para>Fly towards destination.</para>
		/// <para>If ship becomes full goto backout</para>
		/// <para>If ship gets stuck goto backout</para>
		/// <para>If ship flies all the way through goto Ready</para>
		/// </remarks>
		private void Harvest()
		{
			LogEntered("Harvest()");

			if (IsStuck || DrillFullness(GetDrills()) > FullAmount_Abort)
			{
				if (IsStuck)
					myLogger.debugLog("harvester is stuck", "Harvest()");
				else
					myLogger.debugLog("Drills are full: " + DrillFullness(GetDrills()) + " > " + FullAmount_Abort, "Harvest()");
				SetNextStage(StartBackout, false);
				return;
			}
			if (!IsInsideAsteroid())
			{
				//myLogger.debugLog("harvester reached far side", "Harvest()");
				myNav.fullStop("harvester reached far side");
				SetNextStage(Ready, false);
				return;
			}
			else
			{
				if (Tunnel)
				{
					myLogger.debugLog("Tunnel set", "StartHarvest()");
					SetNextStage(StartTunnelThrough, true);
					return;
				}
			}

			if (myNav.MM.distToWayDest < 10)
			{
				//myLogger.debugLog("reached wayDest: " + CNS.getTypeOfWayDest(), "Harvest()");
				myNav.fullStop("reached target");
				SetNextStage(StartBackout, false);
				return;
			}

			myNav.collisionCheckMoveAndRotate();
		}

		/// <summary>
		/// Starts backing out
		/// </summary>
		private void StartBackout()
		{
			LogEntered("StartBackout()");
			HarvestState = Navigator.ReportableState.H_Back;

			CNS_SetHarvest();
			CNS.setDestination(ReadyPoint);
			CNS.SpecialFlyingInstructions = NavSettings.SpecialFlying.Line_SidelForward;
			myNav.fullStop("StartBackout");

			SetNextStage(Backout, false);
		}

		/// <summary>
		/// Fly backwards until out of asteroid
		/// </summary>
		/// <remarks>
		/// <para>disable drills, fly drills-backwards out of asteroid aabb/volume</para>
		///	<para>if ship gets stuck goto tunnel-through</para>
		///	<para>if backed-out goto Ready</para>
		/// </remarks>
		private void Backout()
		{
			LogEntered("Backout()");
			if (IsStuck)
			{
				myLogger.debugLog("harvester is stuck", "Backout()");
				SetNextStage(StartTunnelThrough, true);
				return;
			}
			if (!IsInsideAsteroid())// || myNav.MM.distToWayDest < 10)
			{
				myLogger.debugLog("harvester backed out successfully", "Backout()");
				myNav.fullStop("backed away from asteroid");
				SetNextStage(Ready, false);
				return;
			}
			if (myNav.MM.distToWayDest < 1)
			{
				myLogger.debugLog("reached way dest, still inside asteroid", "Backout()");
				SetNextStage(StartTunnelThrough, true);
				return;
			}

			myNav.collisionCheckMoveAndRotate();
		}

		private void StartTunnelThrough()
		{
			LogEntered("StartTunnelThrough()");
			HarvestState = Navigator.ReportableState.H_Tunnel;

			Vector3D current = NavigationDrill.GetPosition();
			Vector3D forwardVect = NavigationDrill.WorldMatrix.Forward;
			CNS_SetHarvest();
			CNS.setDestination(current + forwardVect * 128);
			CNS.SpecialFlyingInstructions = NavSettings.SpecialFlying.Line_Any;
			myNav.fullStop("start tunneling through");

			SetNextStage(TunnelThrough, true);
		}

		/// <summary>
		/// Tunnel through the asteroid to the other side.
		/// </summary>
		/// <remarks>
		/// <para>enable drills</para>
		///	<para>fly forwards until outside of asteroid</para>
		/// </remarks>
		private void TunnelThrough()
		{
			LogEntered("TunnelThrough()");

			if (IsStuck)
			{
				myLogger.debugLog("stuck", "TunnelThrough()");
				// if pathfinder is blocking harvester, try backing out
				if (myNav.PathfinderAllowsMovement)
					SetNextStage(StartTunnelThrough, false);
				else
					SetNextStage(StartBackout, false);
				return;
			}
			if (myNav.MM.distToWayDest < 10)
			{
				myLogger.debugLog("reached wayDest", "TunnelThrough()");
				SetNextStage(StartTunnelThrough, true);
				return;
			}
			if (!IsInsideAsteroid())
			{
				myNav.fullStop("tunneled out the other side");
				myLogger.debugLog("tunneled out the other side", "TunnelThrough()");
				SetNextStage(Ready, false);
				return;
			}

			myNav.collisionCheckMoveAndRotate();
		}

		private void StartMoveAway()
		{
			LogEntered("StartMoveAway()");

			myNav.fullStop("StartMoveAway");

			// set dest to a point away from centre
			Vector3D navDrillPos = NavigationDrill.GetPosition();
			Vector3D directionAway = Vector3D.Normalize(navDrillPos - GetClosestAsteroid().WorldVolume.Center);
			Vector3D pointAway = navDrillPos + directionAway * 128;

			CNS_SetHarvest(); // might be inside asteroid
			CNS.setDestination(pointAway);
			CNS.SpecialFlyingInstructions = NavSettings.SpecialFlying.Line_Any;
			myLogger.debugLog("navDrillPos = " + navDrillPos + ", directionAway = " + directionAway + ", pointAway = " + pointAway, "StartMoveAway()");

			SetNextStage(RotateToMoveAway, false);
		}

		/// <summary>
		/// Rotate to face drill towards waypoint
		/// </summary>
		private void RotateToMoveAway()
		{
			LogEntered("RotateToMoveAway()");

			if (CNS.rotateState == NavSettings.Rotating.NOT_ROTA && myNav.MM.rotLenSq < rotLenSq_rotate)
			{
				myLogger.debugLog("Finished rotating", "RotateToMoveAway()");

				SetNextStage(MoveAway, true); // might be inside asteroid, enable drills to escape!
				return;
			}

			if (IsStuck)
			{
				myLogger.debugLog("harvester is stuck", "RotateToMoveAway()");
				SetNextStage(StartTunnelThrough, true);
				return;
			}

			myNav.calcAndRotate();
		}

		/// <summary>
		/// Move out of asteroid's Bounding Box
		/// </summary>
		private void MoveAway()
		{
			LogEntered("MoveAway()");

			// if outside bounds of asteroid, goto finished
			float distance;
			IMyVoxelMap closest = GetClosestAsteroid(out distance);
			if (distance > 10)
			{
				myLogger.debugLog("moved away from asteroid: " + closest.getBestName(), "MoveAway()");
				SetNextStage(Finished, false);
				return;
			}

			// if reached point, goto StartMoveAway
			if (myNav.MM.distToWayDest < 10)
			{
				myLogger.debugLog("reached wayDest", "MoveAway()");
				SetNextStage(StartMoveAway, false);
				return;
			}

			if (IsStuck)
			{
				myLogger.debugLog("harvester is stuck", "MoveAway()");
				SetNextStage(StartTunnelThrough, true);
				return;
			}

			myNav.collisionCheckMoveAndRotate();
		}

		/// <summary>
		/// Quit harvesting.
		/// </summary>
		private void Finished()
		{
			LogEntered("Finished()");
			CNS_RestorePrevious();
			NavigationDrill = null;

			SetNextStage(null, false);
		}

		#endregion

		private bool IsInsideAsteroid()
		{
			List<IMyVoxelBase> allAsteroids = MyAPIGateway.Session.VoxelMaps.GetInstances_Safe((asteroid) => { return myCubeGrid.IntersectsAABBVolume(asteroid); });

			BoundingSphereD volume = myCubeGrid.WorldVolume;
			volume.Radius += 10;
			foreach (IMyVoxelMap asteroid in allAsteroids)
			{
				if (asteroid.GetIntersectionWithSphere(ref volume))
				{
					//myLogger.debugLog("asteroid: " + asteroid.getBestName() + " intersects sphere", "IsInsideAsteroid()");
					return true;
				}
				//else
				//	myLogger.debugLog("not intersect sphere: asteroid (" + asteroid.getBestName() + ")", "IsInsideAsteroid()");
			}

			return false;
		}

		private ReadOnlyList<IMyCubeBlock> GetDrills()
		{
			ReadOnlyList<IMyCubeBlock> allDrills = myCache.GetBlocksOfType(typeof(MyObjectBuilder_Drill));
			return allDrills;
		}

		/// <summary>
		/// <para>In survival, returns fraction of drills filled</para>
		/// <para>In creative, returns content per drill * 0.01</para>
		/// </summary>
		private float DrillFullness(ReadOnlyList<IMyCubeBlock> allDrills)
		{
			MyFixedPoint content = 0, capacity = 0;
			int drillCount = 0;
			foreach (Ingame.IMyShipDrill drill in allDrills)
			{
				IMyInventory drillInventory = (IMyInventory)Ingame.TerminalBlockExtentions.GetInventory(drill, 0);
				content += drillInventory.CurrentVolume;
				capacity += drillInventory.MaxVolume;
				drillCount++;
			}

			if (CreativeMode)
			{
				myLogger.debugLog("content = " + content + ", drillCount = " + drillCount, "DrillFullness()");
				return (float)content * 0.01f / drillCount;
			}
			myLogger.debugLog("content = " + content + ", capacity = " + capacity, "DrillFullness()");
			return (float)content / (float)capacity;
		}

		private IMyVoxelMap GetClosestAsteroid()
		{
			float distance;
			return GetClosestAsteroid(out distance);
		}

		private IMyVoxelMap GetClosestAsteroid(out float distance)
		{
			List<IMyVoxelBase> allAsteroids = MyAPIGateway.Session.VoxelMaps.GetInstances_Safe();
			SortedDictionary<float, IMyVoxelMap> sortedAsteroids = new SortedDictionary<float, IMyVoxelMap>();
			foreach (IMyVoxelMap asteroid in allAsteroids)
			{
				float distanceCurrent = (float)myCubeGrid.WorldAABB.Distance(asteroid.WorldAABB);
				if (distanceCurrent < 1)
				{
					distance = distanceCurrent;
					return asteroid;
				}
				while (sortedAsteroids.ContainsKey(distanceCurrent))
					distanceCurrent = distanceCurrent.IncrementSignificand();
				sortedAsteroids.Add(distanceCurrent, asteroid);
			}
			var enumerator = sortedAsteroids.GetEnumerator();
			enumerator.MoveNext();
			distance = enumerator.Current.Key;
			return enumerator.Current.Value;
		}

		private static Random myRandom = new Random();

		/// <summary>
		/// Gets a random point beyond sphere
		/// </summary>
		private Vector3D GetRandomTarget(BoundingSphereD sphere)
		{
			double randRadius = myRandom.NextDouble() * sphere.Radius / 2;
			double randAngle1 = myRandom.NextDouble() * MathHelper.TwoPi;
			double randAngle2 = myRandom.NextDouble() * MathHelper.TwoPi;

			double x = randRadius * Math.Sin(randAngle1) * Math.Cos(randAngle2);
			double y = randRadius * Math.Sin(randAngle1) * Math.Sin(randAngle2);
			double z = randRadius * Math.Cos(randAngle1);

			return new Vector3D(x, y, z) + sphere.Center;
		}

		/// <summary>
		/// Keep getting random point in sphere until there is one with voxels between it and nav drill
		/// </summary>
		private Vector3D? GetUsefulTarget(BoundingSphereD sphere)
		{
			Vector3 navPos = NavigationDrill.GetPosition();
			for (int attempt = 0; attempt < 10; attempt++)
			{
				Vector3 point = GetRandomTarget(sphere);
				if (VoxelsBetweenNavAndPoint(point))
				{
					Vector3D p0 = point;
					Vector3D travelVector = Vector3D.Normalize(point - NavigationDrill.GetPosition());
					point += travelVector * 128;
					myLogger.debugLog("got a useful point: " + p0 + ", moved forward to " + point, "GetUsefulTarget()");

					return point;
				}
				myLogger.debugLog("totally useless: " + point, "GetUsefulTarget()");
			}
			return null;
		}

		/// <summary>
		/// Determines if there are voxels between navigation drill and given point
		/// </summary>
		private bool VoxelsBetweenNavAndPoint(Vector3D point)
		{ return MyAPIGateway.Entities.RayCastVoxel_Safe(NavigationDrill.GetPosition(), point, out closestVoxel); }

		#region CNS Variables

		private float initial_speedCruise_external, initial_speedSlow_external;

		/// <summary>
		/// variables that are not cleared at each destination go here, they must only be set once
		/// </summary>
		private void CNS_Store()
		{
			initial_speedCruise_external = CNS.speedCruise_external;
			initial_speedSlow_external = CNS.speedSlow_external;
		}

		/// <summary>
		/// Sets the speeds, ignore asteroids, clears waypoint
		/// </summary>
		private void CNS_SetHarvest()
		{
			CNS.atWayDest(NavSettings.TypeOfWayDest.COORDINATES); // clear destination
			CNS.speedCruise_external = 0.5f;
			CNS.speedSlow_external = 2;
			CNS.ignoreAsteroids = true;
			//CNS.FlyTheLine = true;
		}

		/// <summary>
		/// restore the variables that were saved by CNS_Store()
		/// </summary>
		private void CNS_RestorePrevious()
		{
			CNS.atWayDest(NavSettings.TypeOfWayDest.COORDINATES); // clear destination
			CNS.speedCruise_external = initial_speedCruise_external;
			CNS.speedSlow_external = initial_speedSlow_external;
			CNS.ignoreAsteroids = false;
		}

		#endregion
		#region Enable/Disable Drills

		private bool DrillsOn = false;
		private bool DrillState = true;

		private void SetDrills()
		{
			bool enable;

			if (DrillsOn)
			{
				if (IsInsideAsteroid())
				{
					enable = true;
					CNS.speedCruise_external = 0.5f;
					CNS.speedSlow_external = 2;
				}
				else
				{
					enable = false;
					CNS.speedCruise_external = 5f;
					CNS.speedSlow_external = 10;
				}
			}
			else
				enable = false;

			if (enable && (CNS.moveState == NavSettings.Moving.STOP_MOVE || CNS.moveState == NavSettings.Moving.NOT_MOVE))
				enable = false;

			if (enable == DrillState)
				return;

			DrillState = enable;
			foreach (Ingame.IMyShipDrill drill in GetDrills())
				drill.RequestEnable(enable);
		}

		#region Next Action

		private Action CurrentAction = null;

		private void SetNextStage(Action nextStage, bool drillsOn)
		{
			this.CurrentAction = nextStage;
			this.DrillsOn = drillsOn;
			StuckAt = DateTime.UtcNow + StuckAfter;
			IsStuck = false;
		}

		#endregion
		#endregion

		[System.Diagnostics.Conditional("LOG_ENABLED")]
		private void LogEntered(string method)
		{
			string navEnabledString;
			if (NavigationDrill == null)
				navEnabledString = "null";
			else
				navEnabledString = (NavigationDrill as IMyFunctionalBlock).Enabled.ToString();
			if (myNav.MM != null && myNav.CNS != null)
				myLogger.debugLog("entered " + method + ", DrillsOn = " + DrillsOn + ", DrillState = " + DrillState + ", stuck = " + IsStuck + ", nav drill enabled = " + navEnabledString
					+ ", speed = " + myNav.MM.movementSpeed + ", cruise = " + CNS.getSpeedCruise() + ", slow = " + CNS.getSpeedSlow(), method);
		}
	}

	internal static class HarvesterExtensions
	{
		public static bool IsActive(this HarvesterAsteroid harvester)
		{ return harvester != null && harvester.NavigationDrill != null; }
	}
}
