using System;
using System.Text;
using Rynchodon.Attached;
using Rynchodon.Autopilot.Data;
using Rynchodon.Utility;
using Sandbox.Common.ObjectBuilders;
using Sandbox.ModAPI;
using SpaceEngineers.Game.ModAPI;
using VRage.Game.ModAPI;
using Rynchodon.Autopilot.Pathfinding;
using VRageMath;
using Ingame = Sandbox.ModAPI.Ingame;

namespace Rynchodon.Autopilot.Navigator
{

	class FlyToGrid : ALand
	{

		public enum LandingState : byte { None, Approach, Holding, LineUp, Landing, Catch }

		private static readonly TimeSpan SearchTimeout = new TimeSpan(0, 1, 0);

		private readonly GridFinder m_gridFinder;
		private readonly BlockNameOrientation m_targetBlock;
		/// <summary>m_targetBlock.Forward  or opposite of the landing face</summary>
		private readonly Base6Directions.Direction m_landingDirection;
		/// <summary>Half of length of landing block in the direction it will be landing.</summary>
		private readonly float m_landingHalfSize;
		private readonly bool m_landGearWithoutTargetBlock;
		private readonly AllNavigationSettings.SettingsLevelName m_settingLevel;
		private readonly bool m_landingFriend;

		private TimeSpan m_searchTimeoutAt = Globals.ElapsedTime + SearchTimeout;
		//private Vector3D m_targetOffset;//, m_targetPosition;
		private Destination m_destination;
		private LandingState value_landingState = LandingState.None;
		private Vector3I m_previousCell;
		//private float m_landingSpeedFudge;
		private ulong next_attemptLock;
		private bool m_beforeMerge;

