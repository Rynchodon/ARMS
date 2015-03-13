#define LOG_ENABLED // remove on build

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

using Sandbox.Common;
using Sandbox.Common.Components;
using Sandbox.Common.ObjectBuilders;
using Sandbox.Definitions;
using Sandbox.Engine;
using Sandbox.Game;
using Sandbox.ModAPI;
using Ingame = Sandbox.ModAPI.Ingame;
using Sandbox.ModAPI.Interfaces;
using VRageMath;

namespace Rynchodon.BlockCommunication
{
	public class Message
	{
		public readonly string Transmission, DestinationGrid, DestinationBlock, SourceBlock, SourceGrid;

		public Message(string sourceGrid, string sourceBlock, string destGrid, string destBlock, string message)
		{
			this.SourceBlock = sourceBlock;
			this.SourceGrid = sourceBlock;
			this.DestinationGrid = destGrid;
			this.DestinationBlock = destBlock;
			this.Transmission = message;
		}
	}
}
