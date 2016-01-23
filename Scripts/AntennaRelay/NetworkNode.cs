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

		public enum CommunicationType : byte
		{
			/// <summary>No communication is possible.</summary>
			None,
			/// <summary>Data can be sent from this node to another node.</summary>
			OneWay,
			/// <summary>Data can be sent both ways between the two nodes.</summary>
			TwoWay
		}

		private static int s_searchIdPool;

		private readonly Logger m_logger;
		private readonly Func<string> m_loggingName;
		private readonly Func<long> m_ownerId;
		private readonly IMyEntity m_entity;
		private readonly IMyPlayer m_player;
		private readonly IMyCubeBlock m_comp_blockAttach;
		private readonly ComponentRadio m_comp_radio;
		private readonly ComponentLaser m_comp_laser;

		/// <summary>Two-way communication is established between this node and these nodes.</summary>
		private readonly HashSet<NetworkNode> m_directConnect = new HashSet<NetworkNode>();
		/// <summary>Data can be sent from this node to these nodes.</summary>
		private readonly HashSet<NetworkNode> m_oneWayConnect = new HashSet<NetworkNode>();
		private int m_lastSearchId;
		private NetworkStorage value_storage;

		public string LoggingName { get { return m_loggingName.Invoke(); } }

		public NetworkStorage Storage
		{
			get { return value_storage; }
			private set
			{
				if (value_storage != null)
				{
					m_logger.debugLog("new storage, primary node: " + value_storage.PrimaryNode.LoggingName +
						" => " + value.PrimaryNode.LoggingName, "set_m_storage()", Logger.severity.DEBUG);

					// one ways are no longer valid
					foreach (NetworkNode node in m_oneWayConnect)
						Storage.RemovePushTo(node);
					m_oneWayConnect.Clear();
				}
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
			this.m_loggingName = () => block.DisplayNameText;
			this.m_logger = new Logger(GetType().Name, block) { MinimumLevel = Logger.severity.DEBUG };
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
			this.m_logger = new Logger(GetType().Name, this.m_loggingName) { MinimumLevel = Logger.severity.DEBUG };
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

				CommunicationType connToNode = TestConnection(node);

				if (connToNode == CommunicationType.TwoWay)
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

				if (Storage != null)
					if (connToNode == CommunicationType.OneWay)
					{
						if (m_oneWayConnect.Add(node))
						{
							m_logger.debugLog("New one-way connection to " + node.LoggingName, "Update()", Logger.severity.DEBUG);
							Storage.AddPushTo(node);
						}
						else
							m_logger.debugLog("Affirmed one-way connection to " + node.LoggingName, "Update()", Logger.severity.TRACE);
					}
					else
					{
						if (m_oneWayConnect.Remove(node))
						{
							m_logger.debugLog("Lost one-way connection to " + node.LoggingName, "Update()", Logger.severity.DEBUG);
							Storage.RemovePushTo(node);
						}
						else
							m_logger.debugLog("Affirmed lack of one-way connection to " + node.LoggingName, "Update()", Logger.severity.TRACE);
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
		private CommunicationType TestConnection(NetworkNode other)
		{
			// test relations
			if (this.m_comp_blockAttach != null)
			{
				if (!this.m_comp_blockAttach.canConsiderFriendly(other.m_ownerId.Invoke()))
					return CommunicationType.None;
			}
			else if (!this.m_player.canConsiderFriendly(other.m_ownerId.Invoke()))
				return CommunicationType.None;

			// test block connection
			if (this.m_comp_blockAttach != null && other.m_comp_blockAttach != null &&
				this.m_comp_blockAttach.IsWorking && other.m_comp_blockAttach.IsWorking &&
				AttachedGrid.IsGridAttached(this.m_comp_blockAttach.CubeGrid, other.m_comp_blockAttach.CubeGrid, AttachedGrid.AttachmentKind.Terminal))
			{
				m_logger.debugLog("blocks are attached: " + other.LoggingName, "TestConnection()");
				return CommunicationType.TwoWay;
			}

			// test laser
			if (this.m_comp_laser != null && other.m_comp_laser != null && this.m_comp_laser.IsConnectedTo(other.m_comp_laser))
			{
				m_logger.debugLog("laser connection: " + other.LoggingName, "TestConnection()");
				return CommunicationType.TwoWay;
			}

			// test radio
			if (this.m_comp_radio != null && other.m_comp_radio != null)
			{
				CommunicationType radioComm = this.m_comp_radio.TestConnection(other.m_comp_radio);
				switch (radioComm)
				{
					case CommunicationType.TwoWay:
						m_logger.debugLog("Two way radio communication: " + other.LoggingName, "TestConnection()");
						break;
					case CommunicationType.OneWay:
						m_logger.debugLog("One-way radio communication to " + other.LoggingName, "TestConnection()");
						break;
				}
				return radioComm;
			}

			return CommunicationType.None;
		}

		/// <summary>
		/// Checks for established connections between this node and another.
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
		/// Checks for established connections between this node and another.
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
