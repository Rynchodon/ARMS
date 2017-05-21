
using Sandbox.Common.ObjectBuilders;
using Sandbox.Game.Entities;
using VRage.Game.ModAPI;
using VRage.ObjectBuilders;

namespace Rynchodon.Weapons.SystemDisruption
{
	public class CryoChamberMurder : Disruption
	{

		protected override MyObjectBuilderType[] BlocksAffected
		{
			get { return new MyObjectBuilderType[] { typeof(MyObjectBuilder_CryoChamber) }; }
		}

		protected override bool CanDisrupt(IMyCubeBlock block)
		{
			return (block as MyCockpit).Pilot as IMyCharacter != null;
		}

		protected override void StartEffect(IMyCubeBlock block)
		{
			IMyCharacter pilot = (block as MyCockpit).Pilot as IMyCharacter;
			if (pilot == null)
				return;

			Logger.DebugLog("Killing: " + pilot + ", in " + block.DisplayNameText);
			pilot.Kill();
		}

	}
}
