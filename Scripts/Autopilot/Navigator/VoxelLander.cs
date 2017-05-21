using System; // partial
using System.Collections.Generic;
using System.Text;
using Rynchodon.Autopilot.Data;
using Rynchodon.Autopilot.Pathfinding;
using Rynchodon.Utility;
using Sandbox.Game.Entities;
using Sandbox.ModAPI;
using Sandbox.ModAPI.Interfaces;
using SpaceEngineers.Game.ModAPI;
using VRage.Game.ModAPI;
using VRageMath;

namespace Rynchodon.Autopilot.Navigator
{
	/// <summary>
	/// Lands on a planet or an asteroid.
	/// </summary>
	public class VoxelLander : NavigatorMover, INavigatorRotator
	{

		public enum Stage { Approach, Rotate, Land }

		private readonly PseudoBlock m_landBlock;
		private readonly string m_targetType;

		private Destination m_targetPostion;
		private Stage m_stage;

		private Logable Log
		{
			get { return new Logable(m_controlBlock.CubeBlock, m_landBlock?.Block.getBestName()); }
		}

		public VoxelLander(Pathfinder pathfinder, bool planet, PseudoBlock landBlock = null)
			: base(pathfinder)
		{
			this.m_landBlock = landBlock ?? m_navSet.Settings_Current.LandingBlock;
			this.m_targetType = planet ? "Planet" : "Asteroid";

			if (this.m_landBlock == null)
			{
				Log.DebugLog("No landing block", Logger.severity.INFO);
				return;
			}

			IMyLandingGear asGear = m_landBlock.Block as IMyLandingGear;
			if (asGear != null)
			{
				ITerminalProperty<bool> autolock = asGear.GetProperty("Autolock") as ITerminalProperty<bool>;
				Log.DebugLog("autolock == null", Logger.severity.FATAL, condition: autolock == null);
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
					Log.DebugLog("No planets in the world", Logger.severity.WARNING);
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
					Log.DebugLog("No asteroids nearby", Logger.severity.WARNING);
					return;
				}
			}

			Vector3D end = closest.GetCentre();
			MyVoxelBase hitVoxel;
			Vector3D hitPosition;
			if (!RayCast.RayCastVoxels(ref currentPostion, ref end, out hitVoxel, out hitPosition))
				throw new Exception("Failed to intersect voxel");

			m_targetPostion = Destination.FromWorld(hitVoxel, hitPosition);

			m_navSet.Settings_Task_NavRot.NavigatorMover = this;
			m_navSet.Settings_Task_NavRot.IgnoreAsteroid = true;

			Log.DebugLog("Landing on " + m_targetType + " at " + m_targetPostion, Logger.severity.DEBUG);
		}

		public override void Move()
		{
			switch (m_stage)
			{
				case Stage.Approach:
					if (m_navSet.DistanceLessThanDestRadius())
					{
						Log.DebugLog("finished approach, creating stopper", Logger.severity.DEBUG);
						m_stage = Stage.Rotate;
						m_mover.StopMove();
					}
					else
						m_pathfinder.MoveTo(destinations: m_targetPostion);
					return;
				case Stage.Rotate:
					if (m_navSet.DirectionMatched(0.01f))
					{
						Log.DebugLog("finished rotating, now landing", Logger.severity.DEBUG);
						m_stage = Stage.Land;

						IMyLandingGear gear = m_landBlock.Block as IMyLandingGear;
						if (gear != null)
						{
							// move target 10 m towards centre
							Vector3D targetEntityCentre = m_targetPostion.Entity.GetCentre();
							Vector3D currentPosition = gear.PositionComp.GetPosition();
							Vector3D currToCentre; Vector3D.Subtract(ref targetEntityCentre, ref currentPosition, out currToCentre);
							currToCentre.Normalize();
							Vector3D moveTargetBy; Vector3D.Multiply(ref currToCentre, 10d, out moveTargetBy);
							m_targetPostion.Position += moveTargetBy;
						}
					}
					return;
				case Stage.Land:
					{
						IMyLandingGear gear = m_landBlock.Block as IMyLandingGear;
						if (gear != null)
						{
							if (gear.IsLocked)
							{
								Log.DebugLog("locked", Logger.severity.INFO);
								m_mover.MoveAndRotateStop(false);
								m_navSet.OnTaskComplete(AllNavigationSettings.SettingsLevelName.NavRot);
								return;
							}
						}
						else if (m_navSet.DistanceLessThan(1f))
						{
							Log.DebugLog("close enough", Logger.severity.INFO);
							m_mover.MoveAndRotateStop(false);
							m_navSet.OnTaskComplete(AllNavigationSettings.SettingsLevelName.NavRot);
							return;
						}

						m_pathfinder.MoveTo(destinations: m_targetPostion);
						return;
					}
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
					m_mover.CalcRotate(m_landBlock, RelativeDirection3F.FromWorld(m_landBlock.Grid, m_targetPostion.WorldPosition() - m_landBlock.WorldPosition));
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
