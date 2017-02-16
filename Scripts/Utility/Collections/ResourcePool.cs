using System.Collections;
using VRage.Collections;

namespace Rynchodon
{
	public static class ResourcePool
	{
		public static void Get<T>(out T item) where T : new()
		{
			item = ResourcePool<T>.Get();
		}

		public static void Return<T>(T item) where T : new()
		{
			ResourcePool<T>.Return(item);
		}
	}

	/// <summary>
	/// Static wrapper for MyConcurrentPool
	/// </summary>
	public static class ResourcePool<T> where T : new()
	{

		private static MyConcurrentPool<T> Pool = new MyConcurrentPool<T>();
#if DEBUG
		private static int lastComplain = 0;
#endif

		public static T Get()
		{
			CheckInstancesCreated();
			return Pool.Get();
		}

		public static void Return(T item)
		{
			CheckInstancesCreated();
			CheckCleared(item);
			Pool.Return(item);
		}

		public static int Count
		{
			get { return Pool.Count; }
		}

		public static int InstancesCreated
		{
			get { return Pool.InstancesCreated; }
		}

		[System.Diagnostics.Conditional("DEBUG")]
		private static void CheckInstancesCreated()
		{
#if DEBUG
			if (Pool.InstancesCreated != lastComplain && Pool.InstancesCreated % 100 == 0)
			{
				lastComplain = Pool.InstancesCreated;
				Logger.DebugLog(typeof(ResourcePool).Name + "<" + typeof(T).Name + "> generated " + Pool.InstancesCreated + " instances.", Logger.severity.WARNING);
				Logger.DebugLogCallStack();
			}
#endif
		}

		[System.Diagnostics.Conditional("DEBUG")]
		private static void CheckCleared(T item)
		{
			ICollection collection = item as ICollection;
			if (collection != null && collection.Count != 0)
			{
				Logger.DebugLog("Returning collection with items, collection: " + typeof(T).Name + ", count: " + collection.Count);
				Logger.DebugLogCallStack();
			}
		}

	}
}
