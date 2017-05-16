using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Rynchodon.Autopilot.Data;
using VRageMath;

namespace Rynchodon.Autopilot.Movement
{
	public interface IMover
	{

		string ReportName { get; }

		void SetMoveDestination(PseudoBlock navBlock, ref Vector3 destDirection, float destDistance, ref Vector3 destVelocity, bool isLanding = false);

		void Calculate(out Vector3 moveForceRatio);

	}
}
