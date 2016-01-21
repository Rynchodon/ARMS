using Rynchodon.Attached;
using Sandbox.ModAPI;

namespace Rynchodon.AntennaRelay
{
	public class NetworkBlock
	{

		private readonly Logger m_logger;

		public readonly IMyCubeBlock Block;

		public NetworkBlock(IMyCubeBlock block)
		{
			this.m_logger = new Logger(GetType().Name, block);
			this.Block = block;
		}

		/// <summary>
		/// For laser/radio communication etc. Relations are tested elsewhere.
		/// </summary>
		/// <param name="other">Node that may be connected.</param>
		/// <returns>True if the blocks are connected.</returns>
		protected virtual bool TestConnectSpecial(NetworkNode other)
		{
			return false;
		}

		/// <summary>
		/// Tests if this node is directly connected to another node. Connection must be bi-directional.
		/// </summary>
		/// <param name="other">The other node that may be connected.</param>
		/// <returns>True iff the nodes are connected.</returns>
		protected override sealed bool TestConnection(NetworkNode other)
		{
			if (!Block.IsWorking)
			{
				m_logger.debugLog("Not working", "TestConnection()", Logger.severity.TRACE);
				return false;
			}
			IMyCubeBlock otherBlock = other.Entity as IMyCubeBlock;
			if (otherBlock != null && !otherBlock.IsWorking)
			{
				m_logger.debugLog("Other block not working: " + otherBlock.DisplayNameText, "TestConnection()", Logger.severity.TRACE);
				return false;
			}

			if (!Block.canConsiderFriendly(other.Entity))
			{
				m_logger.debugLog("not friendly: " + other.Entity.getBestName(), "TestConnection()", Logger.severity.TRACE);
				return false;
			}
			if (TestConnectSpecial(other))
			{
				m_logger.debugLog("Special connection: " + other.Entity.getBestName(), "TestConnection()", Logger.severity.TRACE);
				return true;
			}

			if (otherBlock != null)
			{
				if (AttachedGrid.IsGridAttached(Block.CubeGrid, otherBlock.CubeGrid, AttachedGrid.AttachmentKind.Terminal))
				{
					m_logger.debugLog("Grid attached: " + other.Entity.getBestName(), "TestConnection()", Logger.severity.TRACE);
					return true;
				}
				else
					m_logger.debugLog("Not attached: " + other.Entity.getBestName(), "TestConnection()", Logger.severity.TRACE);
			}
			else
				m_logger.debugLog("Not a block: " + other.Entity.getBestName(), "TestConnection()", Logger.severity.TRACE);

			return false;
		}

	}
}
