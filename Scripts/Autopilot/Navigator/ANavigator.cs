using System.Text;
using Rynchodon.AntennaRelay;
using Rynchodon.Autopilot.Data;
using Rynchodon.Autopilot.Movement;
using Rynchodon.Autopilot.Pathfinding;
using VRage.Game.ModAPI;

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
		/// <summary>The Pathfinder this navigator is using.</summary>
		protected readonly Pathfinder m_pathfinder;
		/// <summary>The Mover this navigator is using</summary>
		protected Mover m_mover { get { return m_pathfinder.Mover; } }
		/// <summary>The settings this navigator is using.</summary>
		protected AllNavigationSettings m_navSet { get { return m_pathfinder.Mover.NavSet; } }
		/// <summary>The ship controller the mover is using.</summary>
		protected ShipControllerBlock m_controlBlock { get { return m_pathfinder.Mover.Block; } }
		/// <summary>Current navigation block</summary>
		protected PseudoBlock m_navBlock { get { return m_navSet.Settings_Current.NavigationBlock; } }
		/// <summary>Grid the ship controller is on</summary>
		protected IMyCubeGrid m_grid { get { return m_pathfinder.Mover.Block.CubeGrid; } }

		/// <summary>
		/// Sets m_pathfinder and m_navSet for the navigator.
		/// </summary>
		/// <param name="pathfinder">The Pathfinder to use</param>
		protected ANavigator(Pathfinder pathfinder)
		{
			this.m_pathfinder = pathfinder;
		}

	}

	public abstract class NavigatorMover : ANavigator, INavigatorMover
	{
		/// <summary>
		/// Sets m_pathfinder and m_navSet for the navigator.
		/// </summary>
		/// <param name="pathfinder">The Pathfinder to use</param>
		protected NavigatorMover(Pathfinder pathfinder)
			: base(pathfinder) { }

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
		/// Sets m_pathfinder and m_navSet for the navigator.
		/// </summary>
		/// <param name="pathfinder">The Pathfinder to use</param>
		protected NavigatorRotator(Pathfinder pathfinder)
			: base(pathfinder) { }

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
