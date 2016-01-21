using System;
using System.Collections.Generic;
using Sandbox.ModAPI;
using VRage.ModAPI;

namespace Rynchodon.AntennaRelay
{
	public class NetworkNode
	{

		/// <summary>Tests if this node is directly connected to another node. Connection must be bi-directional.</summary>
		/// <param name="node">Node to test for connectivity.</param>
		/// <returns>True iff the nodes are connected.</returns>
		public delegate bool TestConnection(NetworkNode node);

		private static int s_searchIdPool;

		private readonly Logger m_logger;
		private readonly HashSet<NetworkNode> m_directConnect = new HashSet<NetworkNode>();
		private int m_lastSearchId;
		private NetworkStorage value_storage;

		protected TestConnection m_testConn;

		public readonly IMyEntity Entity;
		public readonly IMyCubeBlock Block;

		public NetworkStorage Storage
		{
			get { return value_storage; }
			private set
			{
				if (value_storage != null)
					m_logger.debugLog("new storage, primary node: " + value_storage.PrimaryNode.Entity.getBestName() +
						" => " + value.PrimaryNode.Entity.getBestName(), "set_m_storage()", Logger.severity.DEBUG);
				else
					m_logger.debugLog("new storage, primary node: " + value.PrimaryNode.Entity.getBestName(),
						"set_m_storage()", Logger.severity.DEBUG);
				value_storage = value;
				foreach (NetworkNode node in m_directConnect)
					if (node.Storage != this.Storage)
						node.Storage = this.Storage;
			}
		}

		public NetworkNode(IMyEntity entity)
		{
			this.m_logger = new Logger(GetType().Name, entity);
			this.Entity = entity;
			this.Block = entity as IMyCubeBlock;
			Registrar.Add(entity, this);
		}

		/// <summary>
		/// Updates direct connections between this node and other nodes.
		/// Tests connection to primary storage node.
		/// </summary>
		public void Update()
		{
			bool checkPrimary = false;

			Registrar.ForEach((NetworkNode node) => {
				if (node == this)
					return;

				if (m_testConn(node))
				{
					if (m_directConnect.Add(node))
					{
						m_logger.debugLog("Now connected to " + node.Entity.getBestName(), "Update()", Logger.severity.DEBUG);

						if (this.Storage == null)
						{
							if (node.Storage != null)
							{
								m_logger.debugLog("Using storage from other node: " + node.Entity.getBestName()
									+ ", primary: " + node.Storage.PrimaryNode.Entity.getBestName(), "Update()", Logger.severity.DEBUG);
								this.Storage = node.Storage;
							}
						}
						else if (node.Storage != null && this.Storage != node.Storage)
						{
							if (this.Storage.Size < node.Storage.Size)
							{
								m_logger.debugLog("Nodes have different storages, copying to other node's storage", "Update()", Logger.severity.DEBUG);
								this.Storage.CopyTo(node.Storage);
								this.Storage = node.Storage;
							}
							else
							{
								m_logger.debugLog("Nodes have different storages, copying to this node's storage", "Update()", Logger.severity.DEBUG);
								node.Storage.CopyTo(this.Storage);
								node.Storage = this.Storage;
							}
						}
					}
					else
						m_logger.debugLog("Affirmed connection to " + node.Entity.getBestName(), "Update()", Logger.severity.TRACE);
				}
				else
				{
					if (m_directConnect.Remove(node))
					{
						m_logger.debugLog("No longer connected to " + node.Entity.getBestName(), "Update()", Logger.severity.DEBUG);
						checkPrimary = true;
					}
					else
						m_logger.debugLog("Affirmed lack of connection to " + node.Entity.getBestName(), "Update()", Logger.severity.TRACE);
				}
			});

			if (Storage == null)
			{
				m_logger.debugLog("No storage, creating a new one", "Update()", Logger.severity.INFO);
				Storage = new NetworkStorage(this);
			}
			else if (checkPrimary && !IsConnectedTo(Storage.PrimaryNode))
			{
				m_logger.debugLog("Lost connection to primary, cloning storage", "Update()", Logger.severity.INFO);
				Storage = Storage.Clone(this);
			}

			m_logger.debugLog(!IsConnectedTo(Storage.PrimaryNode), "Not connected to primary node", "Update()", Logger.severity.ERROR);
		}

		/// <summary>
		/// Tests if this node is connected to another, either directly or indirectly.
		/// </summary>
		/// <param name="other">The other node to test</param>
		/// <returns>true iff the nodes are connected.</returns>
		private bool IsConnectedTo(NetworkNode other)
		{
			if (this == other)
				return true;

			return IsConnectedTo(other, ++s_searchIdPool);
		}

		/// <summary>
		/// Tests if this node is connected to another, either directly or indirectly.
		/// </summary>
		/// <param name="other">the other node to test</param>
		/// <param name="id">the search id</param>
		/// <returns>true iff the nodes are connected.</returns>
		private bool IsConnectedTo(NetworkNode other, int id)
		{
			m_lastSearchId = id;

			if (m_directConnect.Contains(other))
				return true;

			foreach (NetworkNode node in m_directConnect)
				if (node.m_lastSearchId != id && node.IsConnectedTo(other, id))
					return true;

			return false;
		}

	}
}
