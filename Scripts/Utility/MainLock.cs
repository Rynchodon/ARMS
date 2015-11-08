using System;
using System.Collections.Generic;
using Rynchodon.Threading;
using Sandbox.Common.ObjectBuilders;
using Sandbox.ModAPI;
using VRage.ModAPI;
using VRageMath;
using Ingame = Sandbox.ModAPI.Ingame;

namespace Rynchodon
{
	/// <summary>
	/// Obtains a shared lock on core thread while performing an action.
	/// </summary>
	public static class MainLock
	{

		private static Logger myLogger = new Logger("MainLock");
		private static FastResourceLock Lock_MainThread = new FastResourceLock("Lock_MainThread");
		private static FastResourceLock lock_RayCast = new FastResourceLock();

		static MainLock()
		{
			MainThread_AcquireExclusive();
			MyAPIGateway.Entities.OnCloseAll += Entities_OnCloseAll;
		}

		static void Entities_OnCloseAll()
		{
			MyAPIGateway.Entities.OnCloseAll -= Entities_OnCloseAll;
			myLogger = null;
			MainThread_ReleaseExclusive();
			Lock_MainThread = null;
			lock_RayCast = null;
		}

		/// <summary>
		/// This should only ever be called from main thread.
		/// </summary>
		///// <returns>true if the exclusive lock was acquired, false if it is already held</returns>
		public static void MainThread_AcquireExclusive()
		{
			Lock_MainThread.AcquireExclusive();
		}

		/// <summary>
		/// This should only ever be called from main thread.
		/// </summary>
		public static void MainThread_ReleaseExclusive()
		{
			Lock_MainThread.ReleaseExclusive();
		}

		/// <summary>
		/// perform an Action while using a shared lock on main thread.
		/// </summary>
		/// <param name="safeAction">Action to perform</param>
		public static void UsingShared(Action unsafeAction)
		{
			if (ThreadTracker.IsGameThread)
				unsafeAction.Invoke();
			else
				using (Lock_MainThread.AcquireSharedUsing())
					unsafeAction.Invoke();
		}

		public static void GetBlocks_Safe(this IMyCubeGrid grid, List<IMySlimBlock> blocks, Func<IMySlimBlock, bool> collect = null)
		{
			UsingShared(() => grid.GetBlocks(blocks, collect));
		}

		#region IMyEntities

		public static void GetEntities_Safe(this IMyEntities entitiesObject, HashSet<IMyEntity> entities, Func<IMyEntity, bool> collect = null)
		{
			UsingShared(() => entitiesObject.GetEntities(entities, collect));
		}

		#endregion

		public static MyObjectBuilder_CubeBlock GetObjectBuilder_Safe(this IMySlimBlock block)
		{
			MyObjectBuilder_CubeBlock result = null;
			UsingShared(() => result = block.GetObjectBuilder());
			return result;
		}

		public static MyObjectBuilder_CubeBlock GetObjectBuilder_Safe(this IMyCubeBlock block)
		{
			MyObjectBuilder_CubeBlock result = null;
			UsingShared(() => result = block.GetObjectBuilderCubeBlock());
			return result;
		}

		public static MyObjectBuilder_CubeBlock GetObjectBuilder_Safe(this Ingame.IMyCubeBlock block)
		{
			return (block as IMyCubeBlock).GetObjectBuilder_Safe();
		}

		public static List<IMyVoxelBase> GetInstances_Safe(this IMyVoxelMaps mapsObject, Func<IMyVoxelBase, bool> collect = null)
		{
			List<IMyVoxelBase> outInstances = new List<IMyVoxelBase>();
			UsingShared(() => mapsObject.GetInstances(outInstances, collect));
			return outInstances;
		}

		/// <summary>
		/// Now I'm getting memory access violations...
		/// </summary>
		/// <remarks>
		/// Only one ray cast can be performed at a time.
		/// </remarks>
		public static bool RayCastVoxel_Safe(this IMyEntities entities, Vector3 from, Vector3 to, out Vector3 boundary)
		{
			Vector3 in_boundary = Vector3.Zero;
			using (lock_RayCast.AcquireExclusiveUsing())
				UsingShared(() => entities.IsInsideVoxel(from, to, out in_boundary));
			boundary = in_boundary;
			return (boundary != from);
		}

		public static IMyIdentity GetIdentity_Safe(this IMyCharacter character)
		{
			string DisplayName = (character as IMyEntity).DisplayName;
			List<IMyIdentity> match = new List<IMyIdentity>();
			UsingShared(() => MyAPIGateway.Players.GetAllIdentites(match, (id) => { return id.DisplayName == DisplayName; }));
			if (match.Count == 1)
				return match[0];
			return null;
		}

		public static IMyPlayer GetPlayer_Safe(this IMyIdentity identity)
		{
			List<IMyPlayer> match = MyAPIGateway.Players.GetPlayers_Safe((player) => { return player.PlayerID == identity.PlayerId; });
			if (match.Count == 1)
				return match[0];
			return null;
		}

		public static IMyPlayer GetPlayer_Safe(this IMyCharacter character)
		{
			string DisplayName = (character as IMyEntity).DisplayName;
			List<IMyPlayer> match = MyAPIGateway.Players.GetPlayers_Safe((player) => { return player.DisplayName == DisplayName; });
			if (match.Count == 1)
				return match[0];
			return null;
		}

		public static List<IMyPlayer> GetPlayers_Safe(this IMyPlayerCollection PlayColl, Func<IMyPlayer, bool> collect = null)
		{
			List<IMyPlayer> players = new List<IMyPlayer>();
			UsingShared(() => PlayColl.GetPlayers(players, collect));
			return players;
		}

		private static void LogLockStats(string source)
		{ myLogger.debugLog("Lock Stats: Owned = " + Lock_MainThread.Owned + ", Shared Owners = " + Lock_MainThread.SharedOwners + ", Exclusive Waiters = " + Lock_MainThread.ExclusiveWaiters + ", Shared Waiters = " + Lock_MainThread.SharedWaiters, source); }
	}
}
