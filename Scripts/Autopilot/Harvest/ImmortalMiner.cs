using System.Collections.Generic; // from mscorlib.dll, System.dll, System.Core.dll, and VRage.Library.dll
using Rynchodon.Settings;
using Sandbox.ModAPI; // from Sandbox.Common.dll
using VRage.Game.ModAPI; // from VRage.Game.dll
using VRage.ModAPI;

namespace Rynchodon.Autopilot.Harvest
{
	public class ImmortalMiner
	{

		private static ImmortalMiner Instance;

		public static void RegisterMiner(IMyCubeGrid miner, IMyVoxelBase voxel)
		{
			if (Instance != null)
				Instance.miners.Add(miner, voxel);
		}

		public static bool UnregisterMiner(IMyCubeGrid miner)
		{
			if (Instance != null)
				return Instance.miners.Remove(miner);
			return true;
		}

		[OnWorldLoad]
		private static void Load()
		{
			if (ServerSettings.GetSetting<bool>(ServerSettings.SettingName.bImmortalMiner))
				Instance = new ImmortalMiner();
		}

		[OnWorldClose]
		private static void Unload()
		{
			Instance = null;
		}

		private readonly Dictionary<IMyCubeGrid, IMyVoxelBase> miners = new Dictionary<IMyCubeGrid, IMyVoxelBase>();

		public ImmortalMiner()
		{
			MyAPIGateway.Session.DamageSystem.RegisterBeforeDamageHandler((int)MyDamageSystemPriority.Low, BeforeDamageApplied);
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
