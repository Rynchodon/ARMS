using System.Collections.Generic; // from mscorlib.dll, System.dll, System.Core.dll, and VRage.Library.dll
using Rynchodon.Settings;
using Sandbox.ModAPI; // from Sandbox.Common.dll
using VRage.Game.ModAPI; // from VRage.Game.dll
using VRage.ModAPI;

namespace Rynchodon.Autopilot.Harvest
{
	public class DamageHandler
	{

		private static DamageHandler Instance;

		static DamageHandler()
		{
			MyAPIGateway.Entities.OnCloseAll += Entities_OnCloseAll;
		}

		private static void Entities_OnCloseAll()
		{
			MyAPIGateway.Entities.OnCloseAll -= Entities_OnCloseAll;
			Instance = null;
		}

		public static void RegisterMiner(IMyCubeGrid miner, IMyVoxelBase voxel)
		{
			if (Instance == null || !ServerSettings.GetSetting<bool>(ServerSettings.SettingName.bImmortalMiner))
				return;
			Instance.miners.Add(miner, voxel);
		}

		public static bool UnregisterMiner(IMyCubeGrid miner)
		{
			if (Instance == null || !ServerSettings.GetSetting<bool>(ServerSettings.SettingName.bImmortalMiner))
				return false;
			return Instance.miners.Remove(miner);
		}

		private readonly Logger m_logger;
		private readonly Dictionary<IMyCubeGrid, IMyVoxelBase> miners = new Dictionary<IMyCubeGrid, IMyVoxelBase>();

		public DamageHandler()
		{
			m_logger = new Logger();
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
