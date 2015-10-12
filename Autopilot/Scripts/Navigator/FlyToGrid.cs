using System;
using System.Text;
using Rynchodon.Attached;
using Rynchodon.Autopilot.Data;
using Rynchodon.Autopilot.Movement;
using Sandbox.Common.ObjectBuilders;
using Sandbox.Game.Gui;
using Sandbox.ModAPI;
using VRageMath;
using Ingame = Sandbox.ModAPI.Ingame;

namespace Rynchodon.Autopilot.Navigator
{

	/*
	 * TODO:
	 * Stay in formation command
	 * Match roll for orient / dock
	 */

	public class FlyToGrid : NavigatorMover, INavigatorRotator
	{

		private enum LandingState : byte { None, Approach, Holding, LineUp, Landing }

		private static readonly TimeSpan SearchTimeout = new TimeSpan(0, 1, 0);

		private readonly Logger m_logger;
		private readonly GridFinder m_gridFinder;
		private readonly BlockNameOrientation m_targetBlock;
		private readonly PseudoBlock m_contBlock;
		private readonly PseudoBlock m_navBlock;
		private readonly AttachableBlockBase m_landAttach;
		/// <summary>m_targetBlock.Forward  or opposite of the landing face</summary>
		private readonly Base6Directions.Direction m_landingDirection;
		/// <summary>Half of length of landing block in the direction it will be landing.</summary>
		private readonly float m_landingHalfSize;

		private DateTime m_searchTimeoutAt = DateTime.UtcNow + SearchTimeout;
		private Vector3D m_targetPosition;
		private LandingState value_landingState = LandingState.None;
		private ulong next_attemptLock;

		private LandingState m_landingState
		{
			get { return value_landingState; }
			set
			{
				if (value_landingState == value)
					return;

				m_logger.debugLog("changing landing state to " + value, "get_m_landingState()", Logger.severity.DEBUG);
				value_landingState = value;

				switch (value)
				{
					case LandingState.Approach:
						{
							IMyFunctionalBlock asFunc = m_navBlock.Block as IMyFunctionalBlock;
							if (asFunc != null)
								MyAPIGateway.Utilities.TryOnGameThread(() => asFunc.RequestEnable(false), m_logger);
							break;
						}
					case LandingState.Landing:
						{
							IMyFunctionalBlock asFunc = m_navBlock.Block as IMyFunctionalBlock;
							if (asFunc != null)
								MyAPIGateway.Utilities.TryOnGameThread(() => asFunc.RequestEnable(true), m_logger);
							IMyLandingGear asGear = m_navBlock.Block as IMyLandingGear;
							if (asGear != null)
							{
								IMyCubeBlock block = m_navBlock.Block;
								MyAPIGateway.Utilities.TryOnGameThread(() => {
									if ( !(block.GetObjectBuilderCubeBlock() as MyObjectBuilder_LandingGear).AutoLock)
										asGear.ApplyAction("Autolock");
								}, m_logger);
							}
							break;
						}
				}

				m_navSet.OnTaskTertiaryComplete();
			}
		}

