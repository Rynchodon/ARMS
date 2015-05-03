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
	///		harvest:
	/// Pick a random point inside the asteroid. (bias towards middle?)
	/// Disable asteroid obstruction test
	/// Enable drills.
	/// Fly drills-forward towards point - and past it
	/// If ship becomes full goto backout
	/// If ship gets stuck goto backout
	/// If ship flies all the way through goto finished
	/// 
	///		backout:
	///	disable drills, fly drills-backwards out of asteroid aabb/volume
	///	if ship gets stuck goto tunnel-through
	///	goto finished
	///	
	///		tunnel-through:
	///	enable drills
	///	fly forwards until outside of asteroid
	///	
	///		finished:
	///	disabled drills
	///	enable asteroid avoidance
	///	return control to navigator
	internal class HarvesterAsteroid
	{
		private const float FullAmount_Abort = 0.9f, FullAmount_Return = 0.5f;

		private readonly Navigator myNav;
		private readonly CubeGridCache myCache;
		public IMyCubeGrid myCubeGrid { get { return myNav.myGrid; } }
		public NavSettings CNS { get { return myNav.CNS; } }
		private Logger myLogger;

		private Action StageAction;

		private Vector3D harvestTowards;

		public HarvesterAsteroid(Navigator myNav)
		{
			this.myNav = myNav;
			this.myCache = CubeGridCache.GetFor(myCubeGrid);
			this.myLogger = new Logger("HarvesterAsteroid", () => myCubeGrid.DisplayName);
		}

		#region Control

		///<summary>Continue harvesting</summary>
		/// <returns>true iff harvesting actions were performed</returns>
		public bool Run()
		{
			if (StageAction == null)
				return false;
			StageAction();
			return true;
		}

		/// <summary>Start harvesting.</summary>
		public void Start()
		{ Ready(); }

		/// <remarks>
		/// <para>If drills are full, change stage to Off.</para>
		/// <para>Get closest asteroid, pick a random point.</para>
		/// <para>Disable Asteroid obstruction test.</para>
		/// <para>Set speed limits</para>
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
			harvestTowards = GetRandomPointInside(GetClosestAsteroid().WorldVolume);
			CNS.ignoreAsteroids = true;
			CNS.speedCruise_external = 1;
			CNS.speedSlow_external = 2;
			foreach (Ingame.IMyShipDrill drill in allDrills)
				drill.RequestEnable(true);

			StageAction = Harvest;
		}

		/// <remarks>
		/// <para>Fly towards harvestTowards.</para>
		/// <para>If ship becomes full goto backout</para>
		/// <para>If ship gets stuck goto backout</para>
		/// <para>If ship flies all the way through goto finished</para>
		/// </remarks>
		private void Harvest()
		{

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
			CNS.ignoreAsteroids = false;
			CNS.speedCruise_external = Settings.floatSettings[Settings.FloatSetName.fMaxSpeed];
			CNS.speedSlow_external = Settings.floatSettings[Settings.FloatSetName.fDefaultSpeed];
			StageAction = null;
		}

		#endregion

		private bool IsInsideAsteroid()
		{
			HashSet<IMyEntity> asteroids = new HashSet<IMyEntity>();
			MyAPIGateway.Entities.GetEntitiesInAABB_Safe_NoBlock(myCubeGrid.WorldAABB, asteroids, (entity) => { return entity is IMyVoxelMap && myCubeGrid.IntersectsAABBVolume(entity); });
			return asteroids.Count > 0;
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
			HashSet<IMyEntity> allAsteroids = new HashSet<IMyEntity>();
			MyAPIGateway.Entities.GetEntities_Safe(allAsteroids, (entity) => { return entity is IMyVoxelMap; });
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
	}
}
