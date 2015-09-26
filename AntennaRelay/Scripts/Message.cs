using System;
using System.Collections.Generic;
using Sandbox.ModAPI;

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
			foreach (IMyCubeBlock DestBlock in ProgrammableBlock.registry.Keys)
			{
				IMyCubeGrid DestGrid = DestBlock.CubeGrid;
				if (DestGrid.DisplayName.looseContains(DestGridName) // grid matches
					&& DestBlock.DisplayNameText.looseContains(DestBlockName)) // block matches
					if (SourceCubeBlock.canControlBlock(DestBlock)) // can control
						result.Add(new Message(Content, DestBlock, SourceCubeBlock, SourceBlockName));
			}
			return result;
		}

		private bool value_isValid = true;
		/// <summary>
		/// can only be set to false, once invalid always invalid
		/// </summary>
		public bool isValid
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
