using Rynchodon.Autopilot.Pathfinding;

namespace Rynchodon.Autopilot.Instruction.Command
{
	public class FaceMove : ASingleWord
	{
		public override ACommand Clone()
		{
			return new FaceMove();
		}

		public override string AddName
		{
			get { return "Face Move Direction"; }
		}

		public override string Identifier
		{
			get { return "facemove"; }
		}

		public override string AddDescription
		{
			get { return "Face the navigation block in the direction the ship is moving in.\nCleared by STOP or any command that needs to control rotation."; }
		}

		protected override void ActionMethod(Pathfinder pathfinder)
		{
			new Navigator.FaceMove(pathfinder);
		}
	}
}