		public FlyToGrid(Mover mover, AllNavigationSettings navSet, string targetGrid,
			AttachedGrid.AttachmentKind allowedAttachment = AttachedGrid.AttachmentKind.Permanent)
			: base(mover, navSet)
		{
			this.m_logger = new Logger(GetType().Name, m_controlBlock.CubeBlock, () => m_landingState.ToString());
			this.m_targetBlock = m_navSet.Settings_Current.DestinationBlock;
			string blockName = m_targetBlock == null ? null : m_targetBlock.BlockName;
			this.m_gridFinder = new GridFinder(m_mover.Block, targetGrid, blockName, allowedAttachment);
			this.m_contBlock = m_navSet.Settings_Commands.NavigationBlock;

			PseudoBlock landingBlock = m_navSet.Settings_Current.LandingBlock;
			m_navBlock = landingBlock ?? m_navSet.Settings_Current.NavigationBlock;

			if (landingBlock != null)
			{
				if (!(landingBlock.Block is IMyShipMergeBlock) && !AttachableBlockBase.TryGet(landingBlock.Block.EntityId, out m_landAttach))
				{
					m_logger.debugLog("Failed to get AttachableBlockBase for " + landingBlock.Block.DisplayNameText, "FlyToGrid()", Logger.severity.DEBUG);
					m_landAttach = null;
				}
				else
					m_logger.debugLog("Got AttachableBlockBase for " + landingBlock.Block.DisplayNameText, "FlyToGrid()", Logger.severity.DEBUG);

				if (landingBlock.Block is IMyFunctionalBlock)
					m_landingState = LandingState.Approach;
				else
				{
					m_logger.debugLog("landingBlock is not functional, player error? : " + landingBlock.Block.DisplayNameText, "FlyToGrid()", Logger.severity.INFO);
					m_landingState = LandingState.None;
				}

				if (landingBlock.Block is Ingame.IMyShipConnector)
				{
					m_gridFinder.BlockCondition = block => {
						Ingame.IMyShipConnector connector = block as Ingame.IMyShipConnector;
						return connector != null && !connector.IsConnected;
					};
					m_landingDirection = m_targetBlock.Forward ?? Base6Directions.GetFlippedDirection(landingBlock.Block.GetFaceDirection()[0]);
				}
				else if (landingBlock.Block is IMyShipMergeBlock)
				{
					m_gridFinder.BlockCondition = block => block is IMyShipMergeBlock;
					m_landingDirection = m_targetBlock.Forward ?? Base6Directions.GetFlippedDirection(landingBlock.Block.GetFaceDirection()[0]);
				}
				else if (m_targetBlock.Forward.HasValue)
					m_landingDirection = m_targetBlock.Forward.Value;
				else
				{
					m_logger.debugLog("Player failed to specify landing direction and it could not be determined.", "FlyToGrid()", Logger.severity.INFO);
					m_landingState = LandingState.None;
				}

				m_landingHalfSize = landingBlock.Block.GetLengthInDirection(landingBlock.Block.LocalMatrix.GetClosestDirection(landingBlock.LocalMatrix.Forward)) * 0.5f;
				m_logger.debugLog("m_landing direction: " + m_landingDirection + ", m_landingBlockSize: " + m_landingHalfSize, "FlyToGrid()");
			}

			m_logger.debugLog("m_targetBlock: " + m_targetBlock, "FlyToGrid()");
			m_logger.debugLog("blockName: " + blockName, "FlyToGrid()");
			m_logger.debugLog(m_targetBlock != null, "target block forward: " + m_targetBlock.Forward + ", upward: " + m_targetBlock.Upward, "FlyToGrid()");

			m_navSet.Settings_Task_Secondary.NavigatorMover = this;
			if (m_navSet.Settings_Current.NavigatorRotator == null)
			{
				m_logger.debugLog("added as mover and rotator", "FlyToGrid()");
				m_navSet.Settings_Task_Secondary.NavigatorRotator = this;
			}
			else
				m_logger.debugLog("added as mover only", "FlyToGrid()");
		}

		public override void Move()
		{
			m_logger.debugLog(m_gridFinder == null, "m_gridFinder == null", "Move()", Logger.severity.FATAL);
			m_logger.debugLog(m_navSet == null, "m_navSet == null", "Move()", Logger.severity.FATAL);
			m_logger.debugLog(m_mover == null, "m_mover == null", "Move()", Logger.severity.FATAL);
			m_logger.debugLog(m_navBlock == null, "m_navBlock == null", "Move()", Logger.severity.FATAL);

			m_gridFinder.Update();

			if (m_gridFinder.Grid == null)
			{
				m_mover.StopMove();

				// only timeout if (Grid == null), ship could simply be waiting its turn
				if (DateTime.UtcNow > m_searchTimeoutAt)
				{
					m_logger.debugLog("Search timed out", "Move()", Logger.severity.INFO);
					m_navSet.OnTaskSecondaryComplete();
					m_mover.StopMove();
					m_mover.StopRotate();
					return;
				}

				if (m_landingState > LandingState.Approach)
				{
					m_logger.debugLog("Decreasing landing state from " + m_landingState + " to " + LandingState.Approach, "Move()", Logger.severity.DEBUG);
					m_landingState = LandingState.Approach;
				}

				return;
			}
			else
			{
				//if (m_gridFinder.m_targetBlockName == null || m_gridFinder.Block != null)
				m_searchTimeoutAt = DateTime.UtcNow + SearchTimeout;

				if (m_landingState > LandingState.Approach || m_navSet.Settings_Current.Distance < m_navSet.Settings_Current.DestinationRadius)
				{
					Move_Land();
					return;
				}

				m_targetPosition = m_gridFinder.GetPostition(m_navBlock.WorldPosition, m_navSet.Settings_Current.DestinationOffset);

				//if (m_gridFinder.Block != null)
				//	m_logger.debugLog("Block position: " + m_gridFinder.Block.GetPosition(), "Move()");
				//m_logger.debugLog("Target position: " + m_targetPosition, "Move()");

				m_mover.CalcMove(m_navBlock, m_targetPosition, m_gridFinder.Grid.GetLinearVelocity());
			}
		}

