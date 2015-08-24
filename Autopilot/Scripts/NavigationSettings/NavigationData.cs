using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Rynchodon.Autopilot.NavigationSettings
{
	public class NavigationData
	{
		public enum Moving : byte { NOT_MOVE, MOVING, STOP_MOVE, SIDELING, HYBRID }
		public enum Rotating : byte { NOT_ROTA, ROTATING, STOP_ROTA }
		public enum Rolling : byte { NOT_ROLL, ROLLING, STOP_ROLL }
	}
}
