using System;
using System.Text;
using Rynchodon.AntennaRelay;
using Rynchodon.Autopilot.Data;
using Rynchodon.Autopilot.Pathfinding;
using Rynchodon.Utility;
using Rynchodon.Weapons;
using Sandbox.Common.ObjectBuilders;
using Sandbox.Game.Entities.Cube;
using Sandbox.ModAPI;
using VRage;
using VRage.Game.Entity;
using VRage.Game.ModAPI;
using VRage.ModAPI;
using VRageMath;

namespace Rynchodon.Autopilot.Navigator
{

	public class Grinder : NavigatorMover, INavigatorRotator, IDisposable
	{

		private const float MaxAngleRotate = 1f;
		private static readonly TimeSpan SearchTimeout = new TimeSpan(0, 1, 0);
		private static readonly TargeterTracker m_targetTracker = new TargeterTracker();

		private enum Stage : byte { None, Intercept, Grind, Terminated }

		private readonly MultiBlock<MyObjectBuilder_ShipGrinder> m_navGrind;
		private readonly Vector3 m_startPostion;
		private readonly float m_grinderOffset;
		private readonly float m_longestDimension;
		private readonly GridFinder m_finder;

		private Vector3D m_targetPosition;
		private TimeSpan m_timeoutAt = Globals.ElapsedTime + SearchTimeout;
		private LineSegmentD m_approach = new LineSegmentD();
		private ulong m_nextGrinderCheck;
		private bool m_grinderFull, m_enabledGrinders;
		private Stage value_stage;
		private Vector3I m_previousCell;

		private Stage m_stage
		{
			get { return value_stage; }
			set
			{
				if (value == value_stage)
					return;
				Log.DebugLog("Changing stage from " + value_stage + " to " + value, Logger.severity.DEBUG);
				value_stage = value;
			}
		}

		private IMyCubeGrid m_enemy
		{
			get { return m_currentTarget == null ? (IMyCubeGrid)null : (IMyCubeGrid)m_currentTarget.Entity; }
		}

		private LastSeen value_currentTarget;
		private LastSeen m_currentTarget
		{
			get { return value_currentTarget; }
			set
			{
				m_targetTracker.ChangeTarget(value_currentTarget, value, m_mover.Block.CubeBlock.EntityId);
				value_currentTarget = value;
			}
		}

		private Logable Log
		{
			get { return new Logable(m_controlBlock.CubeGrid, m_stage.ToString()); }
		}


		public Grinder(Pathfinder pathfinder, float maxRange)
			: base(pathfinder)
		{
			this.m_startPostion = m_controlBlock.CubeBlock.GetPosition();
			this.m_longestDimension = m_controlBlock.CubeGrid.GetLongestDim();

			PseudoBlock navBlock = m_navSet.Settings_Current.NavigationBlock;
			m_navGrind = navBlock.Block is IMyShipGrinder
				? new MultiBlock<MyObjectBuilder_ShipGrinder>(navBlock.Block)
				: new MultiBlock<MyObjectBuilder_ShipGrinder>(() => m_mover.Block.CubeGrid);

			if (m_navGrind.FunctionalBlocks == 0)
			{
				Log.DebugLog("no working grinders", Logger.severity.INFO);
				return;
			}

			m_grinderOffset = m_navGrind.Block.GetLengthInDirection(m_navGrind.Block.FirstFaceDirection()) * 0.5f + 2.5f;
			if (m_navSet.Settings_Current.DestinationRadius > m_longestDimension)
			{
				Log.DebugLog("Reducing DestinationRadius from " + m_navSet.Settings_Current.DestinationRadius + " to " + m_longestDimension, Logger.severity.DEBUG);
				m_navSet.Settings_Task_NavRot.DestinationRadius = m_longestDimension;
			}

			this.m_finder = new GridFinder(m_navSet, m_controlBlock, maxRange);
			this.m_finder.OrderValue = OrderValue;
			m_navSet.Settings_Task_NavRot.NavigatorMover = this;
			m_navSet.Settings_Task_NavRot.NavigatorRotator = this;
			m_navSet.Settings_Task_NavRot.IgnoreEntity = IgnoreEntity;
		}

		~Grinder()
		{
			Dispose();
		}

