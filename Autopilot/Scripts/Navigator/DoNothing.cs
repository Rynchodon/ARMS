using System.Text;

namespace Rynchodon.Autopilot.Navigator
{
	public class DoNothing : INavigatorMover, INavigatorRotator
	{

		public void Move() { }

		public void Rotate() { }

		public void AppendCustomInfo(StringBuilder customInfo) { }

	}
}
