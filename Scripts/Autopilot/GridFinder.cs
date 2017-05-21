using System;
using System.Collections.Generic;
using System.Linq;
using Rynchodon.AntennaRelay;
using Rynchodon.Attached;
using Rynchodon.Autopilot.Data;
using Rynchodon.Utility;
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

		private const ulong SearchInterval_Grid = 100ul, SearchInterval_Block = 100ul;

		public readonly string m_targetGridName, m_targetBlockName;

		private readonly ShipControllerBlock m_controlBlock;
		private readonly AttachedGrid.AttachmentKind m_allowedAttachment;
		private readonly AllNavigationSettings m_navSet;
		private readonly bool m_mustBeRecent;
		public Vector3D m_startPosition;

		public ulong NextSearch_Grid { get; private set; }
		public ulong NextSearch_Block { get; private set; }

		public virtual LastSeen Grid { get; protected set; }
		public IMyCubeBlock Block { get; private set; }
		public Func<LastSeen, double> OrderValue;
		public Func<IMyCubeGrid, bool> GridCondition;
		/// <summary>Block requirements, other than can control.</summary>
		public Func<IMyCubeBlock, bool> BlockCondition;
		public ReasonCannotTarget m_reason;
		public LastSeen m_bestGrid;

		protected float MaximumRange;
		protected long m_targetEntityId;

		//private Vector3I m_previousCell;

		private RelayStorage m_netStore { get { return m_controlBlock.NetworkStorage; } }

		private Logable Log { get { return new Logable(m_controlBlock?.CubeBlock); } }

		/// <summary>
		/// Creates a GridFinder to find a friendly grid based on its name.
		/// </summary>
		public GridFinder(AllNavigationSettings navSet, ShipControllerBlock controller, string targetGrid, string targetBlock = null,
			AttachedGrid.AttachmentKind allowedAttachment = AttachedGrid.AttachmentKind.Permanent, bool mustBeRecent = false)
		{
			Log.DebugLog("navSet == null", Logger.severity.FATAL, condition: navSet == null);
			Log.DebugLog("controller == null", Logger.severity.FATAL, condition: controller == null);
			Log.DebugLog("controller.CubeBlock == null", Logger.severity.FATAL, condition: controller.CubeBlock == null);
			Log.DebugLog("targetGrid == null", Logger.severity.FATAL, condition: targetGrid == null);

			this.m_targetGridName = targetGrid.LowerRemoveWhitespace();
			if (targetBlock != null)
				this.m_targetBlockName = targetBlock.LowerRemoveWhitespace();
			this.m_controlBlock = controller;
			this.m_allowedAttachment = allowedAttachment;
			this.MaximumRange = 0f;
			this.m_navSet = navSet;
			this.m_mustBeRecent = mustBeRecent;
			this.m_startPosition = m_controlBlock.CubeBlock.GetPosition();
		}

		/// <summary>
		/// Creates a GridFinder to find an enemy grid based on distance.
		/// </summary>
		public GridFinder(AllNavigationSettings navSet, ShipControllerBlock controller, float maxRange = 0f)
		{
			Log.DebugLog("navSet == null", Logger.severity.FATAL, condition: navSet == null);
			Log.DebugLog("controller == null", Logger.severity.FATAL, condition: controller == null);
			Log.DebugLog("controller.CubeBlock == null", Logger.severity.FATAL, condition: controller.CubeBlock == null);

			this.m_controlBlock = controller;
			//this.m_enemies = new List<LastSeen>();

			this.MaximumRange = maxRange;
			this.m_navSet = navSet;
			this.m_mustBeRecent = true;
			this.m_startPosition = m_controlBlock.CubeBlock.GetPosition();
		}

		public void Update()
		{
			if (Grid == null || OrderValue != null)
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
				Log.DebugLog("no storage", Logger.severity.DEBUG);
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
						Log.DebugLog("perfect match LastSeen: " + seen.Entity.getBestName());
						return true;
					}
				}
				return false;
			});

			if (Grid != null)
				Log.DebugLog("Best match LastSeen: " + Grid.Entity.getBestName());
		}

		private void GridSearch_Enemy()
		{
			RelayStorage store = m_netStore;
			if (store == null)
			{
				Log.DebugLog("no storage", Logger.severity.DEBUG);
				return;
			}

			if (m_targetEntityId != 0L)
			{
				LastSeen target;
				if (store.TryGetLastSeen(m_targetEntityId, out target) && CanTarget(target))
				{
					Grid = target;
					Log.DebugLog("found target: " + target.Entity.getBestName());
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
				Log.DebugLog("enemy: " + asGrid.DisplayName);
				return false;
			});

			Log.DebugLog("number of enemies: " + enemies.Count);
			IOrderedEnumerable<LastSeen> enemiesByDistance = enemies.OrderBy(OrderValue != null ? OrderValue : seen => Vector3D.DistanceSquared(position, seen.GetPosition()));
			m_reason = ReasonCannotTarget.None;
			foreach (LastSeen enemy in enemiesByDistance)
			{
				if (CanTarget(enemy))
				{
					Grid = enemy;
					Log.DebugLog("found target: " + enemy.Entity.getBestName());
					enemies.Clear();
					ResourcePool<List<LastSeen>>.Return(enemies);
					return;
				}
			}

			Grid = null;
			Log.DebugLog("nothing found"); 
			enemies.Clear();
			ResourcePool<List<LastSeen>>.Return(enemies);
		}

		private void GridUpdate()
		{
			Log.DebugLog("Grid == null", Logger.severity.FATAL, condition: Grid == null);

			if (!Grid.IsValid)
			{
				Log.DebugLog("no longer valid: " + Grid.Entity.getBestName(), Logger.severity.DEBUG);
				Grid = null;
				return;
			}

			if (m_mustBeRecent && !Grid.isRecent())
			{
				Log.DebugLog("no longer recent: " + Grid.Entity.getBestName() + ", age: " + (Globals.ElapsedTime - Grid.LastSeenAt));
				Grid = null;
				return;
			}

			if (!CanTarget(Grid))
			{
				Log.DebugLog("can no longer target: " + Grid.Entity.getBestName(), Logger.severity.DEBUG);
				Grid = null;
				return;
			}

			RelayStorage storage = m_netStore;
			if (storage == null)
			{
				Log.DebugLog("lost storage", Logger.severity.DEBUG);
				Grid = null;
				return;
			}

			LastSeen updated;
			if (!m_netStore.TryGetLastSeen(Grid.Entity.EntityId, out updated))
			{
				Log.AlwaysLog("Where does the good go? Searching for " + Grid.Entity.EntityId, Logger.severity.WARNING);
				Grid = null;
				return;
			}

			//Log.DebugLog("updating grid last seen " + Grid.LastSeenAt + " => " + updated.LastSeenAt, "GridUpdate()");
			Grid = updated;
		}

		/// <summary>
		/// Find a block that matches m_targetBlockName.
		/// </summary>
		private void BlockSearch()
		{
			Log.DebugLog("Grid == null", Logger.severity.FATAL, condition: Grid == null);
			Log.DebugLog("m_targetBlockName == null", Logger.severity.FATAL, condition: m_targetBlockName == null);

			NextSearch_Block = Globals.UpdateCount + SearchInterval_Block;
			Block = null;

			int bestNameLength = int.MaxValue;
			IMyCubeGrid asGrid = Grid.Entity as IMyCubeGrid;
			Log.DebugLog("asGrid == null", Logger.severity.FATAL, condition: asGrid == null);

			foreach (IMyCubeBlock Fatblock in AttachedGrid.AttachedCubeBlocks(asGrid, m_allowedAttachment, true))
			{
				if (!m_controlBlock.CubeBlock.canControlBlock(Fatblock))
					continue;

				string blockName = Fatblock.DisplayNameText.LowerRemoveWhitespace();

				if (BlockCondition != null && !BlockCondition(Fatblock))
					continue;

				//Log.DebugLog("checking block name: \"" + blockName + "\" contains \"" + m_targetBlockName + "\"", "BlockSearch()");
				if (blockName.Length < bestNameLength && blockName.Contains(m_targetBlockName))
				{
					Log.DebugLog("block name matches: " + Fatblock.DisplayNameText);
					Block = Fatblock;
					bestNameLength = blockName.Length;
					if (m_targetBlockName.Length == bestNameLength)
						return;
				}
			}
		}

		private void BlockCheck()
		{
			Log.DebugLog("Grid == null", Logger.severity.FATAL, condition: Grid == null);
			Log.DebugLog("m_targetBlockName == null", Logger.severity.FATAL, condition: m_targetBlockName == null);
			Log.DebugLog("Block == null", Logger.severity.FATAL, condition: Block == null);

			if (!m_controlBlock.CubeBlock.canControlBlock(Block))
			{
				Log.DebugLog("lost control of block: " + Block.DisplayNameText, Logger.severity.DEBUG);
				Block = null;
				return;
			}

			if (BlockCondition != null && !BlockCondition(Block))
			{
				Log.DebugLog("Block does not statisfy condition: " + Block.DisplayNameText, Logger.severity.DEBUG);
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
					Log.DebugLog("out of range: " + seen.Entity.getBestName());
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
					Log.DebugLog("too fast to target: " + seen.Entity.getBestName() + ", speed: " + seen.GetLinearVelocity().Length() + ", my speed: " + m_navSet.Settings_Task_NavEngage.SpeedTarget);
					if (m_reason < ReasonCannotTarget.Too_Fast)
					{
						m_reason = ReasonCannotTarget.Too_Fast;
						m_bestGrid = seen;
					}
					return false;
				}

				if (GridCondition != null && !GridCondition(seen.Entity as IMyCubeGrid))
				{
					Log.DebugLog("Failed grid condition: " + seen.Entity.getBestName());
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
				Log.AlwaysLog("Exception: " + nre, Logger.severity.ERROR);

				if (!seen.Entity.Closed)
					throw;
				Log.DebugLog("Caught exception caused by grid closing, ignoring.");
				return false;
			}
		}

	}
}
