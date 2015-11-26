using System;
using System.Collections.Generic;
using System.Linq;
using Rynchodon.AntennaRelay;
using Rynchodon.Attached;
using Rynchodon.Autopilot.Data;
using Sandbox.ModAPI;
using VRageMath;

namespace Rynchodon.Autopilot
{
	/// <summary>
	/// Finds a friendly grid and possibly a block, given a ship controller block.
	/// </summary>
	public class GridFinder
	{

		public enum ReasonCannotTarget : byte { None, Too_Far, Grid_Condition, Too_Fast }

		private const ulong SearchInterval_Grid = 100ul, SearchInterval_Block = 1000ul;

		public readonly string m_targetGridName, m_targetBlockName;
		public readonly ShipController m_controller;
		public readonly ShipControllerBlock m_controlBlock;
		public readonly AttachedGrid.AttachmentKind m_allowedAttachment;
		public readonly Vector3 m_startPosition;

		private readonly Logger m_logger;
		private readonly AllNavigationSettings m_navSet;
		private readonly bool m_mustBeRecent;

		private ulong NextSearch_Grid, NextSearch_Block;
		private List<LastSeen> m_enemies;

		public virtual LastSeen Grid { get; protected set; }
		public IMyCubeBlock Block { get; private set; }
		public Func<IMyCubeGrid, bool> GridCondition { get; set; }
		/// <summary>Block requirements, other than can control.</summary>
		public Func<IMyCubeBlock, bool> BlockCondition { get; set; }
		public ReasonCannotTarget m_reason;
		public long m_reasonGrid;

		protected float MaximumRange { get; set; }

		/// <summary>
		/// Creates a GridFinder to find a friendly grid based on its name.
		/// </summary>
		public GridFinder(AllNavigationSettings navSet, ShipControllerBlock controller, string targetGrid, string targetBlock = null,
			AttachedGrid.AttachmentKind allowedAttachment = AttachedGrid.AttachmentKind.Permanent)
		{
			this.m_logger = new Logger(GetType().Name + " friendly", controller.CubeBlock);

			m_logger.debugLog(navSet == null, "navSet == null", "GridFinder()", Logger.severity.FATAL);
			m_logger.debugLog(controller == null, "controller == null", "GridFinder()", Logger.severity.FATAL);
			m_logger.debugLog(controller.CubeBlock == null, "controller.CubeBlock == null", "GridFinder()", Logger.severity.FATAL);
			m_logger.debugLog(targetGrid == null, "targetGrid == null", "GridFinder()", Logger.severity.FATAL);

			if (!Registrar.TryGetValue(controller.CubeBlock.EntityId, out this.m_controller))
				throw new NullReferenceException("ShipControllerBlock is not a ShipController");
			this.m_targetGridName = targetGrid.LowerRemoveWhitespace();
			if (targetBlock != null)
				this.m_targetBlockName = targetBlock.LowerRemoveWhitespace();
			this.m_controlBlock = controller;
			this.m_allowedAttachment = allowedAttachment;
			this.m_startPosition = m_controlBlock.CubeBlock.GetPosition();
			this.MaximumRange = float.MaxValue;
			this.m_navSet = navSet;
		}

		/// <summary>
		/// Creates a GridFinder to find an enemy grid based on distance.
		/// </summary>
		public GridFinder(AllNavigationSettings navSet, ShipControllerBlock controller, float maxRange = 0f)
		{
			this.m_logger = new Logger(GetType().Name + " enemy", controller.CubeBlock);

			m_logger.debugLog(navSet == null, "navSet == null", "GridFinder()", Logger.severity.FATAL);
			m_logger.debugLog(controller == null, "controller == null", "GridFinder()", Logger.severity.FATAL);
			m_logger.debugLog(controller.CubeBlock == null, "controller.CubeBlock == null", "GridFinder()", Logger.severity.FATAL);

			this.m_controlBlock = controller;
			this.m_enemies = new List<LastSeen>();

			if (!Registrar.TryGetValue(controller.CubeBlock.EntityId, out this.m_controller))
				throw new NullReferenceException("ShipControllerBlock is not a ShipController");

			this.m_startPosition = m_controlBlock.CubeBlock.GetPosition();
			this.MaximumRange = maxRange;
			this.m_navSet = navSet;
			this.m_mustBeRecent = true;
		}

		public void Update()
		{
			m_logger.debugLog("entered Update()", "Update()");
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
				: GridCellCache.GetCellCache(Grid.Entity as IMyCubeGrid).GetClosestOccupiedCellPosition(NavPos);
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

			if (m_targetGridName != null)
				GridSearch_Friend();
			else
				GridSearch_Enemy();
		}

