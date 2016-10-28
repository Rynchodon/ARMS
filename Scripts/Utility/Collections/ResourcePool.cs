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

		public static T Get()
		{
			return Pool.Get();
		}

		public static void Return(T item)
		{
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

	}
}
