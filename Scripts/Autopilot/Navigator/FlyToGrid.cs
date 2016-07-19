using System;
using System.Collections.Generic;
using System.Text;
using Rynchodon.Attached;
using Rynchodon.Autopilot.Data;
using Rynchodon.Autopilot.Movement;
using Sandbox.Common.ObjectBuilders;
using Sandbox.ModAPI;
using SpaceEngineers.Game.ModAPI;
using VRage.Game.ModAPI;
using VRageMath;
using Ingame = Sandbox.ModAPI.Ingame;

namespace Rynchodon.Autopilot.Navigator
{

	public class FlyToGrid : NavigatorMover, INavigatorRotator, IDisposable
	{

		public enum LandingState : byte { None, Approach, Holding, LineUp, Landing, Catch }

		private static readonly TimeSpan SearchTimeout = new TimeSpan(0, 1, 0);
		private static HashSet<long> s_reservedTargets = new HashSet<long>();

		static FlyToGrid()
		{
			MyAPIGateway.Entities.OnCloseAll += Entities_OnCloseAll;
		}

		private static void Entities_OnCloseAll()
		{
			MyAPIGateway.Entities.OnCloseAll -= Entities_OnCloseAll;
			s_reservedTargets = null;
		}

		private readonly Logger m_logger;
		private readonly GridFinder m_gridFinder;
		private readonly BlockNameOrientation m_targetBlock;
		private readonly PseudoBlock m_contBlock;
		private readonly PseudoBlock m_navBlock;
		/// <summary>m_targetBlock.Forward  or opposite of the landing face</summary>
		private readonly Base6Directions.Direction m_landingDirection;
		/// <summary>Half of length of landing block in the direction it will be landing.</summary>
		private readonly float m_landingHalfSize;
		private readonly bool m_landGearWithoutTargetBlock;
		private readonly AllNavigationSettings.SettingsLevelName m_settingLevel;
		private readonly bool m_landingFriend;

		private TimeSpan m_searchTimeoutAt = Globals.ElapsedTime + SearchTimeout;
		private Vector3D m_targetPosition;
		private LandingState value_landingState = LandingState.None;
		private float m_landingSpeedFudge;
		private ulong next_attemptLock;
		private bool m_beforeMerge;
		private long m_reservedTarget;

		public LandingState m_landingState
		{
			get { return value_landingState; }
			private set
			{
				if (value_landingState == value)
					return;

				m_logger.debugLog("changing landing state to " + value, Logger.severity.DEBUG);
				value_landingState = value;

				switch (value)
				{
					case LandingState.Catch:
					case LandingState.Landing:
						{
							IMyFunctionalBlock asFunc = m_navBlock.Block as IMyFunctionalBlock;
							if (asFunc != null && !asFunc.Enabled)
							{
								m_logger.debugLog("Enabling m_navBlock: " + m_navBlock.Block.DisplayNameText, Logger.severity.DEBUG);
								MyAPIGateway.Utilities.TryInvokeOnGameThread(() => asFunc.RequestEnable(true));
							}
							IMyLandingGear asGear = m_navBlock.Block as IMyLandingGear;
							if (asGear != null)
							{
								IMyCubeBlock block = m_navBlock.Block;
								MyAPIGateway.Utilities.TryInvokeOnGameThread(() => {
									if (!(block.GetObjectBuilderCubeBlock() as MyObjectBuilder_LandingGear).AutoLock)
										asGear.ApplyAction("Autolock");
								});
							}
							break;
						}
				}

				m_navSet.OnTaskComplete_NavWay();
			}
		}

