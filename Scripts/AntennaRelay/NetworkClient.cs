using System;
using Rynchodon.Attached;
using Sandbox.ModAPI;
using VRage.Game.ModAPI;

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
			return !node.Block.Closed && node.Block.IsWorking && m_block.canConsiderFriendly(node.Block) && AttachedGrid.IsGridAttached(m_block.CubeGrid, node.Block.CubeGrid, AttachedGrid.AttachmentKind.Terminal);
		}

		private NetworkNode GetNode()
		{
			if (Globals.UpdateCount < m_nextNodeSet)
				return value_node;
			m_nextNodeSet = Globals.UpdateCount + UpdateFrequency;

			NetworkNode newNode = value_node;
			if (newNode == null || !IsConnected(newNode))
			{
				newNode = null;
				Registrar.ForEach<NetworkNode>(node => {
					if (node.Block != null && IsConnected(node))
					{
						newNode = node;
						return true;
					}
					return false;
				});

				value_node = newNode;
			}

			return value_node;
		}

		/// <summary>
		/// Gets the NetworkStorage this client is connected to or null, if it is not connected to a storage.
		/// Always test against null, player may forget to put an antenna on the ship.
		/// </summary>
		/// <returns>The NetworkStorage this client is connected to or null.</returns>
		public NetworkStorage GetStorage()
		{
			NetworkNode node = GetNode();
			NetworkStorage store = node != null ? node.Storage : null;

			m_logger.debugLog(m_storage != store, "current storage: " + StorageName(m_storage) + ", new storage: " + StorageName(store), "get_Storage()");
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

		private string StorageName(NetworkStorage store)
		{
			if (store == null)
				return "null";
			if (store.PrimaryNode == null)
				return "NetworkStorage without PrimaryNode!";
			return store.PrimaryNode.LoggingName;
		}

	}
}
