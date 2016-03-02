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

		private DoorLock(IMyCubeGrid grid)
			: base(grid, s_affects)
		{
			Registrar.Add(grid, this);
		}

		protected override void StartEffect(IMyCubeBlock block)
		{
			IMyDoor door = block as IMyDoor;
			if (door.Open)
				door.ApplyAction("Open_Off");
		}

	}
}
