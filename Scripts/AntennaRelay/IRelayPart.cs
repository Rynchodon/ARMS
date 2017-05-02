
namespace Rynchodon.AntennaRelay
{
	/// <summary>
	/// Participant in a relay network.
	/// </summary>
	public interface IRelayPart
	{
		string DebugName { get; }
		long OwnerId { get; }
		RelayStorage GetStorage();
	}
}
