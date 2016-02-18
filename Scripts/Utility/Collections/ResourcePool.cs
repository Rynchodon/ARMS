
using Sandbox.ModAPI;
using VRage.Collections;

namespace Rynchodon
{
	/// <summary>
	/// Static wrapper for MyConcurrentPool
	/// </summary>
	public static class ResourcePool<T> where T : new()
	{

		public static MyConcurrentPool<T> Pool { get; private set; }

		static ResourcePool()
		{
			Pool = new MyConcurrentPool<T>();
			MyAPIGateway.Entities.OnCloseAll += Entities_OnCloseAll;
		}

		private static void Entities_OnCloseAll()
		{
			MyAPIGateway.Entities.OnCloseAll -= Entities_OnCloseAll;
			Pool = null;
		}

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
