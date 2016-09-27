using Sandbox.Game.World;
using Sandbox.ModAPI;
using VRage;
using VRageMath;

namespace Rynchodon
{
	public class SunProperties
	{

		private static SunProperties Instance;

		private Vector3 mySunDirection;
		private readonly FastResourceLock lock_mySunDirection = new FastResourceLock();

		static SunProperties()
		{
			MyAPIGateway.Entities.OnCloseAll += Entities_OnCloseAll;
		}

		private static void Entities_OnCloseAll()
		{
			MyAPIGateway.Entities.OnCloseAll -= Entities_OnCloseAll;
			Instance = null;
		}

		public SunProperties()
		{
			Instance = this;
		}

		public void Update10()
		{
			using (Instance.lock_mySunDirection.AcquireExclusiveUsing())
				mySunDirection = MySector.DirectionToSunNormalized;
		}

		public static Vector3 SunDirection
		{
			get
			{
				using (Instance.lock_mySunDirection.AcquireSharedUsing())
					return Instance.mySunDirection;
			}
		}

	}
}
