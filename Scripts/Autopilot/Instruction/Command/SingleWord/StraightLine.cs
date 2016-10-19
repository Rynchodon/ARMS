using Rynchodon.Autopilot.Navigator;
using Rynchodon.Autopilot.Pathfinding;

namespace Rynchodon.Autopilot.Instruction.Command
{
	public class StraightLine : ASingleWord
	{
		protected override void ActionMethod(Pathfinder pathfinder)
		{
			pathfinder.NavSet.Settings_Task_NavMove.PathfinderCanChangeCourse = false;
			pathfinder.NavSet.Settings_Task_NavMove.NavigatorRotator = new DoNothing();
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
