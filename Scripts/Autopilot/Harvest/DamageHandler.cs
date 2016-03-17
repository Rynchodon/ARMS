using System.Collections.Generic; // from mscorlib.dll, System.dll, System.Core.dll, and VRage.Library.dll
using Sandbox.ModAPI; // from Sandbox.Common.dll
using VRage.Game.ModAPI; // from VRage.Game.dll
using VRage.ModAPI;

namespace Rynchodon.Autopilot.Harvest
{
	public class DamageHandler
	{

		private static DamageHandler Instance;

		public static void RegisterMiner(IMyCubeGrid miner, IMyVoxelBase voxel)
		{
			Instance.miners.Add(miner, voxel);
		}

		public static bool UnregisterMiner(IMyCubeGrid miner)
		{
			return Instance.miners.Remove(miner);
		}

		private readonly Logger m_logger;
		private readonly Dictionary<IMyCubeGrid, IMyVoxelBase> miners = new Dictionary<IMyCubeGrid, IMyVoxelBase>();

		public DamageHandler()
		{
			m_logger = new Logger(GetType().Name);
			MyAPIGateway.Session.DamageSystem.RegisterBeforeDamageHandler((int)MyDamageSystemPriority.Low, BeforeDamageApplied);
			Instance = this;
		}

		private void BeforeDamageApplied(object target, ref MyDamageInformation info)
		{
			IMySlimBlock slim = target as IMySlimBlock;
			if (slim == null)
				return;

			IMyVoxelBase voxel;
			if (!miners.TryGetValue(slim.CubeGrid, out voxel))
				return;

			if (voxel.EntityId == info.AttackerId)
				info.Amount = 0f;
		}

	}
}
