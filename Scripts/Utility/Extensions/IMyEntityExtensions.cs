using System;
using System.Collections.Generic;
using Sandbox.Game.Entities;
using Sandbox.Game.Weapons;
using VRage.Game.Entity;
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
					return asGrid.SimpleName();
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

			if (entity.IsMissile())
				return "Missile";

			return entity.getBestName();
		}

		/// <summary>
		/// Current best hack for determining if an entity is a missile.
		/// </summary>
		public static bool IsMissile(this IMyEntity entity)
		{
			// only MyMissile derives from MyAmmoBase
			return entity is MyAmmoBase;
		}

		/// <summary>
		/// Enumerable for all the subparts in an entity's hierachy.
		/// </summary>
		/// <returns>Parent, name, subpart</returns>
		public static IEnumerable<Tuple<MyEntity, string, MyEntitySubpart>> ExamineSubParts(this MyEntity entity)
		{
			if (entity.Subparts == null || entity.Subparts.Count == 0)
				yield break;
			foreach (KeyValuePair<string, MyEntitySubpart> pair in entity.Subparts)
			{
				yield return new Tuple<MyEntity, string, MyEntitySubpart>(entity, pair.Key, pair.Value);
				foreach (Tuple<MyEntity, string, MyEntitySubpart> tuple in ExamineSubParts(pair.Value))
					yield return tuple;
			}
		}

	}
}
