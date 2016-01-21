using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

using Sandbox.Common;
using Sandbox.Common.Components;
using Sandbox.Common.ObjectBuilders;
using Sandbox.Common.ObjectBuilders.Definitions;
using Sandbox.Common.ObjectBuilders.Voxels;
using Sandbox.Common.ObjectBuilders.VRageData;
using Sandbox.Definitions;
using Sandbox.Game;
using Sandbox.Game.Entities;
using Sandbox.Game.Gui;
using Sandbox.ModAPI;
using Sandbox.ModAPI.Interfaces;
using VRage;
using VRage.Collections;
using VRage.Game.Components;
using VRage.Game.ObjectBuilders;
using VRage.Library.Utils;
using VRage.ModAPI;
using VRage.ObjectBuilders;
using VRage.Utils;
using VRage.Voxels;
using VRageMath;
using Ingame = Sandbox.ModAPI.Ingame;

namespace Rynchodon.AntennaRelay
{
	public class new_RadioAntenna : NetworkBlock
	{

		private readonly Logger m_logger;
		private readonly Ingame.IMyRadioAntenna m_radio;

		public new_RadioAntenna(IMyCubeBlock block)
			: base(block)
		{
			this.m_logger = new Logger(GetType().Name, block);
			this.m_radio = block as Ingame.IMyRadioAntenna;
		}

		protected override bool TestConnectSpecial(NetworkNode other)
		{

		}

	}
}