		public void Dispose()
		{
			if (Globals.WorldClosed)
				return;
			m_currentTarget = null;
		}

		private bool IgnoreEntity(IMyEntity entity)
		{
			return m_stage == Stage.Grind && (entity == m_enemy || m_mover.Block.CubeBlock.canConsiderHostile(entity) && entity.WorldAABB.Intersects(m_enemy.WorldAABB));
		}

		public override void Move()
		{
			if (m_navGrind.FunctionalBlocks == 0)
			{
				Log.DebugLog("No functional grinders remaining", Logger.severity.INFO);
				m_navSet.OnTaskComplete_NavRot();
				m_stage = Stage.Terminated;
				return;
			}

			if (GrinderFull())
			{
				Log.DebugLog("Grinders are full", Logger.severity.INFO);
				m_navSet.OnTaskComplete_NavRot();
				m_stage = Stage.Terminated;
				return;
			}

			m_finder.Update();
			m_currentTarget = m_finder.Grid;

			if (m_currentTarget == null)
			{
				m_mover.StopMove();
				if (Globals.ElapsedTime >= m_timeoutAt)
				{
					Log.DebugLog("Search timed out");
					m_navSet.OnTaskComplete_NavRot();
					m_stage = Stage.Terminated;
				}
				return;
			}

			m_timeoutAt = Globals.ElapsedTime + SearchTimeout;
			if (CheckIntercept())
				return;

			Move_Grind();
		}

		private bool CheckIntercept()
		{
			Vector3D targetCentre = m_enemy.GetCentre();
			float targetLongest = Math.Max(m_enemy.LocalAABB.GetLongestDim(), 10f);
			float minDistance = targetLongest + m_longestDimension;
			float multi = m_stage == Stage.Intercept ? 0.5f : 1f;

			// if enemy is far from start, get in front of it
			double distSq; Vector3D.DistanceSquared(ref m_finder.m_startPosition, ref targetCentre, out distSq);
			if (distSq > 10000d)
			{
				Vector3D targetFromStart; Vector3D.Subtract(ref targetCentre, ref m_finder.m_startPosition, out targetFromStart); targetFromStart.Normalize();
				if (m_stage == Stage.Grind)
				{
					m_approach.From = targetCentre;
					m_approach.To = targetCentre + targetFromStart * (minDistance * 1.5f);
				}
				else
				{
					m_approach.From = targetCentre + targetFromStart * (minDistance * 0.5f);
					m_approach.To = targetCentre + targetFromStart * (minDistance * 2f);
				}
				//Log.DebugLog("start position: " + m_startPostion + ", target centre: " + targetCentre + ", dist sq: " + distSq + ", startFromTarget: " + targetFromStart);
				//Log.DebugLog("Cylinder: {From:" + m_approach.From + " To:" + m_approach.To + " Radius:" + (targetLongest * multi) + "}" + ", position: " + m_navGrind.WorldPosition);
				if (!m_approach.PointInCylinder(targetLongest * multi, m_navGrind.WorldPosition))
				{
					m_targetPosition = targetCentre;
					Move_Intercept(targetCentre + targetFromStart * minDistance);
					return true;
				}
			}
			else
			{
				// if enemy is moving, get in front of it
				Vector3 enemyVelocity = m_enemy.GetLinearVelocity();
				if (enemyVelocity.LengthSquared() > 10f)
				{
					enemyVelocity.Normalize();
					if (m_stage == Stage.Grind)
					{
						m_approach.From = targetCentre;
						m_approach.To = targetCentre + enemyVelocity * (minDistance * 1.5f);
					}
					else
					{
						m_approach.From = targetCentre + enemyVelocity * (minDistance * 0.5f);
						m_approach.To = targetCentre + enemyVelocity * (minDistance * 2f);
					}
					//Log.DebugLog("enemyVelocity: " + enemyVelocity);
					//Log.DebugLog("Cylinder: {From:" + m_approach.From + " To:" + m_approach.To + " Radius:" + (targetLongest * multi) + "}" + ", position: " + m_navGrind.WorldPosition);
					if (!m_approach.PointInCylinder(targetLongest * multi, m_navGrind.WorldPosition))
					{
						m_targetPosition = targetCentre;
						Move_Intercept(targetCentre + enemyVelocity * minDistance);
						return true;
					}
				}
			}
			return false;
		}