		public void Rotate()
		{
			if (m_gridFinder.Grid == null)
			{
				m_mover.StopRotate();
				return;
			}

			if (m_navSet.Settings_Current.Distance > m_navSet.Settings_Current.DestinationRadius)
			{
				//m_logger.debugLog("facing controller towards target : " + m_targetPosition, "Rotate()");
				m_mover.CalcRotate(m_contBlock, RelativeDirection3F.FromWorld(m_contBlock.Grid, m_targetPosition - m_contBlock.WorldPosition));
				return;
			}

			if (m_gridFinder.Block == null)
			{
				m_logger.debugLog("no Block, not facing", "Rotate()");
				m_mover.StopRotate();
				return;
			}

			if (m_landingState != LandingState.None)
			{
				//m_logger.debugLog("rotating for landing", "Rotate()");
				m_mover.CalcRotate(m_navBlock, m_gridFinder.Block, m_landingDirection, m_targetBlock.Upward);
				return;
			}

			if (m_targetBlock.Forward.HasValue)
			{
				m_navSet.Settings_Task_Primary.NavigatorRotator = this;
				if (m_navSet.DirectionMatched())
				{
					m_logger.debugLog("Direction matched", "Rotate()", Logger.severity.INFO);
					m_navSet.OnTaskPrimaryComplete();
					m_mover.StopMove(true);
					m_mover.StopRotate();
					return;
				}

				//m_logger.debugLog("matching target direction", "Rotate()");
				m_mover.CalcRotate(m_navBlock, m_gridFinder.Block, m_targetBlock.Forward, m_targetBlock.Upward);
				return;
			}

			m_mover.StopRotate();
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

				customInfo.Append("Distance: ");
				customInfo.AppendLine(m_navSet.PrettyDistance());
			}
			else
			{
				customInfo.Append("Flying to ");
				customInfo.Append(m_gridFinder.Block.DisplayNameText);
				customInfo.Append(" on ");
				customInfo.AppendLine(m_gridFinder.Grid.Entity.DisplayName);

				customInfo.Append("Distance: ");
				customInfo.AppendLine(m_navSet.PrettyDistance());
			}

			if (m_landingState != LandingState.None)
			{
				customInfo.Append("Landing: ");
				customInfo.AppendLine(m_landingState.ToString());
			}
		}

