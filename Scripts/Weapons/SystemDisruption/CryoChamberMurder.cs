
using Sandbox.Common.ObjectBuilders;
using Sandbox.Game.Entities;
using Sandbox.ModAPI;
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

			m_logger.debugLog("Killing: " + pilot + ", in " + block.DisplayNameText, "StartEffect()");
			pilot.Kill();
		}

	}
}
