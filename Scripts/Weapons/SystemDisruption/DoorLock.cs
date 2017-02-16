
using Sandbox.Common.ObjectBuilders;
using Sandbox.ModAPI;
using VRage.Game.ModAPI;
using VRage.ObjectBuilders;

namespace Rynchodon.Weapons.SystemDisruption
{
	public class DoorLock : Disruption
	{

		protected override MyObjectBuilderType[] BlocksAffected
		{
			get { return new MyObjectBuilderType[] { typeof(MyObjectBuilder_Door), typeof(MyObjectBuilder_AirtightHangarDoor), typeof(MyObjectBuilder_AirtightSlideDoor) }; }
		}

		protected override bool EffectOwnerCanAccess
		{
			get { return true; }
		}

		protected override void StartEffect(IMyCubeBlock block)
		{
			((IMyDoor)block).CloseDoor();
		}

	}
}