		public LandingState m_landingState
		{
			get { return value_landingState; }
			private set
			{
				if (value_landingState == value)
					return;

				Log.DebugLog("changing landing state to " + value, Logger.severity.DEBUG);
				value_landingState = value;

				switch (value)
				{
					case LandingState.Catch:
					case LandingState.Landing:
						{
							m_navSet.GetSettingsLevel(m_settingLevel).DestinationEntity = m_gridFinder.Block != null ? m_gridFinder.Block : m_gridFinder.Grid.Entity;

							IMyFunctionalBlock asFunc = m_navBlock.Block as IMyFunctionalBlock;
							if (asFunc != null && !asFunc.Enabled)
							{
								Log.DebugLog("Enabling m_navBlock: " + m_navBlock.Block.DisplayNameText, Logger.severity.DEBUG);
								MyAPIGateway.Utilities.TryInvokeOnGameThread(() => asFunc.Enabled = true);
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
					default:
						m_navSet.GetSettingsLevel(m_settingLevel).DestinationEntity = null;
						break;
				}

				m_navSet.OnTaskComplete_NavWay();
				m_navSet.Settings_Task_NavWay.PathfinderCanChangeCourse = value != LandingState.Catch && value != LandingState.Landing;
			}
		}

		private Logable Log
		{
			get { return new Logable(m_controlBlock.CubeBlock, m_landingState.ToString()); }
		}

		public FlyToGrid(Pathfinder pathfinder, string targetGrid = null, AttachedGrid.AttachmentKind allowedAttachment = AttachedGrid.AttachmentKind.Permanent, GridFinder finder = null, PseudoBlock landingBlock = null)
			: base(pathfinder)
		{
			this.m_targetBlock = m_navSet.Settings_Current.DestinationBlock;
			string blockName = m_targetBlock == null ? null : m_targetBlock.BlockName;
			if (finder != null)
				this.m_gridFinder = finder;
			else
				this.m_gridFinder = new GridFinder(m_navSet, m_controlBlock, targetGrid, blockName, allowedAttachment);
			this.m_landingFriend = !(this.m_gridFinder is EnemyFinder);

			if (landingBlock == null)
				landingBlock = m_navSet.Settings_Current.LandingBlock;
			m_navSet.Settings_Task_NavRot.NavigationBlock = landingBlock;

			if (landingBlock != null)
			{
				if (landingBlock.Block is IMyFunctionalBlock)
					m_landingState = LandingState.Approach;
				else
				{
					Log.DebugLog("landingBlock is not functional, player error? : " + landingBlock.Block.DisplayNameText, Logger.severity.INFO);
					m_landingState = LandingState.None;
				}

				if (m_targetBlock == null)
				{
					if (!(landingBlock.Block is IMyLandingGear))
					{
						Log.DebugLog("cannot land block without a target", Logger.severity.INFO);
						m_landingState = LandingState.None;
					}
					else
					{
						Log.DebugLog("golden retriever mode enabled", Logger.severity.INFO);
						m_landGearWithoutTargetBlock = true;
					}
				}
				else if (landingBlock.Block is Ingame.IMyShipConnector)
				{
					m_gridFinder.BlockCondition = block => {
						Ingame.IMyShipConnector connector = block as Ingame.IMyShipConnector;
						return connector != null && (connector.Status == Ingame.MyShipConnectorStatus.Unconnected || connector.OtherConnector == m_navBlock.Block) && CanReserveTarget(connector.EntityId);
					};
					m_landingDirection = m_targetBlock.Forward ?? Base6Directions.GetFlippedDirection(landingBlock.Block.FirstFaceDirection());
				}
				else if (landingBlock.Block is IMyShipMergeBlock)
				{
					m_gridFinder.BlockCondition = block => block is IMyShipMergeBlock && CanReserveTarget(block.EntityId);
					m_landingDirection = m_targetBlock.Forward ?? Base6Directions.GetFlippedDirection(landingBlock.Block.FirstFaceDirection());
					(landingBlock.Block as IMyShipMergeBlock).BeforeMerge += MergeBlock_BeforeMerge;
				}
				else if (m_targetBlock.Forward.HasValue)
					m_landingDirection = m_targetBlock.Forward.Value;
				else
				{
					Log.DebugLog("Player failed to specify landing direction and it could not be determined.", Logger.severity.INFO);
					m_landingState = LandingState.None;
				}

				if (m_landingState != LandingState.None)
				{
					//float minDestRadius = m_controlBlock.CubeGrid.GetLongestDim() * 5f;
					//if (m_navSet.Settings_Current.DestinationRadius < minDestRadius)
					//{
					//	Log.DebugLog("Increasing DestinationRadius from " + m_navSet.Settings_Current.DestinationRadius + " to " + minDestRadius, "FlyToGrid()", Logger.severity.DEBUG);
					//	m_navSet.Settings_Task_NavRot.DestinationRadius = minDestRadius;
					//}

					new UnLander(m_pathfinder, landingBlock);

					m_landingHalfSize = landingBlock.Block.GetLengthInDirection(landingBlock.Block.LocalMatrix.GetClosestDirection(landingBlock.LocalMatrix.Forward)) * 0.5f;
					Log.DebugLog("m_landing direction: " + m_landingDirection + ", m_landingBlockSize: " + m_landingHalfSize);
				}
			}

			m_settingLevel = m_landingState != LandingState.None ? AllNavigationSettings.SettingsLevelName.NavRot : AllNavigationSettings.SettingsLevelName.NavMove;
			m_navSet.GetSettingsLevel(m_settingLevel).NavigatorMover = this;
		}

		public override void Move()
		{
			Log.DebugLog("m_gridFinder == null", Logger.severity.FATAL, condition: m_gridFinder == null);
			Log.DebugLog("m_navSet == null", Logger.severity.FATAL, condition: m_navSet == null);
			Log.DebugLog("m_mover == null", Logger.severity.FATAL, condition: m_mover == null);
			Log.DebugLog("m_navBlock == null", Logger.severity.FATAL, condition: m_navBlock == null);

			m_gridFinder.Update();

			if (m_gridFinder.Grid == null)
			{
				Log.DebugLog("searching");
				m_mover.StopMove();

				// only timeout if (Grid == null), ship could simply be waiting its turn
				if (Globals.ElapsedTime > m_searchTimeoutAt)
				{
					Log.DebugLog("Search timed out", Logger.severity.INFO);
					m_navSet.OnTaskComplete(m_settingLevel);
					UnreserveTarget();
					m_mover.StopMove();
					m_mover.StopRotate();
					return;
				}

				if (m_landingState > LandingState.Approach)
				{
					Log.DebugLog("Decreasing landing state from " + m_landingState + " to " + LandingState.Approach, Logger.severity.DEBUG);
					m_landingState = LandingState.Approach;
				}

				return;
			}
			else
			{
				m_searchTimeoutAt = Globals.ElapsedTime + SearchTimeout;

				if (!m_gridFinder.Grid.isRecent())
				{
					m_pathfinder.MoveTo(m_gridFinder.Grid);
					return;
				}

				if (m_gridFinder.Block != null)
				{
					m_destination = Destination.FromWorld(m_gridFinder.Block, m_navSet.Settings_Current.DestinationOffset.ToWorld(m_gridFinder.Block));
				}
				else
				{
					IMyCubeGrid grid  = (IMyCubeGrid)m_gridFinder.Grid.Entity;
					CubeGridCache cache = CubeGridCache.GetFor(grid);
					if (cache == null)
						return;
					m_previousCell = cache.GetClosestOccupiedCell(m_navBlock.WorldPosition, m_previousCell);
					m_destination = Destination.FromWorld(grid, grid.GridIntegerToWorld(m_previousCell));
				}

				if (m_landingState > LandingState.Approach)
				{
					Move_Land();
					return;
				}

				if (m_landingState == LandingState.None && m_navSet.Settings_Current.Stay_In_Formation)
				{
					Log.DebugLog("Maintaining offset position from target", condition: m_navSet.DistanceLessThanDestRadius());
					m_pathfinder.MoveTo(destinations: m_destination);
					return;
				}

				if (m_navSet.DistanceLessThanDestRadius())
				{
					if (m_landingState == LandingState.None)
					{
						if (m_targetBlock != null && m_targetBlock.Forward.HasValue ? m_navSet.DirectionMatched() : m_navBlock.Physics.AngularVelocity.LengthSquared() < 0.01f)
						{
							Log.DebugLog("Arrived at target", Logger.severity.INFO);
							m_navSet.OnTaskComplete(m_settingLevel);
							UnreserveTarget();
							m_mover.StopRotate();
						}
						m_mover.StopMove();
						return;
					}

					Move_Land();
					return;
				}

				m_pathfinder.MoveTo(destinations: m_destination);
			}
		}

		public override void Rotate()
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
					//Log.DebugLog("matching target direction", "Rotate()");
					m_mover.CalcRotate(m_navBlock, m_gridFinder.Block, m_targetBlock.Forward, m_targetBlock.Upward);
					return;
				}
				else
					m_mover.CalcRotate();
				return;
			}

