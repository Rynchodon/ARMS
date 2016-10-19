using Rynchodon.Autopilot.Pathfinding;

namespace Rynchodon.Autopilot.Instruction.Command
{
	public class Asteroid : ASingleWord
	{

		public override ACommand Clone()
		{
			return new Asteroid();
		}

		public override string Identifier
		{
			get { return "asteroid"; }
		}

		public override string AddDescription
		{
			get { return "Ignore asteroids until next destination."; }
		}

		protected override void ActionMethod(Pathfinder pathfinder)
		{
			pathfinder.Mover.NavSet.Settings_Task_NavMove.IgnoreAsteroid = true;
		}

	}
}
