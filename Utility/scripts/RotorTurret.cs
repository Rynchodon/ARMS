using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Sandbox.ModAPI;
using VRageMath;

namespace Rynchodon
{
	/// <summary>
	/// This class controls rotors for the purposes of pointing a block at a target. The turret does not have to be armed, it could be solar panels facing the sun, for example.
	/// </summary>
	public class RotorTurret
	{
		public IMyCubeBlock FaceBlock;

		private Logger myLogger;

		public RotorTurret(IMyCubeBlock block)
		{
			this.FaceBlock = block;
			this.myLogger = new Logger("RotorTurret", block);
		}

		
	}
}
