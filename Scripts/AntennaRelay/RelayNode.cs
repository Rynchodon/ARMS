#if DEBUG
#define TRACE
#endif

using System;
using System.Collections.Generic;
using Rynchodon.Attached;
using Rynchodon.Utility;
using Sandbox.ModAPI;
using VRage.Game.ModAPI;
using VRage.ModAPI;

namespace Rynchodon.AntennaRelay
{
	/// <summary>
	/// Full participant in a network, connects to other nodes.
	/// </summary>
	public class RelayNode : IRelayPart
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
		/// <summary>Storages that receive the position of this node but not data.</summary>
		private static HashSet<RelayStorage> s_sendPositionTo = new HashSet<RelayStorage>();

		private readonly Func<string> m_debugName;
		private readonly Func<long> m_ownerId;
		private readonly IMyEntity m_entity;
		public readonly IMyPlayer m_player;
		private readonly IMyCubeBlock m_comp_blockAttach;
		private readonly ComponentRadio m_comp_radio;
		private readonly IMyLaserAntenna m_comp_laser;

		/// <summary>Two-way communication is established between this node and these nodes.</summary>
		private readonly HashSet<RelayNode> m_directConnect = new HashSet<RelayNode>();
		/// <summary>Data can be sent from this node to these nodes.</summary>
		private readonly HashSet<RelayNode> m_oneWayConnect = new HashSet<RelayNode>();
		private int m_lastSearchId;
		private RelayStorage value_storage;
		private Action<Message> value_messageHandler;

		/// <summary>Name used to identify this node.</summary>
		public string DebugName { get { return m_debugName.Invoke(); } }

		public IMyEntity Entity { get { return m_entity; } }

		public IMyCubeBlock Block { get { return m_entity as IMyCubeBlock; } }

		public long EntityId { get { return m_entity.EntityId; } }

		private Logable Log { get { return m_player != null ? new Logable(m_player.DisplayName) : new Logable(m_entity); } }

		public long OwnerId { get { return m_ownerId(); } }

		public Action<Message> MessageHandler
		{
			get { return value_messageHandler; }
			set
			{
				if (Storage != null)
				{
					if (value_messageHandler != null)
						Storage.RemoveMessageHandler(EntityId);
					if (value != null)
						Storage.AddMessageHandler(EntityId, value);
				}
				value_messageHandler = value;
			}
		}

		/// <summary>Contains all the LastSeen and Message for this node.</summary>
		public RelayStorage Storage
		{
			get { return value_storage; }
			private set
			{
				if (value_storage != null)
				{
					Log.TraceLog("new storage, primary node: " + value_storage.PrimaryNode.DebugName +
						" => " + value.PrimaryNode.DebugName, Logger.severity.DEBUG);

					// one ways are no longer valid
					foreach (RelayNode node in m_oneWayConnect)
						Storage.RemovePushTo(node);
					m_oneWayConnect.Clear();

					if (MessageHandler != null)
						value_storage.RemoveMessageHandler(EntityId);
				}
				else
					Log.TraceLog("new storage, primary node: " + value.PrimaryNode.DebugName, Logger.severity.DEBUG);

				if (value != null && MessageHandler != null)
					value.AddMessageHandler(EntityId, MessageHandler);

				value_storage = value;
				foreach (RelayNode node in m_directConnect)
					if (node.Storage != this.Storage)
						node.Storage = this.Storage;
			}
		}

		public RelayStorage GetStorage()
		{
			return Storage;
		}

		/// <summary>
		/// Creates a NetworkNode for a block, checking block attachments, laser, and radio communication.
		/// </summary>
		/// <param name="block">The block to create the NetworkNode for.</param>
		public RelayNode(IMyCubeBlock block)
		{
			this.m_debugName = () => block.DisplayNameText;
			this.m_ownerId = () => block.OwnerId;
			this.m_entity = block;
			this.m_comp_blockAttach = block;
			this.m_comp_radio = ComponentRadio.TryCreateRadio(block);

			IMyLaserAntenna lAnt = block as IMyLaserAntenna;
			if (lAnt != null)
				this.m_comp_laser = lAnt;

			Registrar.Add(block, this);
		}

