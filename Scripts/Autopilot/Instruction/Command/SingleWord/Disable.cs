using System;
using System.Collections.Generic;
using Rynchodon.Autopilot.Movement;
using Sandbox.ModAPI.Interfaces.Terminal;

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

		protected override void ActionMethod(Mover mover)
		{
			mover.SetControl(false);
		}

	}
}