		public FlyToGrid(Mover mover, string targetGrid = null, AttachedGrid.AttachmentKind allowedAttachment = AttachedGrid.AttachmentKind.Permanent, GridFinder finder = null, PseudoBlock landingBlock = null)
			: base(mover)
		{
			this.m_logger = new Logger(GetType().Name, m_controlBlock.CubeBlock, () => m_landingState.ToString());
			this.m_targetBlock = m_navSet.Settings_Current.DestinationBlock;
			string blockName = m_targetBlock == null ? null : m_targetBlock.BlockName;
			if (finder != null)
				this.m_gridFinder = finder;
			else
				this.m_gridFinder = new GridFinder(m_navSet, m_mover.Block, targetGrid, blockName, allowedAttachment);
			this.m_landingFriend = !(this.m_gridFinder is EnemyFinder);
			this.m_contBlock = m_navSet.Settings_Commands.NavigationBlock;

			if (landingBlock == null)
				landingBlock = m_navSet.Settings_Current.LandingBlock;
			m_navBlock = landingBlock ?? m_navSet.Settings_Current.NavigationBlock;

			if (landingBlock != null)
			{
				if (landingBlock.Block is IMyFunctionalBlock)
					m_landingState = LandingState.Approach;
				else
				{
					m_logger.debugLog("landingBlock is not functional, player error? : " + landingBlock.Block.DisplayNameText, Logger.severity.INFO);
					m_landingState = LandingState.None;
				}

				if (m_targetBlock == null)
				{
					if (!(landingBlock.Block is IMyLandingGear))
					{
						m_logger.debugLog("cannot land block without a target", Logger.severity.INFO);
						m_landingState = LandingState.None;
					}
					else
					{
						m_logger.debugLog("golden retriever mode enabled", Logger.severity.INFO);
						m_landGearWithoutTargetBlock = true;
					}
				}
				else if (landingBlock.Block is Ingame.IMyShipConnector)
				{
					m_gridFinder.BlockCondition = block => {
						Ingame.IMyShipConnector connector = block as Ingame.IMyShipConnector;
						return connector != null && (!connector.IsConnected || connector.OtherConnector == m_navBlock.Block) && ReserveTarget(connector.EntityId);
					};
					m_landingDirection = m_targetBlock.Forward ?? Base6Directions.GetFlippedDirection(landingBlock.Block.FirstFaceDirection());
				}
				else if (landingBlock.Block is IMyShipMergeBlock)
				{
					m_gridFinder.BlockCondition = block => block is IMyShipMergeBlock && ReserveTarget(block.EntityId);
					m_landingDirection = m_targetBlock.Forward ?? Base6Directions.GetFlippedDirection(landingBlock.Block.FirstFaceDirection());
					(landingBlock.Block as IMyShipMergeBlock).BeforeMerge += MergeBlock_BeforeMerge;
				}
				else if (m_targetBlock.Forward.HasValue)
					m_landingDirection = m_targetBlock.Forward.Value;
				else
				{
					m_logger.debugLog("Player failed to specify landing direction and it could not be determined.", Logger.severity.INFO);
					m_landingState = LandingState.None;
				}

				if (m_landingState != LandingState.None)
				{
					//float minDestRadius = m_controlBlock.CubeGrid.GetLongestDim() * 5f;
					//if (m_navSet.Settings_Current.DestinationRadius < minDestRadius)
					//{
					//	m_logger.debugLog("Increasing DestinationRadius from " + m_navSet.Settings_Current.DestinationRadius + " to " + minDestRadius, "FlyToGrid()", Logger.severity.DEBUG);
					//	m_navSet.Settings_Task_NavRot.DestinationRadius = minDestRadius;
					//}

					new UnLander(mover, landingBlock);

					m_landingHalfSize = landingBlock.Block.GetLengthInDirection(landingBlock.Block.LocalMatrix.GetClosestDirection(landingBlock.LocalMatrix.Forward)) * 0.5f;
					m_logger.debugLog("m_landing direction: " + m_landingDirection + ", m_landingBlockSize: " + m_landingHalfSize);
				}
			}

			m_settingLevel = m_landingState != LandingState.None ? AllNavigationSettings.SettingsLevelName.NavRot : AllNavigationSettings.SettingsLevelName.NavMove;
			m_navSet.GetSettingsLevel(m_settingLevel).NavigatorMover = this;
		}

