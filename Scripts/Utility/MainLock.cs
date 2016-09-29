using System;
using System.Collections.Generic;
using Rynchodon.Threading;
using Sandbox.ModAPI;
using VRage;
using VRage.Game;
using VRage.Game.ModAPI;
using VRage.ModAPI;
using Ingame = VRage.Game.ModAPI.Ingame;

namespace Rynchodon
{
	/// <summary>
	/// Obtains a shared lock on core thread while performing an action.
	/// </summary>
	public static class MainLock
	{

		private static Logger myLogger = new Logger();
		private static FastResourceLock Lock_MainThread = new FastResourceLock();
		/// <summary>Dummy lock, exclusive is never held</summary>
		private static FastResourceLock lock_dummy = new FastResourceLock();

		static MainLock()
		{
			MainThread_AcquireExclusive();
			MyAPIGateway.Entities.OnCloseAll += Entities_OnCloseAll;
		}

		static void Entities_OnCloseAll()
		{
			MyAPIGateway.Entities.OnCloseAll -= Entities_OnCloseAll;
			Globals.WorldClosed = true;
			myLogger = null;
			MainThread_ReleaseExclusive();
			Lock_MainThread = null;
			lock_dummy = null;
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
			if (Lock_MainThread == null)
				return;
			if (ThreadTracker.IsGameThread)
				unsafeAction.Invoke();
			else
				using (Lock_MainThread.AcquireSharedUsing())
					unsafeAction.Invoke();
		}

		/// <summary>
		/// Acquire shared using lock on main thread.
		/// </summary>
		/// <returns>Shared using lock on main thread.</returns>
		public static IDisposable AcquireSharedUsing()
		{
			if (ThreadTracker.IsGameThread)
				return lock_dummy.AcquireSharedUsing();
			return Lock_MainThread.AcquireSharedUsing();
		}

		public static void GetBlocks_Safe(this IMyCubeGrid grid, List<IMySlimBlock> blocks, Func<IMySlimBlock, bool> collect = null)
		{
			UsingShared(() => grid.GetBlocks(blocks, collect));
		}

		#region IMyEntities

		[Obsolete("Use MyGamePruningStructure")]
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

		public static void GetInstances_Safe(this IMyVoxelMaps mapsObject, List<IMyVoxelBase> list, Func<IMyVoxelBase, bool> collect = null)
		{
			UsingShared(() => mapsObject.GetInstances(list, collect));
		}

		public static IMyIdentity GetIdentity_Safe(this IMyCharacter character)
		{
			string DisplayName = (character as IMyEntity).DisplayName;
			IMyIdentity living = null;
			IMyIdentity dead = null;
			UsingShared(() => {
				MyAPIGateway.Players.GetAllIdentites(null, id => {
					if (living == null && id.DisplayName == DisplayName)
					{
						if (id.IsDead)
							dead = id;
						else
							living = id;
					}
					return false;
				});
			});
			return living ?? dead;
		}

		public static IMyIdentity GetIdentity_Safe(this IMyPlayer player)
		{
			IMyIdentity living = null;
			IMyIdentity dead = null;
			UsingShared(() => {
				MyAPIGateway.Players.GetAllIdentites(null, id => {
					if (living == null && id.IdentityId == player.IdentityId)
					{
						if (id.IsDead)
							dead = id;
						else
							living = id;
					}
					return false;
				});
			});
			return living ?? dead;
		}

		public static IMyPlayer GetPlayer_Safe(this IMyIdentity identity)
		{
			IMyPlayer match = null;
			UsingShared(() => {
				MyAPIGateway.Players.GetPlayers(null, player => {
					if (match == null && player.IdentityId == identity.IdentityId)
						match = player;
					return false;
				});
			});
			return match;
		}

		public static IMyPlayer GetPlayer_Safe(this IMyCharacter character)
		{
			string DisplayName = (character as IMyEntity).DisplayName;
			IMyPlayer match = null;
			UsingShared(() => {
				MyAPIGateway.Players.GetPlayers(null, player => {
					if (match == null && player.DisplayName == DisplayName)
						match = player;
					return false;
				});
			});
			return match;
		}

		public static void GetPlayers_Safe(this IMyPlayerCollection PlayColl, List<IMyPlayer> players, Func<IMyPlayer, bool> collect = null)
		{
			UsingShared(() => PlayColl.GetPlayers(players, collect));
		}

		public static IMyPlayer GetFirstPlayer_Safe(this IMyPlayerCollection playColl, Func<IMyPlayer, bool> match)
		{
			IMyPlayer result = null;
			UsingShared(() => playColl.GetPlayers(null, player => {
				if (result == null && match(player))
					result = player;
				return false;
			}));
			return result;
		}

		private static void LogLockStats(string source)
		{ myLogger.debugLog("Lock Stats: Owned = " + Lock_MainThread.Owned + ", Shared Owners = " + Lock_MainThread.SharedOwners + ", Exclusive Waiters = " + Lock_MainThread.ExclusiveWaiters + ", Shared Waiters = " + Lock_MainThread.SharedWaiters); }
	}
}
