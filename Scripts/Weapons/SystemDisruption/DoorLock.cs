using System;

using Sandbox.Common.ObjectBuilders;
using Sandbox.ModAPI;
using VRage.ObjectBuilders;

namespace Rynchodon.Weapons.SystemDisruption
{
	public class DoorLock : Disruption
	{

		private static readonly MyObjectBuilderType[] s_affects = new MyObjectBuilderType[]
		{ typeof(MyObjectBuilder_Door), typeof(MyObjectBuilder_AirtightHangarDoor), typeof(MyObjectBuilder_AirtightSlideDoor) };

		public static void Update()
		{
			Registrar.ForEach((DoorLock dl) => dl.UpdateEffect());
		}

		public static int LockDoors(IMyCubeGrid grid, int strength, TimeSpan duration, long effectOwner)
		{
			DoorLock dl;
			if (!Registrar.TryGetValue(grid, out dl))
				dl = new DoorLock(grid);
			return dl.AddEffect(duration, strength, effectOwner);
		}

		private readonly Logger m_logger;

		private DoorLock(IMyCubeGrid grid)
			: base(grid, s_affects)
		{
			m_logger = new Logger(GetType().Name, () => grid.DisplayName);
			Registrar.Add(grid, this);
		}

		protected override int StartEffect(IMyCubeBlock block, int strength)
		{
			IMyDoor door = block as IMyDoor;
			m_logger.debugLog("Locking: " + block.DisplayNameText + ", remaining strength: " + (strength - 1), "StartEffect()");
			if (door.Open)
				door.ApplyAction("Open_Off");
			return 1;
		}

		protected override int EndEffect(IMyCubeBlock block, int strength)
		{
			m_logger.debugLog("Unlocking: " + block.DisplayNameText + ", remaining strength: " + (strength - 1), "EndEffect()");
			(block as IMyFunctionalBlock).RequestEnable(true);
			return 1;
		}

	}
}
