using Rynchodon.Autopilot.Pathfinding;

namespace Rynchodon.Autopilot.Instruction.Command
{
	public class Disable : ASingleWord
	{

		public override ACommand Clone()
		{
			return new Disable();
		}

		public override string Identifier
		{
			get { return "disable"; }
		}

		public override string AddDescription
		{
			get { return "Disable autopilot without stopping"; }
		}

		protected override void ActionMethod(Pathfinder pathfinder)
		{
			pathfinder.Mover.SetControl(false);
		}

	}
}
