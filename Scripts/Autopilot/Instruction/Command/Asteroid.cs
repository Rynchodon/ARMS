
namespace Rynchodon.Autopilot.Instruction.Command
{
	public class Asteroid : SingleWord
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

		protected override void Action(Movement.Mover mover)
		{
			mover.NavSet.Settings_Task_NavMove.IgnoreAsteroid = true;
		}

	}
}