			if (m_landingState == LandingState.Approach)
			{
				//Log.DebugLog("facing controller towards target : " + m_targetPosition, "Rotate()");
				m_mover.CalcRotate();
				return;
			}

			if (m_gridFinder.Block == null)
			{
				if (m_landGearWithoutTargetBlock)
				{
					m_mover.CalcRotate(m_navBlock, RelativeDirection3F.FromWorld(m_navBlock.Grid, m_destination.WorldPosition() - m_navBlock.WorldPosition));
					return;
				}

				m_mover.CalcRotate();
				return;
			}

			//Log.DebugLog("rotating for landing", "Rotate()");
			if (IsLocked())
			{
				Log.DebugLog("already landed");
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
				Log.DebugLog("Attached!", Logger.severity.INFO);
				m_navSet.OnTaskComplete(m_settingLevel);
				UnreserveTarget();
				m_mover.StopMove(false);
				m_mover.StopRotate();
				if (m_navSet.Shopper != null)
				{
					Log.DebugLog("starting shopper");
					m_navSet.Shopper.Start();
				}
				return;
			}

			switch (m_landingState)
			{
				case LandingState.None:
					{
						throw new Exception("landing state is None");
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
						if (m_gridFinder.Block != null && ReserveTarget(m_gridFinder.Block.EntityId))
						{
							Log.DebugLog("Have a block, starting landing sequence", Logger.severity.DEBUG);
							m_landingState = LandingState.LineUp;
							return;
						}
						m_pathfinder.HoldPosition(m_gridFinder.Grid);
						return;
					}
				case LandingState.LineUp:
					{
						if (m_gridFinder.Block == null)
						{
							Log.DebugLog("lost block");
							m_landingState = LandingState.Holding;
							return;
						}

						if (m_navSet.DirectionMatched())
						{
							if (m_navSet.Settings_Current.Distance < 1f)
							{
								Log.DebugLog("Reached line: " + m_navSet.Settings_Current.Distance);
								m_landingState = LandingState.Landing;
								return;
							}
						}
						//else if (m_pathfinder.RotateCheck.ObstructingEntity != null)
						//{
						//	Vector3D destinationWorld = m_destination.WorldPosition();
						//	Vector3 directAway = m_navBlock.WorldPosition - destinationWorld;
						//	if (directAway.LengthSquared() < 1)
						//	{
						//		destinationWorld = m_gridFinder.Grid.Entity.WorldAABB.Center;
						//		directAway = m_navBlock.WorldPosition - destinationWorld;
						//	}
						//	Vector3D targetPosition = destinationWorld + Vector3.Normalize(directAway) * m_navSet.Settings_Current.DestinationRadius;

						//	Log.DebugLog("Pathfinder cannot rotate, moving away. destination: " + destinationWorld + ", directAway: " + Vector3.Normalize(directAway) +
						//		", DestinationRadius: " + m_navSet.Settings_Current.DestinationRadius + ", targetPosition: " + targetPosition);

						//	m_destination.SetWorld(ref targetPosition);
						//	m_pathfinder.MoveTo(destinations: m_destination);
						//	return;
						//}
						else if (m_navSet.Settings_Current.Distance < 1f)
						{
							// replace do nothing (line) or other rotator that is preventing ship from landing
							m_navSet.Settings_Task_NavWay.NavigatorRotator = this;
						}

						// move to line from target block outwards
						Vector3D landFaceVector = GetLandingFaceVector();
						float distanceBetween = m_gridFinder.Block.GetLengthInDirection(m_landingDirection) * 0.5f + m_landingHalfSize + 1f;
						Vector3D destWorld = m_destination.WorldPosition();
						LineSegmentD destinationLine = new LineSegmentD(destWorld + landFaceVector * distanceBetween, destWorld + landFaceVector * 1000f);

						Vector3D closestPoint = destinationLine.ClosestPoint(m_navBlock.WorldPosition);

						Log.DebugLog("Flying to closest point to " + m_navBlock.WorldPosition + " on line between " + destinationLine.From + " and " + destinationLine.To + " which is " + closestPoint);
						Log.DebugLog("missing block", Logger.severity.ERROR, condition: m_gridFinder.Block == null);
						m_destination = Destination.FromWorld(m_gridFinder.Block, closestPoint);
						m_pathfinder.MoveTo(destinations: m_destination);
						return;
					}
				case LandingState.Landing:
					{
						if (m_gridFinder.Block == null)
						{
							Log.DebugLog("lost block");
							m_landingState = LandingState.Holding;
							return;
						}

						if (m_navSet.Settings_Current.DistanceAngle > 0.1f)
						{
							Log.DebugLog("waiting for direction to match");
							m_pathfinder.HoldPosition(m_gridFinder.Grid);
							return;
						}

						LockConnector();

						float distanceBetween = m_gridFinder.Block.GetLengthInDirection(m_landingDirection) * 0.5f + m_landingHalfSize + 0.1f;
						//Log.DebugLog("moving to " + (m_targetPosition + GetLandingFaceVector() * distanceBetween) + ", distance: " + m_navSet.Settings_Current.Distance, "Move_Land()");

						//if (m_navSet.DistanceLessThan(1f))
						//{
						//	m_landingSpeedFudge += 0.0001f;
						//	if (m_landingSpeedFudge > 0.1f)
						//		m_landingSpeedFudge = -0.1f;
						//}

						m_destination.Position += GetLandingFaceVector() * distanceBetween;
						m_pathfinder.MoveTo(destinations: m_destination);
						return;
					}
				case LandingState.Catch:
					{
						if (m_navSet.Settings_Current.DistanceAngle > 0.1f)
						{
							Log.DebugLog("waiting for direction to match");
							m_pathfinder.HoldPosition(m_gridFinder.Grid);
							return;
						}

						//if (m_navSet.DistanceLessThan(1f))
						//{
						//	m_landingSpeedFudge += 0.0001f;
						//	if (m_landingSpeedFudge > 0.1f)
						//		m_landingSpeedFudge = -0.1f;
						//	Log.DebugLog("target position: " + m_destination.WorldPosition() + ", nav position: " + m_navBlock.WorldPosition + ", distance: " + Vector3D.Distance(m_destination.WorldPosition(), m_navBlock.WorldPosition) + "/" + m_navSet.Settings_Current.Distance + ", fudge: " + m_landingSpeedFudge);
						//}
						//m_destination.Position += m_gridFinder.Grid.GetLinearVelocity() * m_landingSpeedFudge;
						m_pathfinder.MoveTo(destinations: m_destination);
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

			IMyShipConnector connector = m_navBlock.Block as IMyShipConnector;
			if (connector != null && connector.Status == Ingame.MyShipConnectorStatus.Connectable)
				MyAPIGateway.Utilities.TryInvokeOnGameThread(() => {
					if (connector.Status == Ingame.MyShipConnectorStatus.Connectable)
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

			IMyShipConnector asConn = m_navBlock.Block as IMyShipConnector;
			if (asConn != null)
			{
				//Log.DebugLog("locked: " + asConn.IsLocked + ", connected: " + asConn.IsConnected + ", other: " + asConn.OtherConnector, "IsLocked()");
				return asConn.Status == Ingame.MyShipConnectorStatus.Connected;
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
