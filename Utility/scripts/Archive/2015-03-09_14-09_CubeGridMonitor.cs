#define LOG_ENABLED //remove on build

using System;
using System.Collections.Generic;
//using System.Linq;
//using System.Text;

using Sandbox.Common;
using Sandbox.Common.Components;
using Sandbox.Common.ObjectBuilders;
using Sandbox.ModAPI;
using Ingame = Sandbox.ModAPI.Ingame;
using Sandbox.ModAPI.Interfaces;

namespace Rynchodon
{
	[MyEntityComponentDescriptor(typeof(MyObjectBuilder_CubeGrid))]
	public class CubeGridMonitor : UpdateEnforcer
	{
		protected override void DelayedInit()
		{
		}

		protected override void alwaysLog(string toLog, string method = null, Logger.severity level = Logger.severity.DEBUG)
		{
		}
	}
}
