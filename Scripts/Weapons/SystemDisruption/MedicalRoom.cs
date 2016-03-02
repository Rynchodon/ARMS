
using Sandbox.Common.ObjectBuilders;
using VRage.ObjectBuilders;

namespace Rynchodon.Weapons.SystemDisruption
{
	public class MedicalRoom : Disruption
	{

		protected override MyObjectBuilderType[] BlocksAffected
		{
			get { return new MyObjectBuilderType[] { typeof(MyObjectBuilder_MedicalRoom) }; }
		}

		protected override bool EffectOwnerCanAccess
		{
			get { return true; }
		}

	}
}
