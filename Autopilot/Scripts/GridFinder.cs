using System;
using Rynchodon.AntennaRelay;
using Rynchodon.Attached;
using Sandbox.ModAPI;
using VRageMath;

namespace Rynchodon.Autopilot
{
	/// <summary>
	/// Finds a friendly grid and possibly a block, given a ship controller block.
	/// </summary>
	public class GridFinder
	{

		private const ulong SearchInterval_Grid = 100ul, SearchInterval_Block = 1000ul, SearchTimeout = 10000ul;

		public readonly string m_targetGridName, m_targetBlockName;
		public readonly ShipController m_controller;
		public readonly AttachedGrid.AttachmentKind m_allowedAttachment;

		private readonly Logger m_logger;

		private ulong NextSearch_Grid, NextSearch_Block, TimeoutAt = ulong.MaxValue;

		public LastSeen Grid { get; private set; }
		public IMyCubeBlock Block { get; private set; }
		/// <summary>Block requirements, other than can control.</summary>
		public Func<IMyCubeBlock, bool> BlockCondition { get; set; }

		public GridFinder(ShipControllerBlock controller, string targetGrid, string targetBlock = null,
			AttachedGrid.AttachmentKind allowedAttachment = AttachedGrid.AttachmentKind.Permanent)
		{
			this.m_logger = new Logger(GetType().Name, controller.CubeBlock);

			m_logger.debugLog(controller == null, "controller == null", "GridFinder()", Logger.severity.FATAL);
			m_logger.debugLog(targetGrid == null, "targetGrid == null", "GridFinder()", Logger.severity.FATAL);

			if (!Registrar.TryGetValue(controller.CubeBlock.EntityId, out this.m_controller))
				throw new NullReferenceException("ShipControllerBlock is not a ShipController");
			this.m_targetGridName = targetGrid.LowerRemoveWhitespace();
			if (targetBlock != null)
				this.m_targetBlockName = targetBlock.LowerRemoveWhitespace();
			this.m_allowedAttachment = allowedAttachment;
		}

		public void Update()
		{
			if (Grid == null)
			{
				if (Globals.UpdateCount >= NextSearch_Grid)
					GridSearch();
			}
			else
				GridUpdate();

			if (Grid != null && m_targetBlockName != null)
			{
				if (Block == null)
				{
					if (Globals.UpdateCount >= NextSearch_Block)
						BlockSearch();
				}
				else
					BlockCheck();
			}
		}

		public Vector3D GetPosition(Vector3D NavPos, Vector3D blockOffset)
		{
			return !Grid.isRecent() ? Grid.predictPosition()
				: Block != null ? GetBlockPosition(blockOffset)
				: GridCellCache.GetCellCache(Grid.Entity as IMyCubeGrid).GetClosestOccupiedCell(NavPos);
		}

		private Vector3D GetBlockPosition(Vector3D blockOffset)
		{
			MatrixD WorldMatrix = Block.WorldMatrix;
			return Block.GetPosition() + WorldMatrix.Right * blockOffset.X + WorldMatrix.Up * blockOffset.Y + WorldMatrix.Backward * blockOffset.Z;
		}

		/// <summary>
		/// Find a seen grid that matches m_targetGridName
		/// </summary>
		private void GridSearch()
		{
			NextSearch_Grid = Globals.UpdateCount + SearchInterval_Grid;

			int bestNameLength = int.MaxValue;
			m_controller.ForEachLastSeen(seen => {

				IMyCubeGrid grid = seen.Entity as IMyCubeGrid;
				if (grid != null && grid.DisplayName.Length < bestNameLength && grid.DisplayName.LowerRemoveWhitespace().Contains(m_targetGridName))
				{
					Grid = seen;
					bestNameLength = grid.DisplayName.Length;
					if (bestNameLength == m_targetGridName.Length)
					{
						m_logger.debugLog("perfect match LastSeen: " + seen.Entity.getBestName(), "GridSearch()");
						return true;
					}
				}
				return false;
			});

			if (Grid != null)
				m_logger.debugLog("Best match LastSeen: " + Grid.Entity.getBestName(), "GridSearch()");
		}

		private void GridUpdate()
		{
			m_logger.debugLog(Grid == null, "Grid == null", "GridUpdate()", Logger.severity.FATAL);

			LastSeen updated;
			if (!m_controller.tryGetLastSeen(Grid.Entity.EntityId, out updated))
			{
				m_logger.alwaysLog("Where does the good go?", "GridUpdate()", Logger.severity.WARNING);
				updated = null;
			}

			Grid = updated;
		}

		/// <summary>
		/// Find a block that matches m_targetBlockName.
		/// </summary>
		private void BlockSearch()
		{
			m_logger.debugLog(Grid == null, "Grid == null", "BlockSearch()", Logger.severity.FATAL);
			m_logger.debugLog(m_targetBlockName == null, "m_targetBlockName == null", "BlockSearch()", Logger.severity.FATAL);

			NextSearch_Block = Globals.UpdateCount + SearchInterval_Block;
			Block = null;

			int bestNameLength = int.MaxValue;
			IMyCubeGrid asGrid = Grid.Entity as IMyCubeGrid;
			m_logger.debugLog(asGrid == null, "asGrid == null", "BlockSearch()", Logger.severity.FATAL);

			AttachedGrid.RunOnAttachedBlock(asGrid, m_allowedAttachment, slim => {
				IMyCubeBlock Fatblock = slim.FatBlock;
				if (Fatblock == null || !m_controller.CubeBlock.canControlBlock(Fatblock))
					return false;

				string blockName = ShipController_Autopilot.IsAutopilotBlock(Fatblock)
						? Fatblock.getNameOnly().LowerRemoveWhitespace()
						: Fatblock.DisplayNameText.LowerRemoveWhitespace();

				if (BlockCondition != null && !BlockCondition(Fatblock))
					return false;

				m_logger.debugLog("checking block name: \"" + blockName + "\" contains \"" + m_targetBlockName + "\"", "BlockSearch()");
				if (blockName.Length < bestNameLength && blockName.Contains(m_targetBlockName))
				{
					m_logger.debugLog("block name matches: " + Fatblock.DisplayNameText, "BlockSearch()");
					Block = Fatblock;
					bestNameLength = blockName.Length;
					if (m_targetBlockName.Length == bestNameLength)
						return true;
				}
				return false;

			}, true);
		}

		private void BlockCheck()
		{
			m_logger.debugLog(Grid == null, "Grid == null", "GridUpdate()", Logger.severity.FATAL);
			m_logger.debugLog(m_targetBlockName == null, "m_targetBlockName == null", "GridUpdate()", Logger.severity.FATAL);
			m_logger.debugLog(Block == null, "Block == null", "GridUpdate()", Logger.severity.FATAL);

			if (!m_controller.CubeBlock.canControlBlock(Block))
			{
				m_logger.debugLog("lost control of block: " + Block.DisplayNameText, "BlockCheck()", Logger.severity.DEBUG);
				Block = null;
				return;
			}

			if (BlockCondition != null && !BlockCondition(Block))
			{
				m_logger.debugLog("Block does not statisfy condition: " + Block.DisplayNameText, "BlockCheck()", Logger.severity.DEBUG);
				Block = null;
				return;
			}
		}

	}
}
