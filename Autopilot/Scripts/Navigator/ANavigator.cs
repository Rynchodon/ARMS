using System.Text;
using Rynchodon.AntennaRelay;
using Rynchodon.Autopilot.Data;
using Rynchodon.Autopilot.Movement;
using Sandbox.ModAPI;

namespace Rynchodon.Autopilot.Navigator
{

	/// <summary>
	/// Interface that all navigators that move the grid must implement.
	/// </summary>
	public interface INavigatorMover
	{
		/// <summary>
		/// Calculate the movement force necessary to reach the target.
		/// </summary>
		void Move();
		/// <summary>
		/// Appends navigator mover's status to customInfo 
		/// </summary>
		/// <param name="customInfo">The autopilot block's custom info</param>
		void AppendCustomInfo(StringBuilder customInfo);
	}

	/// <summary>
	/// Interface that all navigators that rotate the grid must implement.
	/// </summary>
	public interface INavigatorRotator
	{
		/// <summary>
		/// Calculate the angular force necessary to reach the target direction.
		/// </summary>
		void Rotate();
		/// <summary>
		/// Appends navigator rotator's status to customInfo
		/// </summary>
		/// <param name="customInfo">The autopilot block's custom info</param>
		void AppendCustomInfo(StringBuilder customInfo);
	}

	public interface IEnemyResponse : INavigatorMover, INavigatorRotator
	{
		bool CanRespond();
		bool CanTarget(IMyCubeGrid grid);
		void UpdateTarget(LastSeen enemy);
	}

	public abstract class ANavigator
	{
		/// <summary>The Mover this navigator is using.</summary>
		protected readonly Mover m_mover;
		/// <summary>The settings this navigator is using.</summary>
		protected readonly AllNavigationSettings m_navSet;

		/// <summary>The ship controller the mover is using.</summary>
		protected ShipControllerBlock m_controlBlock { get { return m_mover.Block; } }

		/// <summary>
		/// Sets m_mover and m_navSet for the navigator.
		/// </summary>
		/// <param name="mover">The Mover to use</param>
		/// <param name="navSet">The settings to use</param>
		protected ANavigator(Mover mover, AllNavigationSettings navSet)
		{
			this.m_mover = mover;
			this.m_navSet = navSet;
		}

	}

	public abstract class NavigatorMover : ANavigator, INavigatorMover
	{
		/// <summary>
		/// Sets m_mover and m_navSet for the navigator.
		/// </summary>
		/// <param name="mover">The Mover to use</param>
		/// <param name="navSet">The settings to use</param>
		protected NavigatorMover(Mover mover, AllNavigationSettings navSet)
			: base(mover, navSet) { }

		/// <summary>
		/// Calculate the movement force necessary to reach the target.
		/// </summary>
		public abstract void Move();
		/// <summary>
		/// Appends navigator mover's status to customInfo 
		/// </summary>
		/// <param name="customInfo">The autopilot block's custom info</param>
		public abstract void AppendCustomInfo(StringBuilder customInfo);
	}

	public abstract class NavigatorRotator : ANavigator, INavigatorRotator
	{
		/// <summary>
		/// Sets m_mover and m_navSet for the navigator.
		/// </summary>
		/// <param name="mover">The Mover to use</param>
		/// <param name="navSet">The settings to use</param>
		protected NavigatorRotator(Mover mover, AllNavigationSettings navSet)
			: base(mover, navSet) { }

		/// <summary>
		/// Calculate the angular force necessary to reach the target direction.
		/// </summary>
		public abstract void Rotate();
		/// <summary>
		/// Appends navigator rotator's status to customInfo
		/// </summary>
		/// <param name="customInfo">The autopilot block's custom info</param>
		public abstract void AppendCustomInfo(StringBuilder customInfo);
	}

}
