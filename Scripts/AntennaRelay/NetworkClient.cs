using System;
using Rynchodon.Attached;
using Sandbox.ModAPI;

namespace Rynchodon.AntennaRelay
{
	/// <summary>
	/// Block that is a partial participant in a network, only connects to a single node.
	/// </summary>
	public class NetworkClient
	{

		private const ulong UpdateFrequency = 10ul;

		private readonly IMyCubeBlock m_block;
		private readonly Logger m_logger;

		private NetworkNode value_node;
		private ulong m_nextNodeSet;

		private NetworkStorage m_storage;

		private Action<Message> m_messageHandler;

		public NetworkClient(IMyCubeBlock block, Action<Message> messageHandler = null)
		{
			this.m_block = block;
			this.m_messageHandler = messageHandler;
			this.m_logger = new Logger(GetType().Name, block);
		}

		private bool IsConnected(NetworkNode node)
		{
			return !node.Block.Closed && m_block.canConsiderFriendly(node.Block) && AttachedGrid.IsGridAttached(m_block.CubeGrid, node.Block.CubeGrid, AttachedGrid.AttachmentKind.Terminal);
		}

		private NetworkNode GetNode()
		{
			if (Globals.UpdateCount < m_nextNodeSet)
				return value_node;

			if (value_node == null || !IsConnected(value_node))
			{
				value_node = null;
				Registrar.ForEach<NetworkNode>(node => {
					if (node.Block != null && IsConnected(node))
					{
						value_node = node;
						return true;
					}
					return false;
				});
			}

			m_nextNodeSet = Globals.UpdateCount + UpdateFrequency;
			return value_node;
		}

		public NetworkStorage GetStorage()
		{
			NetworkNode node = GetNode();
			NetworkStorage store = node != null ? node.Storage : null;
			m_logger.debugLog("current storage: " + m_storage + ", new storage: " + store, "get_Storage()");
			if (store != m_storage && m_messageHandler != null)
			{
				if (m_storage != null)
				{
					m_logger.debugLog("removing handler: " + m_block.EntityId, "get_Storage()");
					m_storage.RemoveMessageHandler(m_block.EntityId);
				}
				if (store != null)
				{
					m_logger.debugLog("adding handler: " + m_block.EntityId, "get_Storage()");
					store.AddMessageHandler(m_block.EntityId, m_messageHandler);
				}
			}
			m_storage = store;
			return m_storage;
		}

	}
}
