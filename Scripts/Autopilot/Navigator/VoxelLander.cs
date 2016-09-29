using System; // partial
using System.Collections.Generic;
using System.Text;
using Rynchodon.Autopilot.Data;
using Rynchodon.Autopilot.Movement;

using Sandbox.Game.Entities;
using Sandbox.ModAPI.Interfaces;
using SpaceEngineers.Game.ModAPI;
using VRageMath;

namespace Rynchodon.Autopilot.Navigator
{
	/// <summary>
	/// Lands on a planet or an asteroid.
	/// </summary>
	public class VoxelLander : NavigatorMover, INavigatorRotator
	{

		public enum Stage { Approach, Rotate, Land }

		private readonly Logger m_logger;
		private readonly PseudoBlock m_landBlock;
		private readonly string m_targetType;

		private Vector3D m_targetPostion;
		private Stage m_stage;

		public VoxelLander(Mover mover, bool planet, PseudoBlock landBlock = null)
			: base(mover)
		{
			this.m_landBlock = landBlock ?? m_navSet.Settings_Current.LandingBlock;
			this.m_logger = new Logger(m_controlBlock.CubeBlock, () => m_landBlock.Block.getBestName());
			this.m_targetType = planet ? "Planet" : "Asteroid";

			if (this.m_landBlock == null)
			{
				m_logger.debugLog("No landing block", Logger.severity.INFO);
				return;
			}

			IMyLandingGear asGear = m_landBlock.Block as IMyLandingGear;
			if (asGear != null)
			{
				ITerminalProperty<bool> autolock = asGear.GetProperty("Autolock") as ITerminalProperty<bool>;
				m_logger.debugLog("autolock == null", Logger.severity.FATAL, condition: autolock == null);
				if (!autolock.GetValue(asGear))
					autolock.SetValue(asGear, true);
			}

			Vector3D currentPostion = m_landBlock.WorldPosition;
			MyVoxelBase closest = null;
			if (planet)
			{
				closest = MyPlanetExtensions.GetClosestPlanet(m_landBlock.WorldPosition);

				if (closest == null)
				{
					m_logger.debugLog("No planets in the world", Logger.severity.WARNING);
					return;
				}
			}
			else
			{
				BoundingSphereD search = new BoundingSphereD(currentPostion, 10000d);
				List<MyVoxelBase> nearby = new List<MyVoxelBase>();
				MyGamePruningStructure.GetAllVoxelMapsInSphere(ref search, nearby);

				double closestDistSquared = double.MaxValue;

				foreach (MyVoxelBase voxel in nearby)
				{
					if (!(voxel is MyVoxelMap))
						continue;

					double distSquared = Vector3D.DistanceSquared(currentPostion, voxel.GetCentre());
					if (distSquared < closestDistSquared)
					{
						closestDistSquared = distSquared;
						closest = voxel;
					}
				}

				if (closest == null)
				{
					m_logger.debugLog("No asteroids nearby", Logger.severity.WARNING);
					return;
				}
			}

			Vector3D? contact;
			if (!RayCast.RayCastVoxel(closest, new LineD(currentPostion, closest.GetCentre()), out contact))
				throw new Exception("Failed to intersect voxel");

			m_targetPostion = contact.Value;
			m_navSet.Settings_Task_NavRot.NavigatorMover = this;
			m_navSet.Settings_Task_NavRot.IgnoreAsteroid = true;

			m_logger.debugLog("Landing on " + m_targetType + " at " + m_targetPostion, Logger.severity.DEBUG);
		}

		public override void Move()
		{
			switch (m_stage)
			{
				case Stage.Approach:
					if (m_navSet.DistanceLessThanDestRadius())
					{
						m_logger.debugLog("finished approach, creating stopper", Logger.severity.DEBUG);
						m_stage = Stage.Rotate;
						m_mover.StopMove();
					}
					else
						m_mover.CalcMove(m_landBlock, m_targetPostion, Vector3.Zero);
					return;
				case Stage.Rotate:
					if (m_navSet.DirectionMatched(0.01f))
					{
						m_logger.debugLog("finished rotating, now landing", Logger.severity.DEBUG);
						m_stage = Stage.Land;
					}
					return;
				case Stage.Land:
					IMyLandingGear gear = m_landBlock.Block as IMyLandingGear;
					if (gear != null)
					{
						if (gear.IsLocked)
						{
							m_logger.debugLog("locked", Logger.severity.INFO);
							m_mover.MoveAndRotateStop(false);
							m_navSet.OnTaskComplete(AllNavigationSettings.SettingsLevelName.NavRot);
							return;
						}
					}
					else if (m_navSet.DistanceLessThan(1f))
					{
						m_logger.debugLog("close enough", Logger.severity.INFO);
						m_mover.MoveAndRotateStop(false);
						m_navSet.OnTaskComplete(AllNavigationSettings.SettingsLevelName.NavRot);
						return;
					}

					m_mover.CalcMove(m_landBlock, m_targetPostion, Vector3.Zero, true);
					return;
			}
		}

		public void Rotate()
		{
			switch (m_stage)
			{
				case Stage.Approach:
					m_mover.CalcRotate();
					return;
				case Stage.Rotate:
				case Stage.Land:
					m_mover.CalcRotate(m_landBlock, RelativeDirection3F.FromWorld(m_landBlock.Grid, m_targetPostion - m_landBlock.WorldPosition));
					return;
			}
		}

		public override void AppendCustomInfo(StringBuilder customInfo)
		{
			switch (m_stage)
			{
				case Stage.Approach:
					customInfo.Append("Approaching closest ");
					customInfo.AppendLine(m_targetType);
					break;
				case Stage.Rotate:
					customInfo.Append("Rotating, angle: ");
					customInfo.AppendLine(MathHelper.ToDegrees(m_navSet.Settings_Current.DistanceAngle).ToString("F1"));
					break;
				case Stage.Land:
					customInfo.Append("Landing on closest ");
					customInfo.AppendLine(m_targetType);
					break;
			}
		}

	}
}
