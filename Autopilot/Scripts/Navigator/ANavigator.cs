using System.Text;
using Rynchodon.Autopilot.Data;
using Rynchodon.Autopilot.Movement;

namespace Rynchodon.Autopilot.Navigator
{
	public abstract class ANavigator
	{
		protected readonly Mover _mover;
		protected readonly AllNavigationSettings _navSet;

		protected ShipControllerBlock _block { get { return _mover.Block; } }

		protected ANavigator(Mover mover, AllNavigationSettings navSet)
		{
			this._mover = mover;
			this._navSet = navSet;
		}

		public abstract void PerformTask();
		public abstract void AppendCustomInfo(StringBuilder customInfo);
	}
}