		~FlyToGrid()
		{
			Dispose();
		}

		public void Dispose()
		{
			UnreserveTarget();
		}

		private void UnreserveTarget()
		{
			if (m_reservedTarget != 0L && s_reservedTargets != null)
			{
				m_logger.debugLog("unreserve: " + m_reservedTarget);
				s_reservedTargets.Remove(m_reservedTarget);
				m_reservedTarget = 0L;
			}
		}

		private bool ReserveTarget(long target)
		{
			if (target == m_reservedTarget)
				return true;

			UnreserveTarget();

			if (s_reservedTargets.Add(target))
			{
				m_logger.debugLog("reserve: " + target);
				m_reservedTarget = target;
				return true;
			}
			else
				m_logger.debugLog("cannot reserve: " + target);
			return false;
		}

		public override void Move()
		{
			m_logger.debugLog(m_gridFinder == null, "m_gridFinder == null", Logger.severity.FATAL);
			m_logger.debugLog(m_navSet == null, "m_navSet == null", Logger.severity.FATAL);
			m_logger.debugLog(m_mover == null, "m_mover == null", Logger.severity.FATAL);
			m_logger.debugLog(m_navBlock == null, "m_navBlock == null", Logger.severity.FATAL);

			m_gridFinder.Update();

			if (m_gridFinder.Grid == null)
			{
				m_logger.debugLog("searching");
				m_mover.StopMove();

				// only timeout if (Grid == null), ship could simply be waiting its turn
				if (Globals.ElapsedTime > m_searchTimeoutAt)
				{
					m_logger.debugLog("Search timed out", Logger.severity.INFO);
					m_navSet.OnTaskComplete(m_settingLevel);
					UnreserveTarget();
					m_mover.StopMove();
					m_mover.StopRotate();
					return;
				}

				if (m_landingState > LandingState.Approach)
				{
					m_logger.debugLog("Decreasing landing state from " + m_landingState + " to " + LandingState.Approach, Logger.severity.DEBUG);
					m_landingState = LandingState.Approach;
				}

				return;
			}
			else
			{
				m_targetPosition = m_gridFinder.GetPosition(m_navBlock.WorldPosition, m_navSet.Settings_Current.DestinationOffset);

				if (m_gridFinder.Block != null && m_landingState < LandingState.Landing)
					m_navSet.GetSettingsLevel(m_settingLevel).DestinationEntity = m_gridFinder.Block;
				else
					m_navSet.GetSettingsLevel(m_settingLevel).DestinationEntity = m_gridFinder.Grid.Entity;
				m_searchTimeoutAt = Globals.ElapsedTime + SearchTimeout;

				if (m_landingState > LandingState.Approach || m_navBlock.Grid.WorldAABB.Distance(m_targetPosition) < m_navSet.Settings_Current.DestinationRadius)
				{
					Move_Land();
					return;
				}

				// set destination to be short of grid so pathfinder knows we will not hit it
				Vector3D targetToNav = m_navBlock.WorldPosition - m_targetPosition;
				targetToNav.Normalize();
				float adjustment = m_navSet.Settings_Current.DestinationRadius * 0.5f;
				Vector3D destination = m_targetPosition + targetToNav * adjustment;

				//m_logger.debugLog("m_targetPosition: " + m_targetPosition + ", moved by " + adjustment + " to " + destination + ", velocity: " + m_gridFinder.Grid.GetLinearVelocity(), "Move()");
				m_mover.CalcMove(m_navBlock, destination, m_gridFinder.Grid.GetLinearVelocity());
				m_navSet.Settings_Current.Distance += adjustment;
			}
		}

