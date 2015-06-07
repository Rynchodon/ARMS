using System;
using System.Collections.Generic;
using Sandbox.Common.ObjectBuilders;
using Sandbox.ModAPI;
using VRage;
using VRage.ModAPI;
using VRageMath;

namespace Rynchodon
{
	/// <summary>
	/// Obtains a shared lock on core thread while performing an action. Locks can only be obtained while main thread is running.
	/// </summary>
	public static class MainLock
	{
		private static FastResourceLock Lock_MainThread = new FastResourceLock();
		private static bool ExclusiveHeld = false;

		/// <summary>
		/// This should only ever be called from main thread.
		/// </summary>
		/// <returns>true if the exclusive lock was acquired, false if it is already held</returns>
		public static bool MainThread_TryAcquireExclusive()
		{
			if (ExclusiveHeld)
				return false;

			Lock_MainThread.AcquireExclusive();
			ExclusiveHeld = true;
			return true;
		}

		/// <summary>
		/// This should only ever be called from main thread.
		/// </summary>
		/// <returns>true if exclusive lock was released, false if it is not held</returns>
		public static bool MainThread_TryReleaseExclusive()
		{
			if (!ExclusiveHeld)
				return false;

			Lock_MainThread.ReleaseExclusive();
			ExclusiveHeld = false;
			return true;
		}

		/// <summary>
		/// perform an Action while using a shared lock on main thread.
		/// </summary>
		/// <param name="safeAction">Action to perform</param>
		public static void UsingShared(Action safeAction)
		{
			Lock_MainThread.AcquireShared();
			try
			{ safeAction.Invoke(); }
			finally
			{ Lock_MainThread.ReleaseShared(); }
		}

		public static void GetBlocks_Safe(this IMyCubeGrid grid, List<IMySlimBlock> blocks, Func<IMySlimBlock, bool> collect = null)
		{
			Lock_MainThread.AcquireShared();
			try
			{ grid.GetBlocks(blocks, collect); }
			finally
			{ Lock_MainThread.ReleaseShared(); }
		}

		#region IMyEntities

		public static void GetEntities_Safe(this IMyEntities entitiesObject, HashSet<IMyEntity> entities, Func<IMyEntity, bool> collect = null)
		{
			Lock_MainThread.AcquireShared();
			try
			{ entitiesObject.GetEntities(entities, collect); }
			finally
			{ Lock_MainThread.ReleaseShared(); }
		}

		/// <summary>
		/// <para>Uses IMyEntities.GetEntities to get entities in AABB</para>
		/// <para>Always use over GetEntitiesInAABB_Safe() when blocks are not needed, much faster.</para>
		/// </summary>
		/// <param name="preCollect">applied before intersection test</param>
		public static void GetEntitiesInAABB_Safe_NoBlock(this IMyEntities entitiesObject, BoundingBoxD boundingBox, HashSet<IMyEntity> entities, Func<IMyEntity, bool> preCollect = null)
		{
			Func<IMyEntity, bool> collector;
			if (preCollect == null)
				collector = (entity) => boundingBox.Intersects(entity.WorldAABB);
			else
				collector = (entity) => { return preCollect(entity) && boundingBox.Intersects(entity.WorldAABB); };
			entitiesObject.GetEntities_Safe(entities, collector);
		}

		/// <summary>
		/// <para>Uses IMyEntities.GetEntities to get entities in Sphere</para>
		/// <para>Always use over GetEntitiesInSphere_Safe() when blocks are not needed, much faster.</para>
		/// </summary>
		/// <param name="preCollect">applied before intersection test</param>
		public static void GetEntitiesInSphere_Safe_NoBlock(this IMyEntities entitiesObject, BoundingSphereD boundingSphere, HashSet<IMyEntity> entities, Func<IMyEntity, bool> preCollect = null)
		{
			Func<IMyEntity, bool> collector;
			if (preCollect == null)
				collector = (entity) => boundingSphere.Intersects(entity.WorldAABB);
			else
				collector = (entity) => { return preCollect(entity) && boundingSphere.Intersects(entity.WorldAABB); };
			entitiesObject.GetEntities_Safe(entities, collector);
		}

		/// <summary>Consider using GetEntitiesInAABB_Safe_NoBlock instead.</summary>
		public static List<IMyEntity> GetEntitiesInAABB_Safe(this IMyEntities entitiesObject, ref BoundingBoxD boundingBox)
		{
			Lock_MainThread.AcquireShared();
			try
			{ return entitiesObject.GetEntitiesInAABB(ref boundingBox); }
			finally
			{ Lock_MainThread.ReleaseShared(); }
		}

		/// <summary>Consider using GetEntitiesInSphere_Safe_NoBlock instead.</summary>
		public static List<IMyEntity> GetEntitiesInSphere_Safe(this IMyEntities entitiesObject, ref BoundingSphereD boundingSphere)
		{
			Lock_MainThread.AcquireShared();
			try
			{ return entitiesObject.GetEntitiesInSphere(ref boundingSphere); }
			finally
			{ Lock_MainThread.ReleaseShared(); }
		}

		#endregion

		public static MyObjectBuilder_CubeBlock GetObjectBuilder_Safe(this IMySlimBlock block)
		{
			Lock_MainThread.AcquireShared();
			try
			{ return block.GetObjectBuilder(); }
			finally
			{ Lock_MainThread.ReleaseShared(); }
		}

		public static MyObjectBuilder_CubeBlock GetSlimObjectBuilder_Safe(this IMyCubeBlock block)
		{
			Lock_MainThread.AcquireShared();
			try
			{
				IMySlimBlock slim = block.CubeGrid.GetCubeBlock(block.Position);
				return slim.GetObjectBuilder();
			}
			finally
			{ Lock_MainThread.ReleaseShared(); }
		}

		public static IMyPlayer GetPlayer_Safe(this IMyCharacter character)
		{
			List<IMyPlayer> matchingPlayer = new List<IMyPlayer>();
			Lock_MainThread.AcquireShared();
			try
			{ MyAPIGateway.Players.GetPlayers(matchingPlayer, player => { return player.IdentityId == (character as IMyEntity).EntityId; }); }
			finally
			{ Lock_MainThread.ReleaseShared(); }

			switch (matchingPlayer.Count)
			{
				case 0:
					return null;
				case 1:
					return matchingPlayer[0];
				default:
					VRage.Exceptions.ThrowIf<InvalidOperationException>(true, "too many matching players (" + matchingPlayer.Count + ")");
					return null;
			}
		}

		public static List<IMyVoxelBase> GetInstances_Safe(this IMyVoxelMaps mapsObject, Func<IMyVoxelBase, bool> collect = null)
		{
			List<IMyVoxelBase> outInstances = new List<IMyVoxelBase>();
			Lock_MainThread.AcquireShared();
			try
			{ mapsObject.GetInstances(outInstances, collect); }
			finally
			{ Lock_MainThread.ReleaseShared(); }
			return outInstances;
		}

		/// <remarks>I have not tested IsInsideVoxel for thread-safety, I assumed it is not.</remarks>
		public static bool RayCastVoxel(this IMyEntities entities, Vector3 from, Vector3 to, out Vector3 boundary)
		{
			Lock_MainThread.AcquireShared();
			try
			{
				entities.IsInsideVoxel(from, to, out boundary);
				return (boundary != from);
			}
			finally
			{ Lock_MainThread.ReleaseShared(); }
		}
	}
}
