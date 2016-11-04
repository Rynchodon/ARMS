using System;
using System.Text;
using Rynchodon.Autopilot.Pathfinding;

namespace Rynchodon.Autopilot.Navigator
{
	/// <summary>
	/// Waits for a particular condition to be satisfied.
	/// </summary>
	public class WaitForCondition : NavigatorMover
	{
		private Func<bool> m_condition;
		private string m_customInfo;

		public WaitForCondition(Pathfinder pathfinder, Func<bool> condition, string customInfo) : base(pathfinder)
		{
			this.m_condition = condition;
			this.m_customInfo = customInfo;
			m_navSet.Settings_Task_NavMove.NavigatorMover = this;
		}

		public override void AppendCustomInfo(StringBuilder customInfo)
		{
			customInfo.AppendLine(m_customInfo);
		}

		public override void Move()
		{
			if (m_condition.Invoke())
				m_navSet.OnTaskComplete_NavMove();
		}

	}
}