		public void Rotate()
		{
			if (m_gridFinder.Grid == null)
			{
				m_mover.StopRotate();
				return;
			}

			if (m_landingState == LandingState.None)
			{
				if (m_targetBlock != null && m_targetBlock.Forward.HasValue)
				{
					m_navSet.GetSettingsLevel(m_settingLevel).NavigatorRotator = this;
					//m_logger.debugLog("matching target direction", "Rotate()");
					m_mover.CalcRotate(m_navBlock, m_gridFinder.Block, m_targetBlock.Forward, m_targetBlock.Upward);
					return;
				}
				else
					m_mover.CalcRotate();
				return;
			}

			if (m_landingState == LandingState.Approach)
			{
				//m_logger.debugLog("facing controller towards target : " + m_targetPosition, "Rotate()");
				m_mover.CalcRotate();
				return;
			}

			if (m_gridFinder.Block == null)
			{
				if (m_landGearWithoutTargetBlock)
				{
					m_mover.CalcRotate(m_navBlock, RelativeDirection3F.FromWorld(m_navBlock.Grid, m_targetPosition));
					return;
				}

				m_logger.debugLog("no Block, not facing");
				m_mover.StopRotate();
				return;
			}

			//m_logger.debugLog("rotating for landing", "Rotate()");
			if (IsLocked())
			{
				m_logger.debugLog("already landed");
				return;
			}
			m_mover.CalcRotate(m_navBlock, m_gridFinder.Block, m_landingDirection, m_targetBlock.Upward);
			return;
		}

		public override void AppendCustomInfo(StringBuilder customInfo)
		{
			if (m_gridFinder.Grid == null)
			{
				customInfo.Append("Searching for ");
				customInfo.AppendLine(m_gridFinder.m_targetGridName);
			}
			else if (m_gridFinder.Block == null)
			{
				if (m_gridFinder.m_targetBlockName != null)
				{
					customInfo.Append("Searching for ");
					customInfo.AppendLine(m_gridFinder.m_targetBlockName);
				}
				customInfo.Append("Flying to ");
				customInfo.AppendLine(m_gridFinder.Grid.Entity.DisplayName);

				//customInfo.Append("Distance: ");
				//customInfo.AppendLine(SPrettyDistance());
			}
			else
			{
				customInfo.Append("Flying to ");
				customInfo.Append(m_gridFinder.Block.DisplayNameText);
				customInfo.Append(" on ");
				customInfo.AppendLine(m_gridFinder.Grid.Entity.DisplayName);

				//customInfo.Append("Distance: ");
				//customInfo.AppendLine(m_navSet.PrettyDistance());
			}

			if (m_landingState != LandingState.None)
			{
				customInfo.Append("Landing: ");
				customInfo.AppendLine(m_landingState.ToString());
			}
		}

