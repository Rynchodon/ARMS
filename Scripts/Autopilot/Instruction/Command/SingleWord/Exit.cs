using Rynchodon.Autopilot.Navigator;
using Rynchodon.Autopilot.Pathfinding;

namespace Rynchodon.Autopilot.Instruction.Command
{
	public class Exit : ASingleWord
	{

		public override ACommand Clone()
		{
			return new Exit();
		}

		public override string Identifier
		{
			get { return "exit"; }
		}

		public override string AddDescription
		{
			get { return "Stop the ship and disable autopilot"; }
		}

		protected override void ActionMethod(Pathfinder pathfinder)
		{
			new Stopper(pathfinder, true);
		}

	}
}
