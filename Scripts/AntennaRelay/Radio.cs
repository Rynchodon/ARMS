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
	public class Radio : NetworkNode
	{

		private readonly Logger m_logger;
		private readonly Func<float> m_radius;

		public readonly IMyEntity Entity;

		public Radio(IMyEntity entity)
			:base(entity)
		{
			this.m_logger = new Logger(GetType().Name, entity);
			this.Entity = entity;
			this.m_testConn = TestRadioConnection;
			Registrar.Add(entity, this);
		}

		private bool TestRadioConnection(NetworkNode node)
		{
			Radio other = node as Radio;
			if (other == null)
				return false;


		}

	}
}