		private void GridSearch_Friend()
		{
			int bestNameLength = int.MaxValue;
			m_controller.ForEachLastSeen(seen => {
				IMyCubeGrid grid = seen.Entity as IMyCubeGrid;
				if (grid != null && grid.DisplayName.Length < bestNameLength && grid.DisplayName.LowerRemoveWhitespace().Contains(m_targetGridName) && CanTarget(seen))
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

		protected void GridSearch_Enemy()
		{
			Vector3D position = m_controlBlock.CubeBlock.GetPosition();

			m_enemies.Clear();
			m_controller.ForEachLastSeen(seen => {
				if (!seen.IsValid || !seen.isRecent())
					return false;

				IMyCubeGrid asGrid = seen.Entity as IMyCubeGrid;
				if (asGrid == null || !m_controlBlock.CubeBlock.canConsiderHostile(asGrid))
					return false;

				m_enemies.Add(seen);
				m_logger.debugLog("enemy: " + seen.Entity.getBestName(), "Search()");
				return false;
			});

			m_logger.debugLog("number of enemies: " + m_enemies.Count, "Search()");
			IOrderedEnumerable<LastSeen> enemiesByDistance = m_enemies.OrderBy(seen => Vector3D.DistanceSquared(position, seen.GetPosition()));
			m_reason = ReasonCannotTarget.None;
			foreach (LastSeen enemy in enemiesByDistance)
			{
				if (CanTarget(enemy))
				{
					Grid = enemy;
					m_logger.debugLog("found target: " + enemy.Entity.getBestName(), "Search()");
					return;
				}
			}

			Grid = null;
			m_logger.debugLog("nothing found", "Search()");
		}

		private void GridUpdate()
		{
			m_logger.debugLog(Grid == null, "Grid == null", "GridUpdate()", Logger.severity.FATAL);

			if (!Grid.IsValid)
			{
				m_logger.debugLog("no longer valid: " + Grid.Entity.getBestName(), "GridUpdate()", Logger.severity.DEBUG);
				Grid = null;
				return;
			}

			if (m_mustBeRecent && !Grid.isRecent())
			{
				m_logger.debugLog("no longer recent: " + Grid.Entity.getBestName() + ", age: " + (DateTime.UtcNow - Grid.LastSeenAt), "CanTarget()");
				Grid = null;
				return;
			}

			if (!CanTarget(Grid))
			{
				m_logger.debugLog("can no longer target: " + Grid.Entity.getBestName(), "GridUpdate()", Logger.severity.DEBUG);
				Grid = null;
				return;
			}

			LastSeen updated;
			if (!m_controller.tryGetLastSeen(Grid.Entity.EntityId, out updated))
			{
				m_logger.alwaysLog("Where does the good go?", "GridUpdate()", Logger.severity.WARNING);
				Grid = null;
				return;
			}

			m_logger.debugLog("updating grid last seen " + Grid.LastSeenAt + " => " + updated.LastSeenAt, "GridUpdate()");
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

				string blockName;
				try
				{
					blockName = ShipController_Autopilot.IsAutopilotBlock(Fatblock)
							? Fatblock.getNameOnly().LowerRemoveWhitespace()
							: Fatblock.DisplayNameText.LowerRemoveWhitespace();
				}
				catch (NullReferenceException nre)
				{
					m_logger.alwaysLog("Exception: " + nre, "BlockSearch()", Logger.severity.ERROR);
					m_logger.alwaysLog("Fatblock: " + Fatblock + ", DefinitionDisplayNameText: " + Fatblock.DefinitionDisplayNameText + ", DisplayNameText: " + Fatblock.DisplayNameText + ", Name only: " + Fatblock.getNameOnly(), "BlockSearch()", Logger.severity.ERROR);
					throw nre;
				}

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
			m_logger.debugLog(Grid == null, "Grid == null", "BlockCheck()", Logger.severity.FATAL);
			m_logger.debugLog(m_targetBlockName == null, "m_targetBlockName == null", "BlockCheck()", Logger.severity.FATAL);
			m_logger.debugLog(Block == null, "Block == null", "BlockCheck()", Logger.severity.FATAL);

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

		private bool CanTarget(LastSeen seen)
		{
			try
			{
				// if it is too far from start, cannot target
				if (MaximumRange > 1f && Vector3.DistanceSquared(m_startPosition, seen.GetPosition()) > MaximumRange * MaximumRange)
				{
					m_logger.debugLog("out of range of start position: " + seen.Entity.getBestName(), "CanTarget()");
					if (m_reason < ReasonCannotTarget.Too_Far)
					{
						m_reason = ReasonCannotTarget.Too_Far;
						m_reasonGrid = seen.Entity.EntityId;
					}
					return false;
				}

				// if it is too fast, cannot target
				float speedTarget = m_navSet.Settings_Current.SpeedTarget - 1f;
				if (seen.GetLinearVelocity().LengthSquared() >= speedTarget * speedTarget)
				{
					m_logger.debugLog("too fast to target: " + seen.Entity.getBestName(), "CanTarget()");
					if (m_reason < ReasonCannotTarget.Too_Fast)
					{
						m_reason = ReasonCannotTarget.Too_Fast;
						m_reasonGrid = seen.Entity.EntityId;
					}
					return false;
				}

				if (GridCondition != null && !GridCondition(seen.Entity as IMyCubeGrid))
				{
					m_logger.debugLog("Failed grid condition: " + seen.Entity.getBestName(), "CanTarget()");
					if (m_reason < ReasonCannotTarget.Grid_Condition)
					{
						m_reason = ReasonCannotTarget.Grid_Condition;
						m_reasonGrid = seen.Entity.EntityId;
					}
					return false;
				}

				return true;
			}
			catch (NullReferenceException nre)
			{
				m_logger.alwaysLog("Exception: " + nre, "CanTarget()", Logger.severity.ERROR);

				if (!seen.Entity.Closed)
					throw nre;
				m_logger.debugLog("Caught exception caused by grid closing, ignoring.", "CanTarget()");
				return false;
			}
		}

	}
}
