using System;
using System.Collections.Generic;
using System.Xml.Serialization;
using Rynchodon.Autopilot;
using Rynchodon.Update;
using Rynchodon.Utility.Network;
using Sandbox.ModAPI;
using VRage.Game.ModAPI;
using VRage.ModAPI;

namespace Rynchodon.AntennaRelay
{
	public class Message
	{

		[Serializable]
		public class Builder_Message
		{
			[XmlAttribute]
			public long DestCubeBlock, SourceCubeBlock;
			public string Content, SourceGridName, SourceBlockName;
			public SerializableGameTime created;
			public long destOwnerID;

			public override bool Equals(object obj)
			{
				return this == obj || GetHashCode() == obj.GetHashCode();
			}

			public override int GetHashCode()
			{
				return (DestCubeBlock + SourceCubeBlock + created.Value).GetHashCode();
			}

		}

		private class StaticVariables
		{
			public readonly TimeSpan MaximumLifetime = new TimeSpan(1, 0, 0);
			public readonly List<byte> bytes = new List<byte>();
		}

		private static StaticVariables Static = new StaticVariables();

		static Message()
		{
			MessageHandler.AddHandler(MessageHandler.SubMod.Message, FromClient);
		}

		/// <summary>
		/// Message needs to be explicitly initialized because there may be none in the world.
		/// </summary>
		[OnWorldLoad]
		private static void Init() { }

		#region Create & Send

		/// <summary>
		/// Inform the server to send message to autopilot block.
		/// </summary>
		private static void ToServer(long sender, string recipientGrid, string recipientBlock, string message)
		{
			Static.bytes.Clear();

			ByteConverter.AppendBytes(Static.bytes, MessageHandler.SubMod.Message);
			ByteConverter.AppendBytes(Static.bytes, sender);
			ByteConverter.AppendBytes(Static.bytes, recipientGrid);
			ByteConverter.AppendBytes(Static.bytes, recipientBlock);
			ByteConverter.AppendBytes(Static.bytes, message);

			if (MyAPIGateway.Multiplayer.TrySendMessageToServer(Static.bytes.ToArray()))
				Logger.DebugLog("Sent message to server");
			else
			{
				Logger.AlwaysLog("Message too long", Logger.severity.WARNING);

				IMyEntity entity;
				if (MyAPIGateway.Entities.TryGetEntityById(sender, out entity))
				{
					IMyTerminalBlock block = entity as IMyTerminalBlock;
					if (block != null)
						block.AppendCustomInfo("Failed to send message:\nMessage too long (" + Static.bytes.Count + " > 4096 bytes)\n");
				}
			}
		}

		private static void FromClient(byte[] bytes, int pos)
		{
			if (!MyAPIGateway.Multiplayer.IsServer)
			{
				Logger.AlwaysLog("Not the server!", Logger.severity.WARNING);
				return;
			}

			long sender = ByteConverter.GetLong(bytes, ref pos);
			string recipientGrid = ByteConverter.GetString(bytes, ref pos);
			string recipientBlock = ByteConverter.GetString(bytes, ref pos);
			string message = ByteConverter.GetString(bytes, ref pos);

			CreateAndSendMessage_Autopilot(sender, recipientGrid, recipientBlock, message);
		}

		private static void GetStorage(long entityId, out IMyCubeBlock block, out RelayStorage storage)
		{
			IMyEntity entity;
			if (!MyAPIGateway.Entities.TryGetEntityById(entityId, out entity))
			{
				Logger.AlwaysLog("Failed to get entity id for " + entityId, Logger.severity.WARNING);
				block = null;
				storage = null;
				return;
			}

			block = entity as IMyCubeBlock;
			if (block == null)
			{
				Logger.AlwaysLog("Entity is not a block: " + entityId, Logger.severity.WARNING);
				storage = null;
				return;
			}

			storage = RelayClient.GetOrCreateRelayPart(block).GetStorage();
		}

		/// <summary>
		/// Creates a message and sends it.
		/// </summary>
		/// <param name="sender">Block sending the message.</param>
		/// <param name="recipientGrid">Grid that will receive the message.</param>
		/// <param name="recipientBlock">Block that will receive the message.</param>
		/// <param name="message">The content of the message.</param>
		/// <returns>The number of blocks that will receive the message.</returns>
		public static int CreateAndSendMessage(long sender, string recipientGrid, string recipientBlock, string message)
		{
			Logger.DebugLog("sender: " + sender + ", recipientGrid: " + recipientGrid + ", recipientBlock: " + recipientBlock + ", message: " + message);

			int count =  CreateAndSendMessage_Autopilot(sender, recipientGrid, recipientBlock, message);
			if (count != 0 && !MyAPIGateway.Multiplayer.IsServer)
				ToServer(sender, recipientGrid, recipientBlock, message);

			count += CreateAndSendMessage_Program(sender, recipientGrid, recipientBlock, message);

			return count;
		}

