using Sandbox.Game.Entities;
using VRageMath;

namespace Rynchodon
{
	public static class MyPlanetExtensions
	{

		public static MyPlanet GetClosestPlanet(Vector3D position)
		{
			double distSquared;
			return GetClosestPlanet(position, out distSquared);
		}

		public static MyPlanet GetClosestPlanet(Vector3D position, out double distSquared)
		{
			MyPlanet closest = null;
			distSquared = double.MaxValue;
			foreach (MyPlanet planet in Globals.AllPlanets())
			{
				double distance = Vector3D.DistanceSquared(position, planet.GetCentre());
				if (distance < distSquared)
				{
					distSquared = distance;
					closest = planet;
				}
			}

			return closest;
		}

		public static bool IsPositionInGravityWell(this MyPlanet planet, ref Vector3D worldPosition)
		{
			return planet.Components.Get<MyGravityProviderComponent>().IsPositionInRange(worldPosition);
		}

		public static bool IsPositionInGravityWell(this MyPlanet planet, Vector3D worldPosition)
		{
			return planet.Components.Get<MyGravityProviderComponent>().IsPositionInRange(worldPosition);
		}

		public static Vector3D GetWorldGravityNormalized(this MyPlanet planet, ref Vector3D worldPosition)
		{
			return Vector3D.Normalize(planet.WorldMatrix.Translation - worldPosition);
		}

		public static Vector3D GetWorldGravityNormalized(this MyPlanet planet, Vector3D worldPosition)
		{
			return Vector3D.Normalize(planet.WorldMatrix.Translation - worldPosition);
		}

		public static float GetGravityMultiplier(this MyPlanet planet, ref Vector3D worldPosition)
		{
			return planet.Components.Get<MyGravityProviderComponent>().GetGravityMultiplier(worldPosition);
		}

		public static float GetGravityMultiplier(this MyPlanet planet, Vector3D worldPosition)
		{
			return planet.Components.Get<MyGravityProviderComponent>().GetGravityMultiplier(worldPosition);
		}

		public static Vector3D GetWorldGravity(this MyPlanet planet, ref Vector3D worldPosition)
		{
			return planet.Components.Get<MyGravityProviderComponent>().GetWorldGravity(worldPosition);
		}

		public static Vector3D GetWorldGravity(this MyPlanet planet, Vector3D worldPosition)
		{
			return planet.Components.Get<MyGravityProviderComponent>().GetWorldGravity(worldPosition);
		}

		public static float GetGravityLimit(this MyPlanet planet)
		{
			return ((MySphericalNaturalGravityComponent)planet.Components.Get<MyGravityProviderComponent>()).GravityLimit;
		}

	}
}
