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

		private readonly Navigator myNav;
		private readonly CubeGridCache myCache;
		public IMyCubeGrid myCubeGrid { get { return myNav.myGrid; } }
		public NavSettings CNS { get { return myNav.CNS; } }
		private Logger myLogger;

		/// <summary>Current stage of harvesting</summary>
		private Action StageAction;

		public HarvesterAsteroid(Navigator myNav)
		{
			this.myNav = myNav;
			this.myCache = CubeGridCache.GetFor(myCubeGrid);
			this.myLogger = new Logger("HarvesterAsteroid", () => myCubeGrid.DisplayName);
		}

		#region Control

		/// <summary>Start harvesting.</summary>
		public void Start()
		{
			CNS_SetHarvest();
			Ready();
		}

		///<summary>Continue harvesting</summary>
		/// <returns>true iff harvesting actions were performed</returns>
		public bool Run()
		{
			if (StageAction == null)
				return false;
			StageAction();
			return true;
		}

		/// <remarks>
		/// <para>If drills are full, change stage to Backout.</para>
		/// <para>Get closest asteroid, pick a random point.</para>
		/// <para>Set CNS settings</para>
		/// <para>Enables Drills.</para>
		/// </remarks>>
		private void Ready()
		{
			ReadOnlyList<Ingame.IMyTerminalBlock> allDrills = GetDrills();
			if (DrillFullness(allDrills) > FullAmount_Return)
			{
				StageAction = Backout;
				return;
			}
			CNS.setDestination(GetRandomPointInside(GetClosestAsteroid().WorldVolume));
			foreach (Ingame.IMyShipDrill drill in allDrills)
				drill.RequestEnable(true);

			StageAction = Harvest;
		}

		/// <remarks>
		/// <para>Fly towards destination.</para>
		/// <para>If ship becomes full goto backout</para>
		/// <para>If ship gets stuck goto backout</para>
		/// <para>If ship flies all the way through goto finished</para>
		/// </remarks>
		private void Harvest()
		{
			if (DrillFullness(GetDrills()) > FullAmount_Abort || myNav.checkStopped())
			{
				StageAction = Backout;
				return;
			}
			if (!IsInsideAsteroid())
			{
				StageAction = Finished;
				return;
			}
			myNav.collisionCheckMoveAndRotate();
		}

		/// <remarks>
		/// <para>disable drills, fly drills-backwards out of asteroid aabb/volume</para>
		///	<para>if ship gets stuck goto tunnel-through</para>
		///	<para>if backed-out goto finished</para>
		/// </remarks>
		private void Backout()
		{
		}

		/// <remarks>
		/// <para>enable drills</para>
		///	<para>fly forwards until outside of asteroid</para>
		/// </remarks>
		private void TunnelThrough()
		{
			foreach (Ingame.IMyShipDrill drill in GetDrills())
				drill.RequestEnable(true);
		}

		/// <remarks>
		/// <para>disable drills</para>
		///	<para>enable asteroid avoidance</para>
		/// <para>clear speed limits</para>
		/// </remarks>
		private void Finished()
		{
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

		private Vector3D GetRandomPointInside(BoundingSphereD sphere)
		{
			VRage.Exceptions.ThrowIf<NotImplementedException>(true);
			return new Vector3D();
		}

		#region CNS Variables

		private float initial_speedCruise_external, initial_speedSlow_external;
		//private int initial_destinationRadius;

		/// <summary>
		/// variables that are not cleared at each destination go here, they must only be set once
		/// </summary>
		private void CNS_SetHarvest()
		{
			initial_speedCruise_external = CNS.speedCruise_external;
			initial_speedSlow_external = CNS.speedSlow_external;
			//initial_destinationRadius = CNS.destinationRadius;

			CNS.speedCruise_external = 1;
			CNS.speedSlow_external = 2;
			//CNS.destinationRadius = 3;

			CNS_SetHarvestWay();
		}

		/// <summary>
		/// variables that are cleared at each waypoint go here, they must be set more than once
		/// </summary>
		private void CNS_SetHarvestWay()
		{
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
