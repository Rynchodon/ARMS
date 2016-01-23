using System;
using System.Collections.Generic;
using Rynchodon.Attached;
using Sandbox.ModAPI;
using VRage.ModAPI;
using VRageMath;
using Ingame = Sandbox.ModAPI.Ingame;

namespace Rynchodon.AntennaRelay
{
	public class NetworkNode
	{

		private static int s_searchIdPool;

		private readonly Logger m_logger;
		private readonly Func<string> m_loggingName;
		private readonly Func<long> m_ownerId;
		private readonly IMyEntity m_entity;
		private readonly IMyPlayer m_player;
		private readonly IMyCubeBlock m_comp_blockAttach;
		private readonly ComponentRadio m_comp_radio;
		private readonly ComponentLaser m_comp_laser;

		private readonly HashSet<NetworkNode> m_directConnect = new HashSet<NetworkNode>();
		private int m_lastSearchId;
		private NetworkStorage value_storage;

		public string LoggingName { get { return m_loggingName.Invoke(); } }

		public NetworkStorage Storage
		{
			get { return value_storage; }
			private set
			{
				if (value_storage != null)
					m_logger.debugLog("new storage, primary node: " + value_storage.PrimaryNode.LoggingName +
						" => " + value.PrimaryNode.LoggingName, "set_m_storage()", Logger.severity.DEBUG);
				else
					m_logger.debugLog("new storage, primary node: " + value.PrimaryNode.LoggingName,
						"set_m_storage()", Logger.severity.DEBUG);
				value_storage = value;
				foreach (NetworkNode node in m_directConnect)
					if (node.Storage != this.Storage)
						node.Storage = this.Storage;
			}
		}

		/// <summary>
		/// Creates a NetworkNode for a block, checking block attachments, laser, and radio communication.
		/// </summary>
		/// <param name="block">The block to create the NetworkNode for.</param>
		public NetworkNode(IMyCubeBlock block)
		{
			this.m_loggingName = ()=> block.DisplayNameText;
			this.m_logger = new Logger(GetType().Name, block);
			this.m_ownerId = () => block.OwnerId;
			this.m_entity = block;
			this.m_comp_blockAttach = block;
			this.m_comp_radio = ComponentRadio.TryCreateRadio(block);

			Ingame.IMyLaserAntenna lAnt = block as Ingame.IMyLaserAntenna;
			if (lAnt != null)
				this.m_comp_laser = new ComponentLaser(lAnt);

			Registrar.Add(block, this);
		}

		/// <summary>
		/// Creates a NetworkNode for a character, checking radio communication.
		/// </summary>
		/// <param name="character">The character to create the NetworkNode for.</param>
		public NetworkNode(IMyCharacter character)
		{
			IMyPlayer player = character.GetPlayer_Safe();

			this.m_loggingName = () => player.DisplayName;
			this.m_logger = new Logger(GetType().Name, this.m_loggingName);
			this.m_ownerId = () => player.PlayerID;
			this.m_entity = character as IMyEntity;
			this.m_player = player;
			this.m_comp_radio = ComponentRadio.CreateRadio(character);

			Registrar.Add(character as IMyEntity, this);
		}

		/// <summary>
		/// Updates direct connections between this node and other nodes.
		/// Tests connection to primary storage node.
		/// </summary>
		public void Update()
		{
			if (m_comp_laser != null)
				m_comp_laser.Update();

			bool checkPrimary = false;

			Registrar.ForEach((NetworkNode node) => {
				if (node == this)
					return;

				if (TestConnection(node))
				{
					if (m_directConnect.Add(node))
					{
						m_logger.debugLog("Now connected to " + node.LoggingName, "Update()", Logger.severity.DEBUG);

						if (this.Storage == null)
						{
							if (node.Storage != null)
							{
								m_logger.debugLog("Using storage from other node: " + node.LoggingName
									+ ", primary: " + node.Storage.PrimaryNode.LoggingName, "Update()", Logger.severity.DEBUG);
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
						m_logger.debugLog("Affirmed connection to " + node.LoggingName, "Update()", Logger.severity.TRACE);
				}
				else
				{
					if (m_directConnect.Remove(node))
					{
						m_logger.debugLog("No longer connected to " + node.LoggingName, "Update()", Logger.severity.DEBUG);
						checkPrimary = true;
					}
					else
						m_logger.debugLog("Affirmed lack of connection to " + node.LoggingName, "Update()", Logger.severity.TRACE);
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

			// laser connections don't update immediately, so don't worry about a single warning (per block) if there is a laser involved
			m_logger.debugLog(!IsConnectedTo(Storage.PrimaryNode), "Not connected to primary node", "Update()", Logger.severity.WARNING);
		}

		/// <summary>
		/// Tests whether a connection is possible between this and another NetworkNode.
		/// </summary>
		/// <param name="other">Node to test connection to this.</param>
		/// <returns>true iff the nodes are connected.</returns>
		private bool TestConnection(NetworkNode other)
		{
			// test relations
			if (this.m_comp_blockAttach != null)
			{
				if (!this.m_comp_blockAttach.canConsiderFriendly(other.m_ownerId.Invoke()))
					return false;
			}
			else if (!this.m_player.canConsiderFriendly(other.m_ownerId.Invoke()))
				return false;

			// test block connection
			if (this.m_comp_blockAttach != null && other.m_comp_blockAttach != null &&
				this.m_comp_blockAttach.IsWorking && other.m_comp_blockAttach.IsWorking &&
				AttachedGrid.IsGridAttached(this.m_comp_blockAttach.CubeGrid, other.m_comp_blockAttach.CubeGrid, AttachedGrid.AttachmentKind.Terminal))
			{
				m_logger.debugLog("blocks are attached: "+other.LoggingName, "TestConnection()");
				return true;
			}

			// test laser
			if (this.m_comp_laser != null && other.m_comp_laser != null && this.m_comp_laser.IsConnectedTo(other.m_comp_laser))
			{
				m_logger.debugLog("laser connection: " + other.LoggingName, "TestConnection()");
				return true;
			}

			// test radio
			if (this.m_comp_radio != null && other.m_comp_radio != null&&
				this.m_comp_radio.IsWorkBroadReceive && other.m_comp_radio.IsWorkBroadReceive)
			{
				float radius = this.m_comp_radio.Radius, otherRadius = other.m_comp_radio.Radius;
				float distSquared = Vector3.DistanceSquared(this.m_entity.GetPosition(), other.m_entity.GetPosition());
				if (distSquared < radius * radius && distSquared < otherRadius * otherRadius)
				{
					m_logger.debugLog("radio connection: " + other.LoggingName, "TestConnection()");
					return true;
				}
			}

			return false;
		}

		/// <summary>
		/// Checks if this node is connected to another, either directly or indirectly.
		/// </summary>
		/// <param name="other">The other node to check</param>
		/// <returns>true iff the nodes are connected.</returns>
		private bool IsConnectedTo(NetworkNode other)
		{
			if (this == other)
				return true;

			return IsConnectedTo(other, ++s_searchIdPool);
		}

		/// <summary>
		/// Checks if this node is connected to another, either directly or indirectly.
		/// </summary>
		/// <param name="other">the other node to check</param>
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
