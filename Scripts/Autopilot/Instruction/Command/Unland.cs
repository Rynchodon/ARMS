using Rynchodon.Autopilot.Navigator;

namespace Rynchodon.Autopilot.Instruction.Command
{
	public class Unland : SingleWord
	{
		protected override void Action(Movement.Mover mover)
		{
			new UnLander(mover);
		}

		public override ACommand Clone()
		{
			return new Unland();
		}

		public override string Identifier
		{
			get { return "unland"; }
		}

		public override string AddName
		{
			get { return "Unland Last"; }
		}

		public override string AddDescription
		{
			get { return "Unlock the most recently landed block and move away from the attached entity."; }
		}

		public override string[] Aliases
		{
			get { return new string[] { "undock", "unlock" }; }
		}
	}
}
