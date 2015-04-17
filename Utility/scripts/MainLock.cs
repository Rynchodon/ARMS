using System;
using System.Collections.Generic;
//using System.Linq;
//using System.Text;

using Sandbox.Common.ObjectBuilders;
using Sandbox.ModAPI;
using VRage;
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

		public static void GetBlocks_Safe(this IMyCubeGrid grid, List<IMySlimBlock> blocks, Func<IMySlimBlock, bool> collect = null)
		{
			using (Lock_MainThread.AcquireSharedUsing())
				grid.GetBlocks(blocks, collect);
		}

		public static void GetEntities_Safe(this IMyEntities entitiesObject, HashSet<IMyEntity> entities, Func<IMyEntity, bool> collect = null)
		{
			using (Lock_MainThread.AcquireSharedUsing())
				entitiesObject.GetEntities(entities, collect);
		}

		public static List<IMyEntity> GetEntitiesInAABB_Safe(this IMyEntities entitiesObject, ref BoundingBoxD boundingBox)
		{
			using (Lock_MainThread.AcquireSharedUsing())
				return entitiesObject.GetEntitiesInAABB(ref boundingBox);
		}

		public static List<IMyEntity> GetEntitiesInSphere_Safe(this IMyEntities entitiesObject, ref BoundingSphereD boundingSphere)
		{
			using (Lock_MainThread.AcquireSharedUsing())
				return entitiesObject.GetEntitiesInSphere(ref boundingSphere);
		}

		public static MyObjectBuilder_CubeBlock GetObjectBuilder_Safe(this IMySlimBlock block)
		{
			using (Lock_MainThread.AcquireSharedUsing())
				return block.GetObjectBuilder();
		}

		public static MyObjectBuilder_CubeBlock GetSlimObjectBuilder_Safe(this IMyCubeBlock block)
		{
			using (Lock_MainThread.AcquireSharedUsing())
			{
				IMySlimBlock slim = block.CubeGrid.GetCubeBlock(block.Position);
				return slim.GetObjectBuilder();
			}
		}

		public static IMyPlayer GetPlayer_Safe(this IMyCharacter character)
		{
			List<IMyPlayer> matchingPlayer = new List<IMyPlayer>();
			using (Lock_MainThread.AcquireSharedUsing())
				MyAPIGateway.Players.GetPlayers(matchingPlayer, player => { return player.IdentityId == (character as IMyEntity).EntityId; });

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

	}
}