		/// <summary>
		/// Creates a NetworkNode for a character, checking radio communication.
		/// </summary>
		/// <param name="character">The character to create the NetworkNode for.</param>
		public RelayNode(IMyCharacter character)
		{
			IMyPlayer player = character.GetPlayer_Safe();
			this.m_debugName = () => player.DisplayName;
			this.m_ownerId = () => player.IdentityId;
			this.m_entity = character as IMyEntity;
			this.m_player = player;
			this.m_comp_radio = ComponentRadio.CreateRadio(character);

			Registrar.Add(character as IMyEntity, this);
		}

		/// <summary>
		/// Create a NetworkNode for a missile. Update100() will have to be invoked by GuidedMissile.
		/// </summary>
		public RelayNode(IMyEntity missile, Func<long> ownerId, ComponentRadio radio)
		{
			this.m_debugName = missile.getBestName;
			this.m_ownerId = ownerId;
			this.m_entity = missile;
			this.m_comp_radio = radio;

			Registrar.Add(missile, this);
		}

		/// <summary>
		/// Updates direct connections between this node and other nodes.
		/// When debug is set, checks connection to primary storage node.
		/// </summary>
		public void Update100()
		{
			s_sendPositionTo.Clear();

			bool checkPrimary = false;

			Registrar.ForEach((RelayNode node) => {
				if (node == this)
					return;

				CommunicationType connToNode = TestConnection(node);

				if (connToNode == CommunicationType.TwoWay)
				{
					if (m_directConnect.Add(node))
					{
						Log.TraceLog("Now connected to " + node.DebugName, Logger.severity.DEBUG);

						if (this.Storage == null)
						{
							if (node.Storage != null)
							{
								Log.TraceLog("Using storage from other node: " + node.DebugName
									+ ", primary: " + node.Storage.PrimaryNode.DebugName, Logger.severity.DEBUG);
								this.Storage = node.Storage;
							}
						}
						else if (node.Storage != null && this.Storage != node.Storage)
						{
							// should prefer blocks since they are less likely to be removed from world while offline
							if (this.Block != null && node.Block == null)
							{
								Log.TraceLog("Nodes have different storages, copying to this node's storage because this node is a block", Logger.severity.DEBUG);
								node.Storage.CopyTo(this.Storage);
								node.Storage = this.Storage;
							}
							else if (this.Block == null && node.Block != null)
							{
								Log.TraceLog("Nodes have different storages, copying to other node's storage beacause other node is a block", Logger.severity.DEBUG);
								this.Storage.CopyTo(node.Storage);
								this.Storage = node.Storage;
							}
							else if (this.Storage.Size < node.Storage.Size)
							{
								Log.TraceLog("Nodes have different storages, copying to other node's storage", Logger.severity.DEBUG);
								this.Storage.CopyTo(node.Storage);
								this.Storage = node.Storage;
							}
							else
							{
								Log.TraceLog("Nodes have different storages, copying to this node's storage", Logger.severity.DEBUG);
								node.Storage.CopyTo(this.Storage);
								node.Storage = this.Storage;
							}
						}
					}
				}
				else
				{
					if (m_directConnect.Remove(node))
					{
						Log.TraceLog("No longer connected to " + node.DebugName, Logger.severity.DEBUG);
						checkPrimary = true;
					}
				}

				if (Storage != null)
					if (connToNode == CommunicationType.OneWay)
					{
						if (m_oneWayConnect.Add(node))
						{
							Log.TraceLog("New one-way connection to " + node.DebugName, Logger.severity.DEBUG);
							Storage.AddPushTo(node);
						}
					}
					else
					{
						if (m_oneWayConnect.Remove(node))
						{
							Log.TraceLog("Lost one-way connection to " + node.DebugName, Logger.severity.DEBUG);
							Storage.RemovePushTo(node);
						}
					}
			});

			if (Storage == null)
			{
				Log.DebugLog("No storage, creating a new one", Logger.severity.INFO);
				Storage = new RelayStorage(this);
			}
			else if (checkPrimary && !IsConnectedTo(Storage.PrimaryNode))
			{
				Log.DebugLog("Lost connection to primary, cloning storage", Logger.severity.INFO);
				Storage = Storage.Clone(this);
			}

			// connections don't update immediately, so don't worry about a single message (per block)
			Log.TraceLog("Not connected to primary node", Logger.severity.INFO, condition: !IsConnectedTo(Storage.PrimaryNode));

			IMyEntity topEntity = m_entity.GetTopMostParent();

			Log.TraceLog("Sending self to " + s_sendPositionTo.Count + " neutral/hostile storages", Logger.severity.TRACE);
			RelayStorage.Receive(s_sendPositionTo, new LastSeen(topEntity, LastSeen.DetectedBy.Broadcasting));

			if (Storage.VeryRecentRadarInfo(topEntity.EntityId))
				return;

			if (Block == null)
				Storage.Receive(new LastSeen(topEntity, LastSeen.DetectedBy.Broadcasting, new LastSeen.RadarInfo(topEntity)));
			else
				foreach (IMyCubeGrid grid in AttachedGrid.AttachedGrids(Block.CubeGrid, AttachedGrid.AttachmentKind.Terminal, true))
					Storage.Receive(new LastSeen(grid, LastSeen.DetectedBy.Broadcasting, new LastSeen.RadarInfo(grid)));
		}