		private void Move_Grind()
		{
			if (m_stage < Stage.Grind)
			{
				Log.DebugLog("Now grinding", Logger.severity.DEBUG);
				//m_navSet.OnTaskComplete_NavMove();
				m_stage = Stage.Grind;
				//m_navSet.Settings_Task_NavMove.PathfinderCanChangeCourse = false;
				EnableGrinders(true);
			}

			CubeGridCache cache = CubeGridCache.GetFor(m_enemy);
			if (cache == null)
				return;
			m_previousCell = cache.GetClosestOccupiedCell(m_controlBlock.CubeGrid.GetCentre(), m_previousCell);
			IMySlimBlock block = m_enemy.GetCubeBlock(m_previousCell);
			if (block == null)
			{
				Log.DebugLog("No block found at cell position: " + m_previousCell, Logger.severity.INFO);
				return;
			}
			//Log.DebugLog("block: " + block);
			m_targetPosition = m_enemy.GridIntegerToWorld(m_enemy.GetCubeBlock(m_previousCell).Position);
			//Log.DebugLog("cellPosition: " + m_previousCell + ", block: " + m_enemy.GetCubeBlock(m_previousCell) + ", world: " + m_targetPosition);

			if (m_navSet.Settings_Current.DistanceAngle > MaxAngleRotate)
			{
				if (m_pathfinder.RotateCheck.ObstructingEntity != null)
				{
					Log.DebugLog("Extricating ship from target");
					m_navSet.Settings_Task_NavMove.SpeedMaxRelative = float.MaxValue;
					Destination dest = Destination.FromWorld(m_enemy, m_targetPosition + m_navGrind.WorldMatrix.Backward * 100f);
					m_pathfinder.MoveTo(destinations: dest);
				}
				else
				{
					Log.DebugLog("Waiting for angle to match");
					m_pathfinder.HoldPosition(m_enemy);
				}
				return;
			}

			Vector3D grindPosition = m_navGrind.WorldPosition;
			float distSq = (float)Vector3D.DistanceSquared(m_targetPosition, grindPosition);
			float offset = m_grinderOffset + m_enemy.GridSize;
			float offsetEpsilon = offset + 5f;
			if (distSq > offsetEpsilon * offsetEpsilon)
			{
				Vector3D targetToGrinder = grindPosition - m_targetPosition;
				targetToGrinder.Normalize();

				//Log.DebugLog("far away(" + distSq + "), moving to " + (m_targetPosition + targetToGrinder * offset));
				m_navSet.Settings_Task_NavMove.SpeedMaxRelative = float.MaxValue;
				Destination dest = Destination.FromWorld(m_enemy, m_targetPosition + targetToGrinder * offset);
				m_pathfinder.MoveTo(destinations: dest);
			}
			else
			{
				//Log.DebugLog("close(" + distSq + "), moving to " + m_targetPosition);
				m_navSet.Settings_Task_NavMove.SpeedMaxRelative = 1f;
				Destination dest = Destination.FromWorld(m_enemy, m_targetPosition);
				m_pathfinder.MoveTo(destinations: dest);
			}
		}

		private void Move_Intercept(Vector3D position)
		{
			if (m_stage != Stage.Intercept)
			{
				Log.DebugLog("Now intercepting", Logger.severity.DEBUG);
				//m_navSet.OnTaskComplete_NavMove();
				m_navSet.Settings_Task_NavMove.SpeedMaxRelative = float.MaxValue;
				m_stage = Stage.Intercept;
				//m_navSet.Settings_Task_NavMove.PathfinderCanChangeCourse = true;
				EnableGrinders(false);
			}

			//Log.DebugLog("Moving to " + position);
			Destination dest = Destination.FromWorld(m_enemy, position);
			m_pathfinder.MoveTo(destinations: dest);
		}

		public void Rotate()
		{
			if (m_enemy == null || (m_navSet.DistanceLessThan(1f) && m_navSet.Settings_Current.DistanceAngle <= MaxAngleRotate))
			{
				m_mover.CalcRotate_Stop();
				return;
			}
			if (m_stage == Stage.Intercept)
			{
				m_mover.CalcRotate();
				return;
			}

			//Log.DebugLog("rotating to " + m_targetPosition);
			m_mover.CalcRotate(m_navGrind, RelativeDirection3F.FromWorld(m_navGrind.Grid, m_targetPosition - m_navGrind.WorldPosition));
		}