		private static int CreateAndSendMessage_Autopilot(long sender, string recipientGrid, string recipientBlock, string message)
		{
			int count = 0;

			IMyCubeBlock senderBlock;
			RelayStorage storage;
			GetStorage(sender, out senderBlock, out storage);

			if (storage == null)
			{
				Logger.DebugLog("No storage");
				return 0;
			}

			Registrar.ForEach((ShipAutopilot autopilot) => {
				IMyCubeBlock block = autopilot.m_block.CubeBlock;
				IMyCubeGrid grid = block.CubeGrid;
				if (senderBlock.canControlBlock(block) && grid.DisplayName.looseContains(recipientGrid) && block.DisplayNameText.looseContains(recipientBlock))
				{
					count++;
					if (MyAPIGateway.Multiplayer.IsServer)
						storage.Receive(new Message(message, block, senderBlock));
				}
			});

			return count;
		}

		private static int CreateAndSendMessage_Program(long sender, string recipientGrid, string recipientBlock, string message)
		{
			int count = 0;

			IMyCubeBlock senderBlock;
			RelayStorage storage;
			GetStorage(sender, out senderBlock, out storage);

			if (storage == null)
			{
				Logger.DebugLog("No storage");
				return 0;
			}

			Registrar.ForEach((ProgrammableBlock pb) => {
				IMyCubeBlock block = pb.m_block;
				IMyCubeGrid grid = block.CubeGrid;
				if (senderBlock.canControlBlock(block) && grid.DisplayName.looseContains(recipientGrid) && block.DisplayNameText.looseContains(recipientBlock))
				{
					count++;
					storage.Receive(new Message(message, block, senderBlock));
				}
			});

			return count;
		}

		#endregion Create & Send

		public readonly string Content, SourceGridName, SourceBlockName;
		public readonly IMyCubeBlock DestCubeBlock, SourceCubeBlock;
		public readonly TimeSpan created;
		private readonly long destOwnerID;

		private Message(string Content, IMyCubeBlock DestCubeblock, IMyCubeBlock SourceCubeBlock, string SourceBlockName = null)
		{
			this.Content = Content;
			this.DestCubeBlock = DestCubeblock;

			this.SourceCubeBlock = SourceCubeBlock;
			this.SourceGridName = SourceCubeBlock.CubeGrid.DisplayName;
			if (SourceBlockName == null)
				this.SourceBlockName = SourceCubeBlock.DisplayNameText;
			else
				this.SourceBlockName = SourceBlockName;
			this.destOwnerID = DestCubeblock.OwnerId;

			created = Globals.ElapsedTime;
		}

		public Message(Builder_Message builder)
		{
			this.Content = builder.Content;
			this.SourceGridName = builder.SourceGridName;
			this.SourceBlockName = builder.SourceBlockName;

			IMyEntity entity;
			if (!MyAPIGateway.Entities.TryGetEntityById(builder.DestCubeBlock, out entity) || !(entity is IMyCubeBlock))
			{
				Logger.AlwaysLog("Entity does not exist in world: " + builder.DestCubeBlock, Logger.severity.WARNING);
				return;
			}
			this.DestCubeBlock = (IMyCubeBlock)entity;
			if (!MyAPIGateway.Entities.TryGetEntityById(builder.SourceCubeBlock, out entity) || !(entity is IMyCubeBlock))
			{
				Logger.AlwaysLog("Entity does not exist in world: " + builder.SourceCubeBlock, Logger.severity.WARNING);
				return;
			}
			this.SourceCubeBlock = (IMyCubeBlock)entity;

			this.created = builder.created.ToTimeSpan();
			this.destOwnerID = builder.destOwnerID;
		}

		private bool value_isValid = true;
		/// <summary>
		/// can only be set to false, once invalid always invalid
		/// </summary>
		public bool IsValid
		{
			get
			{
				if (value_isValid &&
					(DestCubeBlock == null
					|| SourceCubeBlock == null
					|| DestCubeBlock.Closed
					|| destOwnerID != DestCubeBlock.OwnerId // dest owner changed
					|| (Globals.ElapsedTime - created).CompareTo(Static.MaximumLifetime) > 0)) // expired
					value_isValid = false;
				return value_isValid;
			}
			set
			{
				if (value == false)
					value_isValid = false;
			}
		}

		public Builder_Message GetBuilder()
		{
			return new Builder_Message()
			{
				DestCubeBlock = DestCubeBlock.EntityId,
				SourceCubeBlock = SourceCubeBlock.EntityId,
				created = new SerializableGameTime(created),
				destOwnerID = destOwnerID,
				Content = Content,
				SourceGridName = SourceGridName,
				SourceBlockName = SourceBlockName
			};
		}

	}
}
