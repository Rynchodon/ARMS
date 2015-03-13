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
		public readonly string Transmission, DestinationGrid, DestinationBlock;
		public readonly IMyTerminalBlock SourceBlock;
		public IMyCubeGrid SourceGrid;

		public Message(IMyTerminalBlock source, string destGrid, string destBlock, string message)
		{
			this.SourceBlock = source;
			this.SourceGrid = (source as IMyCubeBlock).CubeGrid;
			this.DestinationGrid = destGrid;
			this.DestinationBlock = destBlock;
			this.Transmission = message;
		}
	}
}
