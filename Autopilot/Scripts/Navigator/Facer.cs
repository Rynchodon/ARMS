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
		private readonly PseudoBlock m_pseudoBlock;
		private readonly IMyLaserAntenna m_laser;

		/// <param name="mover">The mover to use</param>
		/// <param name="navSet">The settings to use</param>
		/// <param name="rotBlock">The block to rotate</param>
		public Facer(Mover mover, AllNavigationSettings navSet, PseudoBlock rotBlock)
			: base(mover, navSet)
		{
			this.m_logger = new Logger("Facer", m_controlBlock.CubeBlock);

			this.m_pseudoBlock = rotBlock;
			this.m_laser = rotBlock.Block as IMyLaserAntenna;
			if (this.m_laser == null)
			{
				if (!(rotBlock.Block is Ingame.IMySolarPanel) && !(rotBlock.Block is Ingame.IMyOxygenFarm))
				{
					m_logger.alwaysLog("Block is of wrong type: " + rotBlock.Block.DisplayNameText, "Facer()", Logger.severity.FATAL);
					throw new Exception("Block is of wrong type: " + rotBlock.Block.DisplayNameText);
				}
			}

			m_navSet.Settings_Task_NavRot.NavigatorRotator = this;
		}

		#region NavigatorRotator Members

		/// <summary>
		/// Calculates the rotation to face the rotation block towards the target.
		/// </summary>
		public override void Rotate()
		{
			m_mover.CalcRotate(m_pseudoBlock, RelativeDirection3F.FromWorld(m_pseudoBlock.Block.CubeGrid, TargetDirection()));
		}

		/// <summary>
		/// Appends "Facing (block name) towards (target)"
		/// </summary>
		/// <param name="customInfo">Custom info to append to</param>
		public override void AppendCustomInfo(StringBuilder customInfo)
		{
			customInfo.Append("Facing ");
			customInfo.Append(m_pseudoBlock.Block.DisplayNameText);
			customInfo.Append(" towards ");
			if (m_laser != null)
				customInfo.AppendLine(m_laser.TargetCoords.ToPretty());
			else
				customInfo.AppendLine("Sun");
			//customInfo.Append("Angle: ");
			//customInfo.AppendLine(PrettySI.makePretty(MathHelper.ToDegrees(m_navSet.Settings_Current.DistanceAngle)));
		}

		#endregion

		/// <summary>
		/// Gets the direction of the target.
		/// </summary>
		/// <returns>The direction of the target.</returns>
		private Vector3 TargetDirection()
		{
			if (m_laser != null)
				return m_laser.TargetCoords - m_laser.GetPosition();
			return SunProperties.SunDirection;
		}

	}
}
