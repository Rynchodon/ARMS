using System.Text;
using Rynchodon.Autopilot.Pathfinding;
using VRageMath;

namespace Rynchodon.Autopilot.Navigator
{
	/// <summary>
	/// Face navigation block's forward in the direction the ship is currently moving in.
	/// </summary>
	public class FaceMove : NavigatorRotator
	{
		public FaceMove(Pathfinder pathfinder) : base(pathfinder)
		{
			m_navSet.Settings_Task_NavRot.NavigatorRotator = this;
		}

		public override void AppendCustomInfo(StringBuilder customInfo)
		{
			customInfo.AppendLine("Facing " + m_navBlock.DisplayName + " in direction of movement");
		}

		public override void Rotate()
		{
			Vector3 linearVelocity = m_grid.Physics.LinearVelocity;
			float speedSquared = linearVelocity.LengthSquared();
			if (speedSquared < 1f)
			{
				m_mover.CalcRotate();
				m_navSet.Settings_Current.DistanceAngle = 0f;
			}
			else
				m_mover.CalcRotate(m_navBlock, RelativeDirection3F.FromWorld(m_grid, linearVelocity));
		}
	}
}
