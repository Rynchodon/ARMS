using System;
using Rynchodon.Autopilot.Movement;
using Rynchodon.Autopilot.Navigator;

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

		protected override void ActionMethod(Mover mover)
		{
			new Stopper(mover, true);
		}

	}
}