		public override void AppendCustomInfo(StringBuilder customInfo)
		{
			customInfo.AppendLine("Grinder:");
			if (m_enemy == null)
			{
				customInfo.Append("Searching for a ship, timeout in ");
				customInfo.Append((m_timeoutAt - Globals.ElapsedTime).Seconds);
				customInfo.AppendLine(" seconds.");

				switch (m_finder.m_reason)
				{
					case GridFinder.ReasonCannotTarget.Too_Far:
						customInfo.Append(m_finder.m_bestGrid.HostileName());
						customInfo.AppendLine(" is too far");
						break;
					case GridFinder.ReasonCannotTarget.Too_Fast:
						customInfo.Append(m_finder.m_bestGrid.HostileName());
						customInfo.AppendLine(" is too fast");
						break;
					case GridFinder.ReasonCannotTarget.Grid_Condition:
						//IMyCubeGrid claimedBy;
						//if (GridsClaimed.TryGetValue(m_finder.m_bestGrid.Entity.EntityId, out claimedBy))
						//{
						//	if (m_controlBlock.CubeBlock.canConsiderFriendly(claimedBy))
						//	{
						//		customInfo.Append(m_finder.m_bestGrid.HostileName());
						//		customInfo.Append(" is claimed by ");
						//		customInfo.AppendLine(claimedBy.DisplayName);
						//	}
						//	else
						//	{
						//		customInfo.Append(m_finder.m_bestGrid.HostileName());
						//		customInfo.AppendLine(" is claimed by another recyler.");
						//	}
						//}
						//else
						//{
						//	customInfo.Append(m_finder.m_bestGrid.HostileName());
						//	customInfo.AppendLine(" was claimed, should be available shortly.");
						//}
						customInfo.AppendLine("Obsolete condition");
						break;
				}
				return;
			}

			switch (m_stage)
			{
				case Stage.Intercept:
					customInfo.Append("Moving towards ");
					customInfo.AppendLine(m_finder.m_bestGrid.HostileName());
					break;
				case Stage.Grind:
					customInfo.Append("Reducing ");
					customInfo.Append(m_finder.m_bestGrid.HostileName());
					customInfo.AppendLine(" to its constituent parts");
					break;
			}
		}

		private double OrderValue(LastSeen seen)
		{
			int numTargeting = m_targetTracker.GetCount(seen.Entity.EntityId);
			if (m_currentTarget != null && m_currentTarget.Entity.EntityId == seen.Entity.EntityId)
				numTargeting--;

			return seen.Entity.WorldAABB.Distance(m_controlBlock.CubeBlock.GetPosition()) + 1000d * numTargeting;
		}

		private bool GrinderFull()
		{
			if (Globals.UpdateCount < m_nextGrinderCheck)
				return m_grinderFull;
			m_nextGrinderCheck = Globals.UpdateCount + 100ul;

			EnableGrinders(m_enabledGrinders);

			MyFixedPoint content = 0, capacity = 0;
			int grinderCount = 0;

			foreach (IMyShipGrinder grinder in CubeGridCache.GetFor(m_controlBlock.CubeGrid).BlocksOfType(typeof(MyObjectBuilder_ShipGrinder)))
			{
				MyInventoryBase grinderInventory = ((MyEntity)grinder).GetInventoryBase(0);

				content += grinderInventory.CurrentVolume;
				capacity += grinderInventory.MaxVolume;
				grinderCount++;
			}

			m_grinderFull = capacity <= 0 || (float)content / (float)capacity >= 0.9f;
			return m_grinderFull;
		}

		private void EnableGrinders(bool enable)
		{
			m_enabledGrinders = enable;

			MyAPIGateway.Utilities.TryInvokeOnGameThread(() => {
				foreach (IMyShipGrinder grinder in CubeGridCache.GetFor(m_controlBlock.CubeGrid).BlocksOfType(typeof(MyObjectBuilder_ShipGrinder)))
					if (!grinder.Closed)
						((MyFunctionalBlock)grinder).Enabled = enable;
			});
		}

	}
}
