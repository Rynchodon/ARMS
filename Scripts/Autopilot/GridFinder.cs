using System;
using System.Collections.Generic;
using System.Linq;
using Rynchodon.AntennaRelay;
using Rynchodon.Attached;
using Rynchodon.Autopilot.Data;
using Rynchodon.Utility.Vectors;
using Sandbox.ModAPI;
using VRage.Game.ModAPI;
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

		private readonly ShipControllerBlock m_controlBlock;
		private readonly AttachedGrid.AttachmentKind m_allowedAttachment;
		private readonly Logger m_logger;
		private readonly AllNavigationSettings m_navSet;
		private readonly bool m_mustBeRecent;

		private ulong NextSearch_Grid, NextSearch_Block;
		//private List<LastSeen> m_enemies;

		public virtual LastSeen Grid { get; protected set; }
		public IMyCubeBlock Block { get; private set; }
		public Func<IMyCubeGrid, bool> GridCondition;
		/// <summary>Block requirements, other than can control.</summary>
		public Func<IMyCubeBlock, bool> BlockCondition;
		public ReasonCannotTarget m_reason;
		public LastSeen m_bestGrid;

		protected float MaximumRange;
		protected long m_targetEntityId;

		private Vector3I m_previousCell;

		private RelayStorage m_netStore { get { return m_controlBlock.NetworkStorage; } }

		/// <summary>
		/// Creates a GridFinder to find a friendly grid based on its name.
		/// </summary>
		public GridFinder(AllNavigationSettings navSet, ShipControllerBlock controller, string targetGrid, string targetBlock = null,
			AttachedGrid.AttachmentKind allowedAttachment = AttachedGrid.AttachmentKind.Permanent, bool mustBeRecent = false)
		{
			this.m_logger = new Logger(controller.CubeBlock);

			m_logger.debugLog("navSet == null", Logger.severity.FATAL, condition: navSet == null);
			m_logger.debugLog("controller == null", Logger.severity.FATAL, condition: controller == null);
			m_logger.debugLog("controller.CubeBlock == null", Logger.severity.FATAL, condition: controller.CubeBlock == null);
			m_logger.debugLog("targetGrid == null", Logger.severity.FATAL, condition: targetGrid == null);

			this.m_targetGridName = targetGrid.LowerRemoveWhitespace();
			if (targetBlock != null)
				this.m_targetBlockName = targetBlock.LowerRemoveWhitespace();
			this.m_controlBlock = controller;
			this.m_allowedAttachment = allowedAttachment;
			this.MaximumRange = float.MaxValue;
			this.m_navSet = navSet;
			this.m_mustBeRecent = mustBeRecent;
		}

		/// <summary>
		/// Creates a GridFinder to find an enemy grid based on distance.
		/// </summary>
		public GridFinder(AllNavigationSettings navSet, ShipControllerBlock controller, float maxRange = 0f)
		{
			this.m_logger = new Logger(controller.CubeBlock);

			m_logger.debugLog("navSet == null", Logger.severity.FATAL, condition: navSet == null);
			m_logger.debugLog("controller == null", Logger.severity.FATAL, condition: controller == null);
			m_logger.debugLog("controller.CubeBlock == null", Logger.severity.FATAL, condition: controller.CubeBlock == null);

			this.m_controlBlock = controller;
			//this.m_enemies = new List<LastSeen>();

			this.MaximumRange = maxRange;
			this.m_navSet = navSet;
			this.m_mustBeRecent = true;
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

		public Vector3D GetPosition(Vector3D NavPos, PositionBlock blockOffset)
		{
			if (!Grid.isRecent())
				return Grid.predictPosition();
			if (Block != null)
				return blockOffset.ToWorld(Block);
			
			IMyCubeGrid grid = (IMyCubeGrid)Grid.Entity;
			Vector3I closestCell;
			GridCellCache.GetCellCache(grid).GetClosestOccupiedCell(ref NavPos, ref m_previousCell, out closestCell);
			m_previousCell = closestCell;
			return grid.GridIntegerToWorld(closestCell);
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
			RelayStorage store = m_netStore;
			if (store == null)
			{
				m_logger.debugLog("no storage", Logger.severity.WARNING);
				return;
			}

			store.SearchLastSeen(seen => {
				IMyCubeGrid grid = seen.Entity as IMyCubeGrid;
				if (grid != null && grid.DisplayName.Length < bestNameLength && grid.DisplayName.LowerRemoveWhitespace().Contains(m_targetGridName) && CanTarget(seen))
				{
					Grid = seen;
					bestNameLength = grid.DisplayName.Length;
					if (bestNameLength == m_targetGridName.Length)
					{
						m_logger.debugLog("perfect match LastSeen: " + seen.Entity.getBestName());
						return true;
					}
				}
				return false;
			});

			if (Grid != null)
				m_logger.debugLog("Best match LastSeen: " + Grid.Entity.getBestName());
		}

		private void GridSearch_Enemy()
		{
			RelayStorage store = m_netStore;
			if (store == null)
			{
				m_logger.debugLog("no storage", Logger.severity.WARNING);
				return;
			}

			if (m_targetEntityId != 0L)
			{
				LastSeen target;
				if (store.TryGetLastSeen(m_targetEntityId, out target) && CanTarget(target))
				{
					Grid = target;
					m_logger.debugLog("found target: " + target.Entity.getBestName());
				}
				else
					Grid = null;
				return;
			}

			List<LastSeen> enemies = ResourcePool<List<LastSeen>>.Get();
			Vector3D position = m_controlBlock.CubeBlock.GetPosition();
			store.SearchLastSeen(seen => {
				if (!seen.IsValid || !seen.isRecent())
					return false;

				IMyCubeGrid asGrid = seen.Entity as IMyCubeGrid;
				if (asGrid == null)
					return false;
				if (!m_controlBlock.CubeBlock.canConsiderHostile(asGrid))
					return false;

				enemies.Add(seen);
				m_logger.debugLog("enemy: " + asGrid.DisplayName);
				return false;
			});

			m_logger.debugLog("number of enemies: " + enemies.Count);
			IOrderedEnumerable<LastSeen> enemiesByDistance = enemies.OrderBy(seen => Vector3D.DistanceSquared(position, seen.GetPosition()));
			m_reason = ReasonCannotTarget.None;
			foreach (LastSeen enemy in enemiesByDistance)
			{
				if (CanTarget(enemy))
				{
					Grid = enemy;
					m_logger.debugLog("found target: " + enemy.Entity.getBestName());
					enemies.Clear();
					ResourcePool<List<LastSeen>>.Return(enemies);
					return;
				}
			}

			Grid = null;
			m_logger.debugLog("nothing found"); 
			enemies.Clear();
			ResourcePool<List<LastSeen>>.Return(enemies);
		}

		private void GridUpdate()
		{
			m_logger.debugLog("Grid == null", Logger.severity.FATAL, condition: Grid == null);

			if (!Grid.IsValid)
			{
				m_logger.debugLog("no longer valid: " + Grid.Entity.getBestName(), Logger.severity.DEBUG);
				Grid = null;
				return;
			}

			if (m_mustBeRecent && !Grid.isRecent())
			{
				m_logger.debugLog("no longer recent: " + Grid.Entity.getBestName() + ", age: " + (Globals.ElapsedTime - Grid.LastSeenAt));
				Grid = null;
				return;
			}

			if (!CanTarget(Grid))
			{
				m_logger.debugLog("can no longer target: " + Grid.Entity.getBestName(), Logger.severity.DEBUG);
				Grid = null;
				return;
			}

			RelayStorage storage = m_netStore;
			if (storage == null)
			{
				m_logger.debugLog("lost storage", Logger.severity.DEBUG);
				Grid = null;
				return;
			}

			LastSeen updated;
			if (!m_netStore.TryGetLastSeen(Grid.Entity.EntityId, out updated))
			{
				m_logger.alwaysLog("Where does the good go? Searching for " + Grid.Entity.EntityId, Logger.severity.WARNING);
				Grid = null;
				return;
			}

			//m_logger.debugLog("updating grid last seen " + Grid.LastSeenAt + " => " + updated.LastSeenAt, "GridUpdate()");
			Grid = updated;
		}

		/// <summary>
		/// Find a block that matches m_targetBlockName.
		/// </summary>
		private void BlockSearch()
		{
			m_logger.debugLog("Grid == null", Logger.severity.FATAL, condition: Grid == null);
			m_logger.debugLog("m_targetBlockName == null", Logger.severity.FATAL, condition: m_targetBlockName == null);

			NextSearch_Block = Globals.UpdateCount + SearchInterval_Block;
			Block = null;

			int bestNameLength = int.MaxValue;
			IMyCubeGrid asGrid = Grid.Entity as IMyCubeGrid;
			m_logger.debugLog("asGrid == null", Logger.severity.FATAL, condition: asGrid == null);

			AttachedGrid.RunOnAttachedBlock(asGrid, m_allowedAttachment, slim => {
				IMyCubeBlock Fatblock = slim.FatBlock;
				if (Fatblock == null || !m_controlBlock.CubeBlock.canControlBlock(Fatblock))
					return false;

				string blockName;
				try
				{
					blockName = ShipAutopilot.IsAutopilotBlock(Fatblock)
							? Fatblock.getNameOnly().LowerRemoveWhitespace()
							: Fatblock.DisplayNameText.LowerRemoveWhitespace();
				}
				catch (NullReferenceException nre)
				{
					m_logger.alwaysLog("Exception: " + nre, Logger.severity.ERROR);
					m_logger.alwaysLog("Fatblock: " + Fatblock + ", DefinitionDisplayNameText: " + Fatblock.DefinitionDisplayNameText + ", DisplayNameText: " + Fatblock.DisplayNameText + ", Name only: " + Fatblock.getNameOnly(), Logger.severity.ERROR);
					throw nre;
				}

				if (BlockCondition != null && !BlockCondition(Fatblock))
					return false;

				//m_logger.debugLog("checking block name: \"" + blockName + "\" contains \"" + m_targetBlockName + "\"", "BlockSearch()");
				if (blockName.Length < bestNameLength && blockName.Contains(m_targetBlockName))
				{
					m_logger.debugLog("block name matches: " + Fatblock.DisplayNameText);
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
			m_logger.debugLog("Grid == null", Logger.severity.FATAL, condition: Grid == null);
			m_logger.debugLog("m_targetBlockName == null", Logger.severity.FATAL, condition: m_targetBlockName == null);
			m_logger.debugLog("Block == null", Logger.severity.FATAL, condition: Block == null);

			if (!m_controlBlock.CubeBlock.canControlBlock(Block))
			{
				m_logger.debugLog("lost control of block: " + Block.DisplayNameText, Logger.severity.DEBUG);
				Block = null;
				return;
			}

			if (BlockCondition != null && !BlockCondition(Block))
			{
				m_logger.debugLog("Block does not statisfy condition: " + Block.DisplayNameText, Logger.severity.DEBUG);
				Block = null;
				return;
			}
		}

		private bool CanTarget(LastSeen seen)
		{
			try
			{
				// if it is too far from start, cannot target
				if (MaximumRange > 1f && Vector3.DistanceSquared(m_controlBlock.CubeBlock.GetPosition(), seen.GetPosition()) > MaximumRange * MaximumRange)
				{
					m_logger.debugLog("out of range: " + seen.Entity.getBestName());
					if (m_reason < ReasonCannotTarget.Too_Far)
					{
						m_reason = ReasonCannotTarget.Too_Far;
						m_bestGrid = seen;
					}
					return false;
				}

				// if it is too fast, cannot target
				float speedTarget = m_navSet.Settings_Task_NavEngage.SpeedTarget - 1f;
				if (seen.GetLinearVelocity().LengthSquared() >= speedTarget * speedTarget)
				{
					m_logger.debugLog("too fast to target: " + seen.Entity.getBestName() + ", speed: " + seen.GetLinearVelocity().Length() + ", my speed: " + m_navSet.Settings_Task_NavEngage.SpeedTarget);
					if (m_reason < ReasonCannotTarget.Too_Fast)
					{
						m_reason = ReasonCannotTarget.Too_Fast;
						m_bestGrid = seen;
					}
					return false;
				}

				if (GridCondition != null && !GridCondition(seen.Entity as IMyCubeGrid))
				{
					m_logger.debugLog("Failed grid condition: " + seen.Entity.getBestName());
					if (m_reason < ReasonCannotTarget.Grid_Condition)
					{
						m_reason = ReasonCannotTarget.Grid_Condition;
						m_bestGrid = seen;
					}
					return false;
				}

				m_bestGrid = seen;
				return true;
			}
			catch (NullReferenceException nre)
			{
				m_logger.alwaysLog("Exception: " + nre, Logger.severity.ERROR);

				if (!seen.Entity.Closed)
					throw nre;
				m_logger.debugLog("Caught exception caused by grid closing, ignoring.");
				return false;
			}
		}

	}
}