		/// <summary>
		/// Subpart of Mover() that runs when ship is inside destination radius.
		/// </summary>
		private void Move_Land()
		{
			if (IsLocked())
			{
				m_logger.debugLog("Attached!", Logger.severity.INFO);
				m_navSet.OnTaskComplete(m_settingLevel);
				UnreserveTarget();
				m_mover.StopMove(false);
				m_mover.StopRotate();
				if (m_navSet.Shopper != null)
				{
					m_logger.debugLog("starting shopper");
					m_navSet.Shopper.Start();
				}
				return;
			}

			switch (m_landingState)
			{
				case LandingState.None:
					{
						if (m_navSet.Settings_Current.Stay_In_Formation)
						{
							m_logger.debugLog("Maintaining relative position to target");
							m_mover.CalcMove(m_navBlock, m_navBlock.WorldPosition, m_gridFinder.Grid.GetLinearVelocity());
						}
						else
						{
							if (m_targetBlock != null && m_targetBlock.Forward.HasValue ? m_navSet.DirectionMatched() : m_mover.AngularVelocity == Vector3.Zero)
							{
								m_logger.debugLog("Arrived at target", Logger.severity.INFO);
								m_navSet.OnTaskComplete(m_settingLevel);
								UnreserveTarget();
								m_mover.StopRotate();
							}
							m_mover.StopMove();
						}
						return;
					}
				case LandingState.Approach:
					{
						m_navSet.GetSettingsLevel(m_settingLevel).NavigatorRotator = this;
						m_navSet.GetSettingsLevel(m_settingLevel).NavigationBlock = m_navBlock;
						if (m_landGearWithoutTargetBlock)
						{
							m_landingState = LandingState.Catch;
							goto case LandingState.Catch;
						}
						else
						{
							m_landingState = LandingState.Holding;
							goto case LandingState.Holding;
						}
					}
				case LandingState.Holding:
					{
						if (m_gridFinder.Block != null)
						{
							m_logger.debugLog("Have a block, starting landing sequence", Logger.severity.DEBUG);
							m_landingState = LandingState.LineUp;
							return;
						}
						m_mover.CalcMove(m_navBlock, m_navBlock.WorldPosition, m_gridFinder.Grid.GetLinearVelocity());
						return;
					}
				case LandingState.LineUp:
					{
						if (m_gridFinder.Block == null)
						{
							m_logger.debugLog("lost block");
							m_landingState = LandingState.Holding;
							return;
						}

						if (m_navSet.DirectionMatched())
						{
							if (m_navSet.Settings_Current.Distance < 1f)
							{
								m_logger.debugLog("Reached line: " + m_navSet.Settings_Current.Distance);
								m_landingState = LandingState.Landing;
								return;
							}
						}
						else if (!m_mover.Pathfinder.CanRotate)
						{
							Vector3 destination = m_targetPosition;
							Vector3 directAway = m_navBlock.WorldPosition - destination;
							if (directAway.LengthSquared() < 1)
							{
								destination = m_gridFinder.Grid.Entity.WorldAABB.Center;
								directAway = m_navBlock.WorldPosition - destination;
							}
							Vector3D targetPosition = destination + Vector3.Normalize(directAway) * m_navSet.Settings_Current.DestinationRadius;

							m_logger.debugLog("Pathfinder cannot rotate, moving away. destination: " + destination + ", directAway: " + Vector3.Normalize(directAway) +
								", DestinationRadius: " + m_navSet.Settings_Current.DestinationRadius + ", targetPosition: " + targetPosition);

							m_mover.CalcMove(m_navBlock, targetPosition, m_gridFinder.Grid.GetLinearVelocity());
							return;
						}
						else if (m_navSet.Settings_Current.Distance < 1f)
						{
							// replace do nothing (line) or other rotator that is preventing ship from landing
							m_navSet.Settings_Task_NavWay.NavigatorRotator = this;
						}

						// move to line from target block outwards
						Vector3D landFaceVector = GetLandingFaceVector();
						float distanceBetween = m_gridFinder.Block.GetLengthInDirection(m_landingDirection) * 0.5f + m_landingHalfSize + 1f;
						Line destinationLine = new Line(m_targetPosition + landFaceVector * distanceBetween, m_targetPosition + landFaceVector * 1000f);
						Vector3D closestPoint = destinationLine.ClosestPoint(m_navBlock.WorldPosition);

						//m_logger.debugLog("Flying to closest point on line between " + destinationLine.From + " and " + destinationLine.To + " which is " + closestPoint, "Move_Land()");
						m_mover.CalcMove(m_navBlock, closestPoint, m_gridFinder.Grid.GetLinearVelocity());

						return;
					}
				case LandingState.Landing:
					{
						if (m_gridFinder.Block == null)
						{
							m_logger.debugLog("lost block");
							m_landingState = LandingState.Holding;
							return;
						}

						if (m_navSet.Settings_Current.DistanceAngle > 0.1f)
						{
							m_logger.debugLog("waiting for direction to match");
							m_mover.CalcMove(m_navBlock, m_navBlock.WorldPosition, m_gridFinder.Grid.GetLinearVelocity());
							return;
						}

						LockConnector();

						float distanceBetween = m_gridFinder.Block.GetLengthInDirection(m_landingDirection) * 0.5f + m_landingHalfSize + 0.1f;
						//m_logger.debugLog("moving to " + (m_targetPosition + GetLandingFaceVector() * distanceBetween) + ", distance: " + m_navSet.Settings_Current.Distance, "Move_Land()");

						if (m_navSet.DistanceLessThan(1f))
						{
							m_landingSpeedFudge += 0.0001f;
							if (m_landingSpeedFudge > 0.1f)
								m_landingSpeedFudge = -0.1f;
						}
						m_targetPosition += m_gridFinder.Grid.GetLinearVelocity() * m_landingSpeedFudge;

						m_mover.CalcMove(m_navBlock, m_targetPosition + GetLandingFaceVector() * distanceBetween, m_gridFinder.Grid.GetLinearVelocity(), m_landingFriend);
						return;
					}
				case LandingState.Catch:
					{
						if (m_navSet.Settings_Current.DistanceAngle > 0.1f)
						{
							m_logger.debugLog("waiting for direction to match");
							m_mover.CalcMove(m_navBlock, m_navBlock.WorldPosition, m_gridFinder.Grid.GetLinearVelocity());
							return;
						}

						if (m_navSet.DistanceLessThan(1f))
						{
							m_landingSpeedFudge += 0.0001f;
							if (m_landingSpeedFudge > 0.1f)
								m_landingSpeedFudge = -0.1f;
							m_logger.debugLog("target position: " + m_targetPosition + ", nav position: " + m_navBlock.WorldPosition + ", distance: " + Vector3D.Distance(m_targetPosition, m_navBlock.WorldPosition) + "/" + m_navSet.Settings_Current.Distance + ", fudge: " + m_landingSpeedFudge);
						}
						m_targetPosition += m_gridFinder.Grid.GetLinearVelocity() * m_landingSpeedFudge;

						m_logger.debugLog("moving to " + m_targetPosition);
						m_mover.CalcMove(m_navBlock, m_targetPosition, m_gridFinder.Grid.GetLinearVelocity(), m_landingFriend);
						return;
					}
			}
		}

