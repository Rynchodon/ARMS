using System;
using Rynchodon.Attached;
using Rynchodon.Utility;
using Sandbox.ModAPI;
using VRage.Game.ModAPI;
using VRage.ModAPI;

namespace Rynchodon.AntennaRelay
{
	/// <summary>
	/// Block that is a partial participant in a network, only connects to a single node.
	/// </summary>
	public class RelayClient : IRelayPart
	{

		private const ulong UpdateFrequency = 10ul;

		/// <summary>
		/// If a node or client already exists for the block, returns that. Otherwise, creates a new NetworkClient and returns it.
		/// </summary>
		public static IRelayPart GetOrCreateRelayPart(IMyCubeBlock block)
		{
			RelayNode node;
			if (Registrar.TryGetValue(block, out node))
				return node;
			RelayClient client;
			if (Registrar.TryGetValue(block, out client))
				return client;
			return new RelayClient(block);
		}

		public static bool TryGetRelayPart(IMyCubeBlock block, out IRelayPart relayPart)
		{
			RelayNode node;
			if (Registrar.TryGetValue(block, out node))
			{
				relayPart = node;
				return true;
			}
			RelayClient client;
			if (Registrar.TryGetValue(block, out client))
			{
				relayPart = client;
				return true;
			}
			relayPart = null;
			return false;
		}

		private readonly IMyCubeBlock m_block;

		private RelayNode value_node;
		private ulong m_nextNodeSet;

		private RelayStorage m_storage;

		private Action<Message> m_messageHandler;

		public string DebugName { get { return m_block.nameWithId(); } }
		public long OwnerId { get { return m_block.OwnerId; } }

		private Logable Log
		{ get { return new Logable(m_block); } }

		/// <summary>
		/// Use GetOrCreateRelayPart if client may already exist.
		/// </summary>
		public RelayClient(IMyCubeBlock block, Action<Message> messageHandler = null)
		{
			this.m_block = block;
			this.m_messageHandler = messageHandler;

			Registrar.Add(block, this);
		}

		private bool IsConnected(RelayNode node)
		{
			return !node.Block.Closed && node.Block.IsWorking && m_block.canConsiderFriendly(node.Block) && AttachedGrid.IsGridAttached(m_block.CubeGrid, node.Block.CubeGrid, AttachedGrid.AttachmentKind.Terminal);
		}

		private RelayNode GetNode()
		{
			if (Globals.UpdateCount < m_nextNodeSet)
				return value_node;
			m_nextNodeSet = Globals.UpdateCount + UpdateFrequency;

			RelayNode newNode = value_node;
			if (newNode == null || !IsConnected(newNode))
			{
				newNode = null;
				Registrar.ForEach<RelayNode>(node => {
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
		public RelayStorage GetStorage()
		{
			RelayNode node = GetNode();
			RelayStorage store = node != null ? node.Storage : null;

			Log.DebugLog("current storage: " + StorageName(m_storage) + ", new storage: " + StorageName(store), condition: m_storage != store);
			if (store != m_storage && m_messageHandler != null)
			{
				if (m_storage != null)
					m_storage.RemoveMessageHandler(m_block.EntityId);
				if (store != null)
					store.AddMessageHandler(m_block.EntityId, m_messageHandler);
			}
			m_storage = store;
			return m_storage;
		}

		private string StorageName(RelayStorage store)
		{
			if (store == null)
				return "null";
			if (store.PrimaryNode == null)
				return "NetworkStorage without PrimaryNode!";
			return store.PrimaryNode.DebugName;
		}

	}
}
