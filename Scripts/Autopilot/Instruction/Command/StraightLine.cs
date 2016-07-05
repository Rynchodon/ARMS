using Rynchodon.Autopilot.Navigator;

namespace Rynchodon.Autopilot.Instruction.Command
{
	public class StraightLine : SingleWord
	{
		protected override void Action(Movement.Mover mover)
		{
			mover.NavSet.Settings_Task_NavMove.PathfinderCanChangeCourse = false;
			mover.NavSet.Settings_Task_NavMove.NavigatorRotator = new DoNothing();
		}

		public override ACommand Clone()
		{
			return new StraightLine();
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
