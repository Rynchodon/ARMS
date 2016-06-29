
namespace Rynchodon.Autopilot.Instruction.Command
{
	public class Form : SingleWord
	{
		protected override void Action(Movement.Mover mover)
		{
			mover.m_navSet.Settings_Task_NavMove.Stay_In_Formation = true;
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
