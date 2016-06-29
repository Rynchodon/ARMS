using System;
using System.Collections.Generic;
using Rynchodon.Autopilot.Movement;
using Sandbox.ModAPI.Interfaces.Terminal;

namespace Rynchodon.Autopilot.Instruction.Command
{
	public class Disable : SingleWord
	{

		public override ACommand Clone()
		{
			return new Disable();
		}

		public override string Identifier
		{
			get { return "Disable"; }
		}

		public override string AddDescription
		{
			get { return "Disable autopilot without stopping"; }
		}

		protected override void Action(Mover mover)
		{
			mover.SetControl(false);
		}

	}
}
