using System;

namespace Rynchodon.Autopilot.Harvest
{
	/// <summary>
	/// Class shall be responsible for controlling the ship for the purposes of harvesting from an asteroid.
	/// </summary>
	/// <remarks>
	/// <para>Steps:</para>
	/// <para>Find a target point (currently random)</para>
	/// <para>Enable Drills</para>
	/// <para>Fly Towards target, drills forward</para>
	/// <para> </para>
	/// <para>Abort Conditions (OR):</para>
	/// <para>Drilled Past Asteroid - fly out</para>
	/// <para>Drilled Past Target - reverse</para>
	/// <para>Drills are Full - reverse</para>
	/// <para>Stuck While Reversing - drill forward to get out</para>
	/// <para> </para>
	/// <para>Special Collison Aboidance:</para>
	/// <para>Ignore Asteroids</para>
	/// <para>Cannot fly around grids, so there will need to be an alternate solution.</para>
	/// </remarks>
	public class HarvestAsteroid
	{
		
	}
}
