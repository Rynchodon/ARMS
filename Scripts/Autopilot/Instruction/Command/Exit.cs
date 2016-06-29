using System;
using Rynchodon.Autopilot.Movement;
using Rynchodon.Autopilot.Navigator;

namespace Rynchodon.Autopilot.Instruction.Command
{
	public class Exit : SingleWord
	{

		public override ACommand Clone()
		{
			return new Exit();
		}

		public override string Identifier
		{
			get { return "Exit"; }
		}

		public override string AddDescription
		{
			get { return "Stop the ship and disable autopilot"; }
		}

		protected override void Action(Mover mover)
		{
			new Stopper(mover, true);
		}

	}
}
