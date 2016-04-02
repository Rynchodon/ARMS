using System;
using System.Collections.Generic;
using System.Xml.Serialization;
using Rynchodon.Update;
using Sandbox.Common.ObjectBuilders;
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
			// may contain some gnarly characters
			public byte[] Content, SourceGridName, SourceBlockName;
			public SerializableGameTime created;
			public long destOwnerID;
		}

		private static readonly TimeSpan MaximumLifetime = new TimeSpan(1, 0, 0); // one hour

		public readonly string Content, SourceGridName, SourceBlockName;
		public readonly IMyCubeBlock DestCubeBlock, SourceCubeBlock;
		public readonly TimeSpan created;
		private readonly long destOwnerID;

		public Message(string Content, IMyCubeBlock DestCubeblock, IMyCubeBlock SourceCubeBlock, string SourceBlockName = null)
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
			this.Content = ByteConverter.GetString(builder.Content);
			this.SourceGridName = ByteConverter.GetString(builder.SourceGridName);
			this.SourceBlockName = ByteConverter.GetString(builder.SourceBlockName);

			IMyEntity entity;
			if (!MyAPIGateway.Entities.TryGetEntityById(builder.DestCubeBlock, out entity) || !(entity is IMyCubeBlock))
			{
				(new Logger(GetType().Name)).alwaysLog("Entity does not exist in world: " + builder.DestCubeBlock, "LastSeen()", Logger.severity.WARNING);
				return;
			}
			this.DestCubeBlock = (IMyCubeBlock)entity;
			if (!MyAPIGateway.Entities.TryGetEntityById(builder.SourceCubeBlock, out entity) || !(entity is IMyCubeBlock))
			{
				(new Logger(GetType().Name)).alwaysLog("Entity does not exist in world: " + builder.SourceCubeBlock, "LastSeen()", Logger.severity.WARNING);
				return;
			}
			this.SourceCubeBlock = (IMyCubeBlock)entity;

			this.created = builder.created.ToTimeSpan();
			this.destOwnerID = builder.destOwnerID;
		}

		public static List<Message> buildMessages(string Content, string DestGridName, string DestBlockName, IMyCubeBlock SourceCubeBlock, string SourceBlockName = null)
		{
			List<Message> result = new List<Message>();
			HashSet<IMyEntity> matchingGrids = new HashSet<IMyEntity>();
			MyAPIGateway.Entities.GetEntities_Safe(matchingGrids, ent => ent is IMyCubeGrid && ent.DisplayName.looseContains(DestGridName));
			foreach (IMyCubeGrid grid in matchingGrids)
			{
				var progs = CubeGridCache.GetFor(grid).GetBlocksOfType(typeof(MyObjectBuilder_MyProgrammableBlock));
				foreach (IMyCubeBlock block in progs)
				{
					if (block.DisplayNameText.looseContains(DestBlockName)
						&& SourceCubeBlock.canControlBlock(block))
						result.Add(new Message(Content, block, SourceCubeBlock, SourceBlockName));
				}
			}
			return result;
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
					|| (Globals.ElapsedTime - created).CompareTo(MaximumLifetime) > 0)) // expired
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
			Builder_Message result = new Builder_Message()
			{
				DestCubeBlock = DestCubeBlock.EntityId,
				SourceCubeBlock = SourceCubeBlock.EntityId,
				created = new SerializableGameTime(created),
				destOwnerID = destOwnerID
			};

			List<byte> bytes = new List<byte>(Content.Length * 2);
			ByteConverter.AppendBytes(bytes, Content);
			result.Content = bytes.ToArray();

			bytes.Clear();
			ByteConverter.AppendBytes(bytes, SourceGridName);
			result.SourceGridName = bytes.ToArray();

			bytes.Clear();
			ByteConverter.AppendBytes(bytes, SourceBlockName);
			result.SourceBlockName = bytes.ToArray();

			return result;
		}

	}
}