		/// <summary>
		/// Force this node to create a storage. Should only be called from game thread.
		/// </summary>
		public void ForceCreateStorage()
		{
			Storage = new RelayStorage(this);
		}

		/// <summary>
		/// Tests whether a connection is possible between this and another NetworkNode.
		/// </summary>
		/// <param name="other">Node to test connection to this.</param>
		private CommunicationType TestConnection(RelayNode other)
		{
			if (!this.m_ownerId().canConsiderFriendly(other.m_ownerId()))
			{
				if (this.m_comp_radio != null && other.m_comp_radio != null && this.m_comp_radio.CanBroadcastPositionTo(other.m_comp_radio) && other.Storage != null)
					if (s_sendPositionTo.Add(other.Storage))
						Log.TraceLog("Hostile receiver in range: " + other.DebugName + ", new storage: " + other.Storage.PrimaryNode.DebugName);
					else
						Log.TraceLog("Hostile receiver in range: " + other.DebugName + ", existing storage: " + other.Storage.PrimaryNode.DebugName);
				return CommunicationType.None;
			}

			// test block connection
			// skip is working test so that storage doesn't split if ship powers off
			if (this.m_comp_blockAttach != null && other.m_comp_blockAttach != null &&
				AttachedGrid.IsGridAttached(this.m_comp_blockAttach.CubeGrid, other.m_comp_blockAttach.CubeGrid, AttachedGrid.AttachmentKind.Terminal))
				return CommunicationType.TwoWay;

			// test laser
			if (this.m_comp_laser != null && other.m_comp_laser != null && m_comp_laser.Other == other.m_comp_laser && other.m_comp_laser.Other == m_comp_laser)
				return CommunicationType.TwoWay;

			// test radio
			if (this.m_comp_radio != null && other.m_comp_radio != null)
			{
				// check radio antenna to radio antenna
				CommunicationType radioResult;
				radioResult = this.m_comp_radio.TestConnection(other.m_comp_radio);
				if (radioResult != CommunicationType.None)
				{
					Log.TraceLog("radio connection to " + other.DebugName + " is " + radioResult);
					return radioResult;
				}

				// check beacon to radio antenna
				if (this.m_comp_radio.CanBroadcastPositionTo(other.m_comp_radio) && other.Storage != null)
					if (s_sendPositionTo.Add(other.Storage))
						Log.TraceLog("Friendly receiver in range: " + other.DebugName + ", new storage: " + other.Storage.PrimaryNode.DebugName);
					else
						Log.TraceLog("Friendly receiver in range: " + other.DebugName + ", existing storage: " + other.Storage.PrimaryNode.DebugName);
			}

			return CommunicationType.None;
		}

		/// <summary>
		/// Checks for established connections between this node and another.
		/// </summary>
		/// <param name="other">The other node to check</param>
		/// <returns>true iff the nodes are connected.</returns>
		private bool IsConnectedTo(RelayNode other)
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
		private bool IsConnectedTo(RelayNode other, int id)
		{
			m_lastSearchId = id;

			if (m_directConnect.Contains(other))
				return true;

			foreach (RelayNode node in m_directConnect)
				if (node.m_lastSearchId != id && node.IsConnectedTo(other, id))
					return true;

			return false;
		}

	}
}
