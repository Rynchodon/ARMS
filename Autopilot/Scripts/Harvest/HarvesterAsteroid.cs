#define LOG_ENABLED //remove on build

using System;
using System.Collections.Generic;
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
	internal class HarvesterAsteroid
	{
		private const float FullAmount_Abort = 0.9f, FullAmount_Return = 0.5f;
		private const float rotLenSq_rotate = 0.00762f; // 5°

		private readonly Navigator myNav;
		private readonly CubeGridCache myCache;
		public IMyCubeGrid myCubeGrid { get { return myNav.myGrid; } }
		public NavSettings CNS { get { return myNav.CNS; } }
		public IMyCubeBlock NavigationDrill { get; private set; }

		private static readonly TimeSpan StuckAfter = new TimeSpan(0, 0, 10);
		private DateTime StuckAt = DateTime.MaxValue;
		private bool IsStuck = false;

		private Logger myLogger;

		///// <summary>Current stage of harvesting</summary>
		//private Action StageAction;

		public HarvesterAsteroid(Navigator myNav)
		{
			this.myNav = myNav;
			this.myCache = CubeGridCache.GetFor(myCubeGrid);
			this.myLogger = new Logger("HarvesterAsteroid", () => myCubeGrid.DisplayName, () => { return CNS.moveState + ":" + CNS.rotateState; });
		}

		/// <summary>Start harvesting.</summary>
		/// <returns>true iff harvesting could be started</returns>
		public bool Start()
		{
			LogEntered("Start()");
			try
			{
				IEnumerator<Ingame.IMyTerminalBlock> drillEnum = GetDrills().GetEnumerator();
				drillEnum.MoveNext();
				NavigationDrill = drillEnum.Current as IMyCubeBlock; // while harvester is random, drill choice does not matter (assuming all drills face the same way)
				//EnableDrills(false);

				CNS_Store();
				//CNS_SetHarvest();

				myLogger.debugLog("Started harvester", "Start()");
				SetNextStage(Ready, false);
				return Run();
			}
			catch (Exception ex)
			{
				myLogger.debugLog("Failed to start, Ex: " + ex, "Start()");
				return false;
			}
		}

		///<summary>Continue harvesting</summary>
		/// <returns>true iff harvesting actions were performed</returns>
		public bool Run()
		{
			if (CurrentAction == null)
				return false;

			//myLogger.debugLog("Linear Velocity Squared = " + myCubeGrid.Physics.LinearVelocity.LengthSquared(), "Run()");
			if (myCubeGrid.Physics.LinearVelocity.LengthSquared() > 0.25f)
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

		#region Stage Action

		/// <summary>
		/// Approach asteroid.
		/// </summary>
		private void Approach()
		{
			LogEntered("Approach()");
			VRage.Exceptions.ThrowIf<NotImplementedException>(true);
		}

		private Vector3D ReadyPoint;

		/// <summary>
		/// Prepare for harvesting
		/// </summary>
		/// <remarks>
		/// <para>If drills are full, change stage to Finished.</para>
		/// <para>Get closest asteroid, pick a random point.</para>
		/// <para>Set CNS settings</para>
		/// <para>Enables Drills.</para>
		/// </remarks>>
		private void Ready()
		{
			LogEntered("Ready()");
			if (DrillFullness(GetDrills()) > FullAmount_Return)
			{
				myLogger.debugLog("Drills are full: " + DrillFullness(GetDrills()) + " > " + FullAmount_Return, "Ready()");
				SetNextStage(Finished, false);
				return;
			}
			ReadyPoint = NavigationDrill.GetPosition();
			//CNS.setDestination(NavigationDrill.GetPosition()); // will return to this point when backing out
			CNS.setDestination(GetRandomTarget(GetClosestAsteroid().WorldVolume));
			CNS_SetHarvest();
			CNS.SpecialFlyingInstructions = NavSettings.SpecialFlying.Line_Any;
			//CNS.speedCruise_external = 5;
			//CNS.speedSlow_external = 10;

			myLogger.debugLog("Ready", "Ready()");
			SetNextStage(RotateToWaypoint, false);
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
				//StageAction = MoveToAsteroid;
				//EnableDrills(true);
				

				SetNextStage(StartHarvest, true);
				return;
			}
			myNav.calcAndRotate();
		}

		///// <summary>
		///// Move right up to asteroid
		///// </summary>
		//private void MoveToAsteroid()
		//{
		//	LogEntered("MoveToAsteroid");

			

		//	myNav.collisionCheckMoveAndRotate();
		//}

		/// <summary>
		/// Starts harvesting.
		/// </summary>
		private void StartHarvest()
		{
			LogEntered("StartHarvest()");
			if (myNav.MM.rotLenSq > rotLenSq_rotate)
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
		/// <para>If ship flies all the way through goto finished</para>
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

			CNS.atWayDest(); // consider destination reached
			CNS.setDestination(ReadyPoint);
			CNS_SetHarvest();
			CNS.SpecialFlyingInstructions = NavSettings.SpecialFlying.Line_SidelForward;
			myNav.fullStop("StartBackout");

			SetNextStage(Backout, false);
		}

		//private void WaitForMoving_Backout()
		//{
		//	LogEntered("WaitForMoving_Backout()");

		//	if (!IsStuck)
		//	{
		//		myLogger.debugLog("started backing out", "StartBackout()");
		//		SetNextStage(Backout, false);
		//	}

		//	myNav.collisionCheckMoveAndRotate();
		//}

		/// <summary>
		/// Fly backwards until out of asteroid
		/// </summary>
		/// <remarks>
		/// <para>disable drills, fly drills-backwards out of asteroid aabb/volume</para>
		///	<para>if ship gets stuck goto tunnel-through</para>
		///	<para>if backed-out goto finished</para>
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
			if (!IsInsideAsteroid() || myNav.MM.distToWayDest < 10)
			{
				myLogger.debugLog("harvester backed out successfully", "Backout()");
				myNav.fullStop("backed away from asteroid");
				SetNextStage(Ready, false);
				return;
			}

			myNav.collisionCheckMoveAndRotate();
		}

		private void StartTunnelThrough()
		{
			LogEntered("StartTunnelThrough()");
			CNS.atWayDest(); // consider destination reached
			Vector3D current = NavigationDrill.GetPosition();
			Vector3D forwardVect = NavigationDrill.WorldMatrix.Forward;
			CNS.setDestination(current + forwardVect * 128);
			CNS_SetHarvest();
			CNS.SpecialFlyingInstructions = NavSettings.SpecialFlying.Line_Any;
			myNav.fullStop("start tunneling through");

			SetNextStage(TunnelThrough, true);
		}

		//private void WaitForMoving_TunnelThrough()
		//{
		//	LogEntered("WaitForMoving_TunnelThrough()");

		//	if (!IsStuck)
		//	{
		//		myLogger.debugLog("started tunneling through", "StartBackout()");
		//		SetNextStage(TunnelThrough, true);
		//	}

		//	myNav.collisionCheckMoveAndRotate();
		//}

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

			if (IsStuck || myNav.MM.distToWayDest < 10)
			{
				if (IsStuck)
					myLogger.debugLog("stuck", "TunnelThrough()");
				else
					myLogger.debugLog("reached wayDest", "TunnelThrough()");
				SetNextStage(StartTunnelThrough, true);
				return;
			}
			if (!IsInsideAsteroid())
			{
				myLogger.debugLog("tunneled out the other side", "TunnelThrough()");
				SetNextStage(Ready, false);
				return;
			}

			myNav.collisionCheckMoveAndRotate();
		}

		/// <summary>
		/// Either reset for another harvest or stop harvesting.
		/// </summary>
		/// <remarks>
		/// <para>disable drills</para>
		///	<para>enable asteroid avoidance</para>
		/// <para>clear speed limits</para>
		/// </remarks>
		private void Finished()
		{
			LogEntered("Finished()");
			CNS.atWayDest(); // consider destination reached
			CNS_RestorePrevious();
			NavigationDrill = null;

			SetNextStage(null, false);
		}

		#endregion

		private bool IsInsideAsteroid()
		{
			List<IMyVoxelMap> allAsteroids = MyAPIGateway.Session.VoxelMaps.GetInstances_Safe((asteroid) => { return myCubeGrid.IntersectsAABBVolume(asteroid); });
			return allAsteroids.Count > 0;
		}

		private ReadOnlyList<Ingame.IMyTerminalBlock> GetDrills()
		{
			ReadOnlyList<Ingame.IMyTerminalBlock> allDrills = myCache.GetBlocksOfType(typeof(MyObjectBuilder_Drill));
			return allDrills;
		}

		private float DrillFullness(ReadOnlyList<Ingame.IMyTerminalBlock> allDrills)
		{
			MyFixedPoint content = 0, capacity = 0;
			foreach (Ingame.IMyShipDrill drill in allDrills)
			{
				IMyInventory drillInventory = (IMyInventory)Ingame.TerminalBlockExtentions.GetInventory(drill, 0);
				content += drillInventory.CurrentVolume;
				capacity += drillInventory.MaxVolume;
			}
			return (float)content / (float)capacity;
		}

		private IMyVoxelMap GetClosestAsteroid()
		{
			List<IMyVoxelMap> allAsteroids = MyAPIGateway.Session.VoxelMaps.GetInstances_Safe();
			SortedDictionary<float, IMyVoxelMap> sortedAsteroids = new SortedDictionary<float, IMyVoxelMap>();
			foreach (IMyVoxelMap asteroid in allAsteroids)
			{
				float distance = (float)myCubeGrid.WorldVolume.Distance(asteroid.WorldVolume);
				while (sortedAsteroids.ContainsKey(distance))
					distance.IncrementSignificand();
				sortedAsteroids.Add(distance, asteroid);
			}
			var enumerator = sortedAsteroids.GetEnumerator();
			enumerator.MoveNext();
			return enumerator.Current.Value;
		}

		private static Random myRandom = new Random(4);

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

			Vector3D RandomPoint = new Vector3D(x, y, z) + sphere.Center;

			// set destination to really far ahead
			//Vector3D travelVector = Vector3D.Normalize(RandomPoint - NavigationDrill.GetPosition());
			//Vector3D destination = travelVector * 131072;

			//Vector3D RandomPoint = new Vector3D(x, y, z);
			//myLogger.debugLog("Random point inside sphere: {" + sphere.Center + ", " + sphere.Radius + "} is " + RandomPoint, "GetRandomPointInside()");
			return RandomPoint;
		}

		#region CNS Variables

		private float initial_speedCruise_external, initial_speedSlow_external;
		//private int initial_destinationRadius;

		/// <summary>
		/// variables that are not cleared at each destination go here, they must only be set once
		/// </summary>
		private void CNS_Store()
		{
			initial_speedCruise_external = CNS.speedCruise_external;
			initial_speedSlow_external = CNS.speedSlow_external;
			//initial_destinationRadius = CNS.destinationRadius;
		}

		private void CNS_SetHarvest()
		{
			CNS.speedCruise_external = 0.5f;
			CNS.speedSlow_external = 5;
			CNS.ignoreAsteroids = true;
			//CNS.FlyTheLine = true;
			CNS.atWayDest(NavSettings.TypeOfWayDest.WAYPOINT); // clear waypoint
		}

		/// <summary>
		/// restore the variables that were saved by CNS_SetHarvest()
		/// </summary>
		private void CNS_RestorePrevious()
		{
			CNS.speedCruise_external = initial_speedCruise_external;
			CNS.speedSlow_external = initial_speedSlow_external;
			//CNS.destinationRadius = initial_destinationRadius;
		}

		#endregion
		#region Enable/Disable Drills

		private bool DrillsOn = false;
		//private bool DrillsAreEnabled = true;

		private void SetDrills()
		{
			bool enable = DrillsOn;

			if (enable)
				enable = CNS.moveState == NavSettings.Moving.MOVING;

			//if (DrillsAreEnabled == enable)
			//	return;

			//DrillsAreEnabled = enable;
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
			myLogger.debugLog("entered " + method + ", DrillsOn = " + DrillsOn + ", stuck = " + IsStuck + ", nav drill enabled = " + navEnabledString, method);
		}
	}
}
