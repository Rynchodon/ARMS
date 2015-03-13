#define LOG_ENABLED //remove on build

using System;
//using System.Collections.Generic;
//using System.Linq;
//using System.Text;

using Sandbox.ModAPI;

namespace Rynchodon.BlockCommunication
{
	internal class BlockComLogger : Logger
	{
		static BlockComLogger() { logFile = "BlockCommunication.log"; }

		internal BlockComLogger(string gridName, string className)
		{
			this.gridName = gridName;
			this.className = className;

			if (logWriter == null && MyAPIGateway.Utilities != null)
				logWriter = MyAPIGateway.Utilities.WriteFileInLocalStorage(logFile, typeof(Logger)); // was getting a NullReferenceException in ..cctor()
		}
	}
}
