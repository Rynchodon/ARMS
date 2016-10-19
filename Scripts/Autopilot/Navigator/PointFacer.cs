using System.Text;
using Rynchodon.Autopilot.Data;
using Rynchodon.Autopilot.Pathfinding;
using VRageMath;

namespace Rynchodon.Autopilot.Navigator
{
	public class PointFacer : NavigatorRotator
	{

		private readonly Logger m_logger;
		private readonly PseudoBlock m_navBlock;
		private readonly Destination m_destination;

		public PointFacer(Pathfinder pathfinder, PseudoBlock rotBlock, Destination destination)
			: base(pathfinder)
		{
			this.m_navBlock = rotBlock;
			this.m_destination = destination;
		}

		public override void Rotate()
		{
			Vector3D destWorld = m_destination.WorldPosition();
			Vector3D currentPosition = m_navBlock.WorldPosition;
			Vector3D direction; Vector3D.Subtract(ref destWorld, ref currentPosition, out direction);
			direction.Normalize();

			m_mover.CalcRotate(m_navBlock, RelativeDirection3F.FromWorld(m_navBlock.Grid, direction));
		}

		public override void AppendCustomInfo(StringBuilder customInfo)
		{
			//customInfo.Append("Facing ");
			//customInfo.Append(m_navBlock.Block.DisplayNameText);
			//customInfo.Append(" towards ");

		}

	}
}
