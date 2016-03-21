using System.Text; // from mscorlib.dll
using Rynchodon.Autopilot.Data; // from VRage.Math.dll

namespace Rynchodon.Autopilot.Navigator
{
	/// <summary>
	/// Navigator that complains about something for a little while, then terminates.
	/// </summary>
	public class Complainer : INavigatorMover
	{

		private readonly AllNavigationSettings m_navSet;
		private readonly ulong m_complainUntil;
		private readonly string m_message;

		public Complainer(AllNavigationSettings navSet, ulong updates, string msg)
		{
			this.m_navSet = navSet;
			this.m_complainUntil = Globals.UpdateCount + updates;
			this.m_message = msg;

			this.m_navSet.Settings_Task_NavWay.NavigatorMover = this;
		}

		public Complainer(AllNavigationSettings navSet, int seconds, string msg)
			: this(navSet, (ulong)(seconds * Globals.UpdatesPerSecond), msg) { }

		public void Move()
		{
			if (Globals.UpdateCount >= m_complainUntil)
				m_navSet.OnTaskComplete_NavWay();
		}

		public void AppendCustomInfo(StringBuilder customInfo)
		{
			customInfo.AppendLine(m_message);
		}

	}
}
