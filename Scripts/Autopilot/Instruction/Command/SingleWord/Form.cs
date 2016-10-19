using Rynchodon.Autopilot.Pathfinding;

namespace Rynchodon.Autopilot.Instruction.Command
{
	public class Form : ASingleWord
	{
		protected override void ActionMethod(Pathfinder pathfinder)
		{
			pathfinder.NavSet.Settings_Task_NavMove.Stay_In_Formation = true;
		}

		public override ACommand Clone()
		{
			return new Form();
		}

		public override string Identifier
		{
			get { return "form"; }
		}

		public override string AddDescription
		{
			get { return "Remain in formation after moving to a friendly ship."; }
		}
	}
}