		private void Move_Land()
		{
			if (m_landAttach != null && m_landAttach.IsAttached)
			{
				m_logger.debugLog("Attached!", "Move_Land()", Logger.severity.INFO);
				m_navSet.OnTaskPrimaryComplete();
				m_mover.StopMove(false);
				m_mover.StopRotate();
				return;
			}

			switch (m_landingState)
			{
				case LandingState.None:
					//if (formation)
					{
						m_logger.debugLog("Arrived at target", "Move_Land()", Logger.severity.INFO);
						m_navSet.OnTaskSecondaryComplete();
						m_mover.StopMove();
						m_mover.StopRotate();
					}
					//else
					//{
					//	m_logger.debugLog("Maintaining relative position to target", "Move_Land()");
					//	m_mover.CalcMove(m_navBlock, m_navBlock.WorldPosition, m_gridFinder.Grid.GetLinearVelocity());
					//}
					return;

				case LandingState.Approach:
					m_landingState = LandingState.Holding;
					m_navSet.Settings_Task_Primary.NavigatorRotator = this;
					goto case LandingState.Holding;
				case LandingState.Holding:
					Vector3D navPos = m_navBlock.WorldPosition;
					Vector3D destination = m_gridFinder.GetPostition(navPos, m_navSet.Settings_Current.DestinationOffset);
					Vector3 directAway = navPos - destination;
					if (directAway.LengthSquared() < 1)
					{
						destination = m_gridFinder.Grid.Entity.WorldAABB.Center;
						directAway = navPos - destination;
					}
					Vector3D targetPosition = destination + Vector3.Normalize( directAway) * m_navSet.Settings_Current.DestinationRadius;

					m_logger.debugLog("destination: " + destination + ", directAway: " + Vector3.Normalize(directAway) + ", DestinationRadius: " + m_navSet.Settings_Current.DestinationRadius + ", targetPosition: " + targetPosition, "Move_Land()");

					m_mover.CalcMove(m_navBlock, targetPosition, m_gridFinder.Grid.GetLinearVelocity());
					if (m_gridFinder.Block != null)
					{
						m_logger.debugLog("Have a block, starting landing sequence", "Move_Land()", Logger.severity.DEBUG);
						m_landingState = LandingState.LineUp;
					}
					else
						m_logger.debugLog("no block", "Move_Land()");
					return;
				case LandingState.LineUp:
					if (m_gridFinder.Block == null)
					{
						m_logger.debugLog("lost block", "Move_Land()");
						m_landingState = LandingState.Holding;
						return;
					}

					if (m_navSet.Settings_Current.Distance < 1)
					{
						m_logger.debugLog("Reached line: " + m_navSet.Settings_Current.Distance, "Move_Land()");
						m_landingState = LandingState.Landing;
						return;
					}

					// move to line from target block outwards
					Vector3D targetBlockPosition = m_gridFinder.Block.GetPosition();
					Vector3D landFaceVector = GetLandingFaceVector();
					Line destinationLine = new Line(targetBlockPosition + landFaceVector * 10, targetBlockPosition + landFaceVector * 1000);
					Vector3D closestPoint = destinationLine.ClosestPoint(m_navBlock.WorldPosition);

					m_logger.debugLog("Flying to closest point on line between " + destinationLine.From + " and " + destinationLine.To + " which is " + closestPoint, "Move_Land()");
					m_mover.CalcMove(m_navBlock, closestPoint, m_gridFinder.Grid.GetLinearVelocity());

					return;
				case LandingState.Landing:
					if (m_gridFinder.Block == null)
					{
						m_logger.debugLog("lost block", "Move_Land()");
						m_landingState = LandingState.Holding;
						return;
					}

					if (m_navSet.Settings_Current.DistanceAngle > 0.1f)
					{
						m_logger.debugLog("waiting for direction to match", "Move_Land()");
						m_mover.StopMove();
						return;
					}

					LockConnector();

					float distanceBetween = m_gridFinder.Block.GetLengthInDirection(m_landingDirection) * 0.5f + m_landingHalfSize;
					m_mover.CalcMove(m_navBlock, m_gridFinder.Block.GetPosition() + GetLandingFaceVector() * distanceBetween, m_gridFinder.Grid.GetLinearVelocity());
					return;
			}
		}

		private Vector3 GetLandingFaceVector()
		{
			return m_gridFinder.Block.WorldMatrix.GetDirectionVector(Base6Directions.GetFlippedDirection(m_landingDirection));
		}

		private void LockConnector()
		{
			if (Globals.UpdateCount < next_attemptLock)
				return;
			next_attemptLock = Globals.UpdateCount + 20ul;

			Ingame.IMyShipConnector connector = m_navBlock.Block as Ingame.IMyShipConnector;
			if (connector != null && !connector.IsConnected)
				MyAPIGateway.Utilities.TryOnGameThread(() => {
					if (!connector.IsConnected)
						connector.ApplyAction("Lock");
				}, m_logger);
		}

	}
}
