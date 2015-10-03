using System.Text;
using Rynchodon.Autopilot.Data;
using Rynchodon.Autopilot.Movement;

namespace Rynchodon.Autopilot.Navigator
{

	public interface INavigatorMover
	{
		void Move();
		void AppendCustomInfo(StringBuilder customInfo);
	}

	public interface INavigatorRotator
	{
		void Rotate();
		void AppendCustomInfo(StringBuilder customInfo);
	}

	public abstract class ANavigator
	{
		protected readonly Mover _mover;
		protected readonly AllNavigationSettings _navSet;

		protected ShipControllerBlock m_controlBlock { get { return _mover.Block; } }

		protected ANavigator(Mover mover, AllNavigationSettings navSet)
		{
			this._mover = mover;
			this._navSet = navSet;
		}
	}

	public abstract class NavigatorMover : ANavigator, INavigatorMover
	{
		protected NavigatorMover(Mover mover, AllNavigationSettings navSet)
			: base(mover, navSet) { }

		public abstract void Move();
		public abstract void AppendCustomInfo(StringBuilder customInfo);
	}

	public abstract class NavigatorRotator : ANavigator, INavigatorRotator
	{
		protected NavigatorRotator(Mover mover, AllNavigationSettings navSet)
			: base(mover, navSet) { }

		public abstract void Rotate();
		public abstract void AppendCustomInfo(StringBuilder customInfo);
	}

}
