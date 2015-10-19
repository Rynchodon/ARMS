using System;
using System.Collections.Generic;
using Sandbox.Common.ObjectBuilders;
using Sandbox.ModAPI;
using VRage.ModAPI;

namespace Rynchodon.AntennaRelay
{
	public class Message
	{
		private static readonly TimeSpan MaximumLifetime = new TimeSpan(1, 0, 0); // one hour

		public readonly string Content, SourceGridName, SourceBlockName;
		public readonly IMyCubeBlock DestCubeBlock, SourceCubeBlock;
		public readonly DateTime created;
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

			created = DateTime.UtcNow;
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
				if (value_isValid && (DestCubeBlock == null
					|| DestCubeBlock.Closed
					|| destOwnerID != DestCubeBlock.OwnerId // dest owner changed
					|| (DateTime.UtcNow - created).CompareTo(MaximumLifetime) > 0)) // expired
					value_isValid = false;
				return value_isValid;
			}
			set
			{
				if (value == false)
					value_isValid = false;
			}
		}
	}
}
