using VRage;

namespace Rynchodon
{
	public static class FastResourceLockExtensions
	{

		public static string GetStatus(this FastResourceLock fastLock)
		{
			return "Owned=" + fastLock.Owned + ", SharedOwners=" + fastLock.SharedOwners + ", ExclusiveWaiters=" + fastLock.ExclusiveWaiters + ", SharedWaiters=" + fastLock.SharedWaiters;
		}

	}
}
