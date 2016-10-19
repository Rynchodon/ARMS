using Rynchodon.Autopilot.Navigator;
using Rynchodon.Autopilot.Pathfinding;

namespace Rynchodon.Autopilot.Instruction.Command
{
	public class Stop : ASingleWord
	{

		public override ACommand Clone()
		{
			return new Stop();
		}

		public override string Identifier
		{
			get { return "stop"; }
		}

		public override string AddDescription
		{
			get { return "Stop the ship before continuing"; }
		}

		protected override void ActionMethod(Pathfinder pathfinder)
		{
			new Stopper(pathfinder);
		}

	}
}
