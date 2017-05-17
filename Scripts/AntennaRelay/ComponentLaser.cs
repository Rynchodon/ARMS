using Sandbox.Common.ObjectBuilders;
using Sandbox.ModAPI;
using VRage.Game.ModAPI;

namespace Rynchodon.AntennaRelay
{
	/// <summary>
	/// Laser component of a NetworkNode
	/// </summary>
	public class ComponentLaser
	{

		private const byte FinalState = 5;

		private readonly IMyLaserAntenna m_laser;

		private long? m_targetEntityId;
		/// <summary>state 5 is the final state. It is possible for one to be in state 5, while the other is not</summary>
		private byte m_state;

		/// <summary>
		/// Creates the laser component from a laser antenna block.
		/// </summary>
		/// <param name="laser">The block to create the laser component for.</param>
		public ComponentLaser(IMyLaserAntenna laser)
		{
			this.m_laser = laser;
		}

		/// <summary>
		/// Updates the connection state and target. Both ComponentLaser must be updated for IsConnectedTo to return true.
		/// </summary>
		public void Update()
		{
			if (!m_laser.IsWorking)
			{
				m_targetEntityId = null;
				return;
			}

			MyObjectBuilder_LaserAntenna builder = ((IMyCubeBlock)m_laser).GetObjectBuilderCubeBlock() as MyObjectBuilder_LaserAntenna;
			m_targetEntityId = builder.targetEntityId;
			if (m_targetEntityId == null)
				return;
			m_state = builder.State;
		}

		/// <summary>
		/// Checks for a connection between two lasers. Both of them must have been updated.
		/// </summary>
		/// <param name="other">The laser to check for a connection to.</param>
		/// <returns>True iff this laser is targeting other and both are in the final stage.</returns>
		public bool IsConnectedTo(ComponentLaser other)
		{
			return m_targetEntityId == other.m_laser.EntityId && m_state == FinalState && other.m_state == FinalState;
		}

	}
}