		/// <summary>
		/// Gets the world vector representing the opposite direction of m_landingDirection
		/// </summary>
		private Vector3 GetLandingFaceVector()
		{
			return m_gridFinder.Block.WorldMatrix.GetDirectionVector(Base6Directions.GetFlippedDirection(m_landingDirection));
		}

		/// <summary>
		/// If m_navBlock is a connector, attempt to lock it.
		/// </summary>
		private void LockConnector()
		{
			if (Globals.UpdateCount < next_attemptLock)
				return;
			next_attemptLock = Globals.UpdateCount + 20ul;

			Ingame.IMyShipConnector connector = m_navBlock.Block as Ingame.IMyShipConnector;
			if (connector != null && !connector.IsConnected && connector.IsLocked)
				MyAPIGateway.Utilities.TryInvokeOnGameThread(() => {
					if (!connector.IsConnected)
						connector.ApplyAction("Lock");
				});
		}

		/// <summary>
		/// Determines if the connector, landing gear, or merge block is locked. False if m_navBlock is none of those.
		/// </summary>
		private bool IsLocked()
		{
			if (m_landingState == LandingState.None)
				return false;

			if (m_beforeMerge)
				return true;

			IMyLandingGear asGear = m_navBlock.Block as IMyLandingGear;
			if (asGear != null)
				return asGear.IsLocked;

			Ingame.IMyShipConnector asConn = m_navBlock.Block as Ingame.IMyShipConnector;
			if (asConn != null)
			{
				//m_logger.debugLog("locked: " + asConn.IsLocked + ", connected: " + asConn.IsConnected + ", other: " + asConn.OtherConnector, "IsLocked()");
				return asConn.IsConnected;
			}

			return false;
		}

		private void MergeBlock_BeforeMerge()
		{
			(m_navBlock.Block as IMyShipMergeBlock).BeforeMerge -= MergeBlock_BeforeMerge;
			m_beforeMerge = true;
		}

	}
}
