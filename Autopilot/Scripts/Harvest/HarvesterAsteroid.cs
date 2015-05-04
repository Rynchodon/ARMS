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

		private Logger myLogger;

		/// <summary>Current stage of harvesting</summary>
		private Action StageAction;

		public HarvesterAsteroid(Navigator myNav)
		{
			this.myNav = myNav;
			this.myCache = CubeGridCache.GetFor(myCubeGrid);
			this.myLogger = new Logger("HarvesterAsteroid", () => myCubeGrid.DisplayName);
		}

		/// <summary>Start harvesting.</summary>
		/// <returns>true iff harvesting could be started</returns>
		public bool Start()
		{
			myLogger.debugLog("Entered Start()", "Start()");
			try
			{
				IEnumerator<Ingame.IMyTerminalBlock> drillEnum = GetDrills().GetEnumerator();
				drillEnum.MoveNext();
				NavigationDrill = drillEnum.Current as IMyCubeBlock; // while harvester is random, drill choice does not matter (assuming all drills face the same way)

				CNS.setDestination(NavigationDrill.GetPosition()); // will return to this point when backing out
				CNS_Store();
				CNS_SetHarvest();

				myLogger.debugLog("Started harvester", "Start()");
				Ready();
				return true;
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
			//myLogger.debugLog("Entered Run()", "Run()");
			if (StageAction == null)
				return false;
			StageAction();
			return true;
		}

		#region Stage Action

		/// <summary>
		/// Approach asteroid.
		/// </summary>
		private void Approach()
		{
			myLogger.debugLog("Entered Approach()", "Approach()");
			VRage.Exceptions.ThrowIf<NotImplementedException>(true);
		}

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
			myLogger.debugLog("Entered Ready()", "Ready()");
			if (DrillFullness(GetDrills()) > FullAmount_Return)
			{
				myLogger.debugLog("Drills are full: " + DrillFullness(GetDrills()) + " > " + FullAmount_Return, "Ready()");
				StageAction = Finished;
				return;
			}
			CNS.setWaypoint(GetRandomPointInside(GetClosestAsteroid().WorldVolume));
			CNS_SetHarvest();

			myLogger.debugLog("Ready", "Ready()");
			StageAction = RotateToWaypoint;
		}

		/// <summary>
		/// Rotate to face drill towards waypoint
		/// </summary>
		private void RotateToWaypoint()
		{
			myLogger.debugLog("Entered RotateToWaypoint()", "RotateToWaypoint()");
			if (CNS.rotateState == NavSettings.Rotating.NOT_ROTA && myNav.MM.rotLenSq < rotLenSq_rotate)
			{
				myLogger.debugLog("Finished rotating", "RotateToWaypoint()");
				StageAction = MoveToAsteroid;
				return;
			}
			myNav.calcAndRotate();
		}

		/// <summary>
		/// Move right up to asteroid
		/// </summary>
		private void MoveToAsteroid()
		{
			myLogger.debugLog("Entered MoveToAsteroid()", "MoveToAsteroid()");
			if (IsInsideAsteroid() && myNav.checkStopped())
			{
				foreach (Ingame.IMyShipDrill drill in GetDrills())
					drill.RequestEnable(true);
				myLogger.debugLog("started", "MoveToAsteroid()");
				StageAction = StartHarvest;
			}

			myNav.collisionCheckMoveAndRotate();
		}

		/// <summary>
		/// Starts harvesting.
		/// </summary>
		private void StartHarvest()
		{
			myLogger.debugLog("Entered StartHarvest()", "StartHarvest()");
			if (!myNav.checkStopped())
			{
				myLogger.debugLog("now moving", "StartHarvest()");
				StageAction = Harvest;
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
			myLogger.debugLog("Entered Harvest()", "Harvest()");
			if (DrillFullness(GetDrills()) > FullAmount_Abort )
			{
				myLogger.debugLog("Drills are full: " + DrillFullness(GetDrills()) + " > " + FullAmount_Abort, "Harvest()");
				CNS.atWayDest(NavSettings.TypeOfWayDest.WAYPOINT); // consider waypoint reached
				CNS_SetHarvest();
				StageAction = StartBackout;
				return;
			}
			if (myNav.checkStopped())
			{
				myLogger.debugLog("harvester is stuck", "Harvest()");
				CNS.atWayDest(NavSettings.TypeOfWayDest.WAYPOINT); // consider waypoint reached
				CNS_SetHarvest();
				StageAction = StartBackout;
				return;
			}
			if (!IsInsideAsteroid())
			{
				myLogger.debugLog("harvester reached far side", "Harvest()");
				StageAction = Ready;
				return;
			}

			// TODO: check distance to waypoint

			myNav.collisionCheckMoveAndRotate();
		}

		/// <summary>
		/// Starts backing out
		/// </summary>
		private void StartBackout()
		{
			myLogger.debugLog("Entered StartBackout()", "StartBackout()");
			if (!myNav.checkStopped() && IsInsideAsteroid())
			{
				myLogger.debugLog("started", "StartBackout()");
				StageAction = Backout;
			}

			myNav.collisionCheckMoveAndRotate();
		}

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
			myLogger.debugLog("Entered Backout()", "Backout()");
			if (myNav.checkStopped())
			{
				myLogger.debugLog("harvester is stuck", "Backout()");
				CNS.atWayDest(NavSettings.TypeOfWayDest.COORDINATES); // consider destination reached
				CNS_SetHarvest();
				StageAction = TunnelThrough;
				return;
			}
			if (!IsInsideAsteroid())
			{
				myLogger.debugLog("harvester backed out successfully", "Backout()");
				CNS.atWayDest(NavSettings.TypeOfWayDest.COORDINATES); // consider destination reached
				CNS_SetHarvest();
				StageAction = Ready;
				return;
			}

			myNav.collisionCheckMoveAndRotate();
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
			myLogger.debugLog("Entered TunnelThrough()", "TunnelThrough()");
			foreach (Ingame.IMyShipDrill drill in GetDrills())
				drill.RequestEnable(true);

			// incomplete
			myLogger.debugLog("successfully reached TunnelThrough()! which is not implemented...", "TunnelThrough()");
			VRage.Exceptions.ThrowIf<NotImplementedException>(true);
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
			myLogger.debugLog("Entered Finished()", "Finished()");
			foreach (Ingame.IMyShipDrill drill in GetDrills())
				drill.RequestEnable(false);
			CNS_RestorePrevious();
			StageAction = null;
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

		private static Random myRandom = new Random();

		/// <summary>
		/// Gets a random point inside sphere
		/// </summary>
		private Vector3D GetRandomPointInside(BoundingSphereD sphere)
		{
			double randRadius = myRandom.NextDouble() * sphere.Radius;
			double randAngle1 = myRandom.NextDouble() * MathHelper.TwoPi;
			double randAngle2 = myRandom.NextDouble() * MathHelper.TwoPi;

			double x = randRadius * Math.Sin(randAngle1) * Math.Cos(randAngle2);
			double y = randRadius * Math.Sin(randAngle1) * Math.Sin(randAngle2);
			double z = randRadius * Math.Cos(randAngle1);

			Vector3D RandomPoint = new Vector3D(x, y, z);
			myLogger.debugLog("Random point inside sphere: {" + sphere.Center + ", " + sphere.Radius + "} is " + RandomPoint, "GetRandomPointInside()");
			return sphere.Center + RandomPoint;
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
			CNS.speedCruise_external = 5;
			CNS.speedSlow_external = 10;
			CNS.ignoreAsteroids = true;
			CNS.FlyTheLine = true;
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
	}
}
