using Sandbox.Game.Entities;
using VRage.Game.ModAPI;
using VRage.ModAPI;

namespace Rynchodon
{
	public static class IMyEntityExtensions
	{

		/// <summary>
		/// Gets a name for an entity that can be shown to players. Assumes the entity has been detected in a fair way.
		/// </summary>
		/// <param name="entity">The entity to get the name for.</param>
		/// <param name="playerId">Who wants to know?</param>
		/// <returns>A name that can be shown to players.</returns>
		public static string GetNameForDisplay(this IMyEntity entity, long playerId)
		{
			IMyCubeGrid asGrid = entity as IMyCubeGrid;
			if (asGrid != null)
			{
				if (playerId.canConsiderFriendly(asGrid))
					return asGrid.DisplayName;
				else
					return "Unknown Grid";
			}

			IMyCubeBlock asBlock = entity as IMyCubeBlock;
			if (asBlock != null)
			{
				if (playerId.canConsiderFriendly(asBlock.OwnerId))
					return asBlock.DisplayNameText + " on " + GetNameForDisplay(asBlock.CubeGrid, playerId);
				else
					return asBlock.DefinitionDisplayNameText + " on " + GetNameForDisplay(asBlock.CubeGrid, playerId);
			}

			IMyCharacter asChar = entity as IMyCharacter;
			if (asChar != null)
			{
				if (string.IsNullOrEmpty(entity.DisplayName))
					return "Creature";
				return entity.DisplayName;
			}

			if (entity is IMyVoxelMap)
				return "Asteroid";

			if (entity is MyPlanet)
				return "Planet";

			if (entity.ToString().StartsWith("MyMissile"))
				return "Missile";

			return entity.getBestName();
		}

	}
}
