using System;
using System.Collections.Generic;
using Rynchodon.Threading;
using Sandbox.ModAPI;
using VRage.Game;
using VRage.Game.ModAPI;
using VRage.ModAPI;

namespace Rynchodon
{
	/// <summary>
	/// Obtains a shared lock on core thread while performing an action.
	/// </summary>
	public static class MainLock
	{

		private class DummyDisposable : IDisposable
		{
			public void Dispose() { }
		}

		private static FastResourceLock Lock_MainThread = new FastResourceLock();
		/// <summary>Dummy lock, exclusive is never held</summary>
		private static DummyDisposable lock_dummy = new DummyDisposable();

		/// <summary>
		/// This should only ever be called from main thread.
		/// </summary>
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

#if PROFILE
		/// <summary>
		/// perform an Action while using a shared lock on main thread.
		/// </summary>
		/// <param name="safeAction">Action to perform</param>
		public static void UsingShared(Action unsafeAction, [CallerFilePath] string callerFilePath = null, [CallerMemberName] string callerMemberName = null)
		{
			using (AcquireSharedUsing(callerFilePath, callerMemberName))
				unsafeAction.Invoke();
		}

		/// <summary>
		/// Acquire shared using lock on main thread.
		/// </summary>
		/// <returns>Shared using lock on main thread.</returns>
		public static IDisposable AcquireSharedUsing([CallerFilePath] string callerFilePath = null, [CallerMemberName] string callerMemberName = null)
		{
			if (ThreadTracker.IsGameThread)
				return lock_dummy;

			Profiler.StartProfileBlock("Waiting for shared lock. File: " + Path.GetFileName(callerFilePath) + " Member: " + callerMemberName);
			IDisposable result = Lock_MainThread.AcquireSharedUsing();
			Profiler.EndProfileBlock();
			return result;
		}
#else
		/// <summary>
		/// perform an Action while using a shared lock on main thread.
		/// </summary>
		/// <param name="safeAction">Action to perform</param>
		public static void UsingShared(Action unsafeAction)
		{
			using (AcquireSharedUsing())
				unsafeAction.Invoke();
		}

		/// <summary>
		/// Acquire shared using lock on main thread.
		/// </summary>
		/// <returns>Shared using lock on main thread.</returns>
		public static IDisposable AcquireSharedUsing()
		{
			if (ThreadTracker.IsGameThread)
				return lock_dummy;

			IDisposable result = Lock_MainThread.AcquireSharedUsing();
			return result;
		}
#endif

		public static void GetBlocks_Safe(this IMyCubeGrid grid, List<IMySlimBlock> blocks, Func<IMySlimBlock, bool> collect = null)
		{
			UsingShared(() => grid.GetBlocks(blocks, collect));
		}

		public static MyObjectBuilder_CubeBlock GetObjectBuilder_Safe(this IMyCubeBlock block)
		{
			MyObjectBuilder_CubeBlock result = null;
			UsingShared(() => result = block.GetObjectBuilderCubeBlock());
			return result;
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

	}
}
