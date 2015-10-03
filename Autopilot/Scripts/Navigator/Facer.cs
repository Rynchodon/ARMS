using System;
using System.Text;
using Rynchodon.Autopilot.Data;
using Rynchodon.Autopilot.Movement;
using Sandbox.ModAPI;
using VRageMath;
using Ingame = Sandbox.ModAPI.Ingame;

namespace Rynchodon.Autopilot.Navigator
{
	/// <summary>
	/// Faces the sun, or laser antenna's target.
	/// </summary>
	public class Facer : NavigatorRotator
	{

		private readonly Logger m_logger;
		private readonly IMyCubeBlock m_rotBlock;
		private readonly IMyLaserAntenna m_laser;
		private readonly Base6Directions.Direction m_blockDir;

		public Facer(Mover mover, AllNavigationSettings navSet, IMyCubeBlock rotBlock, Base6Directions.Direction? blockDirection = null)
			: base(mover, navSet)
		{
			this.m_logger = new Logger("Facer", m_controlBlock.CubeBlock);

			this.m_rotBlock = rotBlock;
			this.m_laser = rotBlock as IMyLaserAntenna;
			if (this.m_laser == null)
			{
				if (rotBlock is Ingame.IMySolarPanel || rotBlock is Ingame.IMyOxygenFarm)
					this.m_rotBlock = rotBlock;
				else
				{
					m_logger.alwaysLog("Block is of wrong type: " + rotBlock.DisplayNameText, "Facer()", Logger.severity.FATAL);
					throw new Exception("Block is of wrong type: " + rotBlock.DisplayNameText);
				}
			}

			if (blockDirection.HasValue)
				this.m_blockDir = blockDirection.Value;
			else
				this.m_blockDir = rotBlock.GetFaceDirection(TargetDirection());

			_navSet.Settings_Task_Primary.NavigatorRotator = this;
			m_logger.debugLog("created Facer, rotBlock: " + rotBlock.DisplayNameText + ", direction: " + m_blockDir, "Facer()");
		}

		public override void Rotate()
		{
			_mover.CalcRotate(m_rotBlock, RelativeDirection3F.FromWorld(m_controlBlock.CubeGrid, TargetDirection()));
		}

		public override void AppendCustomInfo(StringBuilder customInfo)
		{
			customInfo.Append("Facing ");
			customInfo.Append(m_rotBlock.DisplayNameText);
			customInfo.Append(" towards ");
			if (m_laser != null)
				customInfo.AppendLine(m_laser.TargetCoords.ToString());
			else
				customInfo.AppendLine("Sun");
		}

		private Vector3 TargetDirection()
		{
			if (m_laser != null)
				return m_laser.TargetCoords - m_laser.GetPosition();
			return SunProperties.SunDirection;
		}

	}
}
