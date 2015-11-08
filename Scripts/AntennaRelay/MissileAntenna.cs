
using VRage.ModAPI;

namespace Rynchodon.AntennaRelay
{
	public class MissileAntenna : Receiver
	{

		public MissileAntenna(IMyEntity missile)
			: base(missile)
		{
			Registrar.Add(missile, this);
		}

	}
}
