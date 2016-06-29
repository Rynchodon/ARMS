using Rynchodon.Autopilot.Navigator;

namespace Rynchodon.Autopilot.Instruction.Command
{
	public class Line : SingleWord
	{
		protected override void Action(Movement.Mover mover)
		{
			mover.m_navSet.Settings_Task_NavMove.PathfinderCanChangeCourse = false;
			mover.m_navSet.Settings_Task_NavMove.NavigatorRotator = new DoNothing();
		}

		public override ACommand Clone()
		{
			return new Line();
		}

		public override string Identifier
		{
			get { return "line"; }
		}

		public override string AddDescription
		{
			get { return "Fly in a straight line, without rotating, to the next destination."; }
		}
	}
}
