using System.Text; // from mscorlib.dll
using Rynchodon.Autopilot.Data;
using Rynchodon.Autopilot.Movement;
using VRage.Game.Entity;
using VRage.Game.ModAPI;
using VRage.ModAPI; // from VRage.Game.dll
using VRageMath;

namespace Rynchodon.Autopilot.Navigator
{
	/// <summary>
	/// Fly to an entity with a world offset.
	/// </summary>
	public class Waypoint : NavigatorMover, INavigatorRotator
	{

		private readonly Logger m_logger;
		private readonly AllNavigationSettings.SettingsLevelName m_level;
		private readonly IMyEntity m_targetEntity;
		private readonly Vector3D m_targetOffset;

		private PseudoBlock NavBlock
		{
			get { return m_navSet.Settings_Current.NavigationBlock; }
		}

		private Vector3D TargetPosition
		{
			get { return m_targetEntity.GetPosition() + m_targetOffset; }
		}

		public Waypoint(Mover mover, AllNavigationSettings navSet, AllNavigationSettings.SettingsLevelName level, IMyEntity targetEntity, Vector3D worldOffset)
			: base(mover)
		{
			this.m_logger = new Logger(m_controlBlock.CubeBlock);
			this.m_level = level;
			this.m_targetEntity = targetEntity;
			this.m_targetOffset = worldOffset;

			m_logger.debugLog("targetEntity is not top-most", Logger.severity.FATAL, condition: targetEntity != targetEntity.GetTopMostParent());

			IMyCubeGrid asGrid = targetEntity as IMyCubeGrid;
			if (asGrid != null && Attached.AttachedGrid.IsGridAttached(asGrid, m_controlBlock.CubeGrid, Attached.AttachedGrid.AttachmentKind.Physics))
			{
				m_logger.debugLog("Cannot fly to entity, attached: " + targetEntity.getBestName() + ", creating GOLIS", Logger.severity.WARNING);
				new GOLIS(mover, TargetPosition, level);
				return;
			}
			if (targetEntity.Physics == null)
			{
				m_logger.debugLog("Target has no physics: " + targetEntity.getBestName() + ", creating GOLIS", Logger.severity.WARNING);
				new GOLIS(mover, TargetPosition, level);
				return;
			}

			var setLevel = navSet.GetSettingsLevel(level);
			setLevel.NavigatorMover = this;
			//setLevel.DestinationEntity = mover.Block.CubeBlock; // to force avoidance 

			m_logger.debugLog("created, level: " + level + ", target: " + targetEntity.getBestName() + ", target position: " + targetEntity.GetPosition() + ", offset: " + worldOffset + ", position: " + TargetPosition, Logger.severity.DEBUG);
		}

		public override void Move()
		{
			if (m_navSet.DistanceLessThanDestRadius() || m_targetEntity.MarkedForClose)
			{
				m_logger.debugLog(() => "Reached destination: " + TargetPosition, Logger.severity.INFO, condition: !m_targetEntity.Closed);
				m_logger.debugLog("Target entity closed", Logger.severity.INFO, condition: m_targetEntity.Closed);

				m_navSet.OnTaskComplete(m_level);
				m_mover.StopMove();
				m_mover.StopRotate();
			}
			else
				m_mover.CalcMove(NavBlock, TargetPosition, m_targetEntity.Physics.LinearVelocity);
		}

		public void Rotate()
		{
			m_mover.CalcRotate();
		}

		public override void AppendCustomInfo(StringBuilder customInfo)
		{
			customInfo.Append("Flying to waypoint: ");
			customInfo.AppendLine(TargetPosition.ToPretty());
		}

	}
}
