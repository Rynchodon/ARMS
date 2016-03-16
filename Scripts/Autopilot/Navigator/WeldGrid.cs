using System.Collections.Generic; // from mscorlib.dll, System.dll, System.Core.dll, and VRage.Library.dll
using System.Text; // from mscorlib.dll
using Rynchodon.AntennaRelay;
using Rynchodon.Autopilot.Data;
using Rynchodon.Autopilot.Movement;
using Sandbox.Common.ObjectBuilders;
using Sandbox.Game.Entities; // from Sandbox.Game.dll
using Sandbox.ModAPI; // from Sandbox.Common.dll
using VRage.Game; // from VRage.Math.dll
using VRage.Game.Entity; // from VRage.Game.dll
using VRageMath;

namespace Rynchodon.Autopilot.Navigator
{
	public class WeldGrid : NavigatorMover, INavigatorRotator
	{

		private readonly Logger m_logger;
		private readonly bool m_shopAfter;
		private readonly GridFinder m_finder;
		private readonly MultiBlock<MyObjectBuilder_ShipWelder> m_navWeld;

		private ulong m_timeoutAt;
		private LastSeen m_currentGrid;

		private readonly List<IMySlimBlock> m_damagedBlocks = new List<IMySlimBlock>();
		private readonly Dictionary<string, int> m_components_missing = new Dictionary<string, int>();
		private readonly Dictionary<string, int> m_components_inventory = new Dictionary<string, int>();
		private List<IMySlimBlock> m_blocksWithInventory;

		public WeldGrid(Mover mover, AllNavigationSettings navSet, string gridName, bool shopAfter)
			: base(mover, navSet)
		{
			this.m_logger = new Logger(GetType().Name, mover.Block.CubeBlock);
			this.m_finder = new GridFinder(navSet, m_controlBlock, gridName);
			this.m_shopAfter = shopAfter;

			PseudoBlock navBlock = m_navSet.Settings_Current.NavigationBlock;
			if (navBlock.Block is IMyShipWelder)
				m_navWeld = new MultiBlock<MyObjectBuilder_ShipWelder>(navBlock.Block);
			else
			{
				CubeGridCache cache = CubeGridCache.GetFor(m_controlBlock.CubeGrid);
				if (cache == null)
				{
					m_logger.debugLog("failed to get cache", "RepairGrid()", Logger.severity.WARNING);
					return;
				}
				if (cache.CountByType(typeof(MyObjectBuilder_ShipWelder)) < 1)
				{
					m_logger.debugLog("no welders on ship", "RepairGrid()", Logger.severity.WARNING);
					return;
				}
				m_navWeld = new MultiBlock<MyObjectBuilder_ShipWelder>(() => m_mover.Block.CubeGrid);
			}

			UpdateTimeout();

			m_navSet.Settings_Task_NavMove.NavigatorMover = this;
		}

		public override void Move()
		{
			m_finder.Update();
			m_mover.StopMove();

			if (m_finder.Grid == null)
			{
				m_mover.StopMove();
				m_currentGrid = null;

				if (Globals.UpdateCount >= m_timeoutAt)
				{
					m_logger.debugLog("Search timed out", "Move()", Logger.severity.INFO);
					m_navSet.OnTaskComplete_NavMove();
				}

				return;
			}

			if (m_currentGrid == null || m_finder.Grid.Entity != m_currentGrid.Entity)
			{
				m_logger.debugLog("grid changed from " + m_finder.Grid.Entity.getBestName() + " to " + m_finder.Grid.Entity.getBestName(), "Move()", Logger.severity.INFO);
				m_currentGrid = m_finder.Grid;
				GetDamagedBlocks();
			}

			IMySlimBlock repairable = FindClosestRepairable();
			if (repairable == null)
			{
				m_logger.debugLog("failed to find a repairable block", "Move()", Logger.severity.DEBUG);
				if (m_shopAfter)
					CreateShopper();
				m_navSet.OnTaskComplete_NavMove();
				return;
			}

			m_damagedBlocks.Remove(repairable);
			new WeldBlock(m_mover, m_navSet, m_navWeld, repairable);
		}

		public void Rotate()
		{
			m_mover.StopRotate();
		}

		public override void AppendCustomInfo(StringBuilder customInfo)
		{
			customInfo.Append("Searching for: ");
			customInfo.AppendLine(m_finder.m_targetGridName);
		}

		/// <summary>
		/// Updates search timeout
		/// </summary>
		private void UpdateTimeout()
		{
			const ulong Timeout = 60 * Globals.UpdatesPerSecond;
			m_timeoutAt = Globals.UpdateCount + Timeout;
		}

		/// <summary>
		/// Get all the blocks on the target grid that are damaged
		/// </summary>
		private void GetDamagedBlocks()
		{
			m_damagedBlocks.Clear();

			Attached.AttachedGrid.RunOnAttachedBlock(m_currentGrid.Entity as IMyCubeGrid, Attached.AttachedGrid.AttachmentKind.Permanent, slim => {
				if (slim.CurrentDamage > 0f || slim.BuildLevelRatio < 1f)
					m_damagedBlocks.Add(slim);
				return false;
			}, true);
		}

		/// <summary>
		/// Find the closest damaged block that can be repaired; either the missing components are available or there are no missing components.
		/// </summary>
		/// <returns>The closest repairable block to the welders.</returns>
		private IMySlimBlock FindClosestRepairable()
		{
			m_logger.debugLog("searching for closest repairable block", "FindClosestRepairable()");

			if (m_damagedBlocks.Count == 0)
			{
				GetDamagedBlocks();
				if (m_damagedBlocks.Count == 0)
					return null;
			}

			GetInventoryItems();

			IMySlimBlock repairable = null;
			double closest = float.MaxValue;
			List<IMySlimBlock> removeList = null;
			foreach (IMySlimBlock slim in m_damagedBlocks)
			{
				if (slim.Closed())
				{
					m_logger.debugLog("slim closed: " + slim.getBestName(), "FindClosestRepairable()");
					continue;
				}

				if (slim.CurrentDamage == 0f && slim.BuildLevelRatio == 1f)
				{
					m_logger.debugLog("already repaired: " + slim.getBestName(), "FindClosestRepairable()", Logger.severity.DEBUG);
					if (removeList == null)
						removeList = new List<IMySlimBlock>();
					removeList.Add(slim);
					continue;
				}

				Vector3D position;
				slim.ComputeWorldCenter(out position);
				double dist = Vector3D.DistanceSquared(m_controlBlock.CubeBlock.GetPosition(), position);

				if (dist < closest)
				{
					m_components_missing.Clear();
					MainLock.UsingShared(() => slim.GetMissingComponents(m_components_missing));

					bool haveItem = false;

					foreach (string component in m_components_missing.Keys)
						if (m_components_inventory.ContainsKey(component))
						{
							haveItem = true;
							break;
						}

					if (haveItem || m_components_missing.Count == 0)
					{
						repairable = slim;
						closest = dist;
					}
				}
			}
			if (removeList != null)
				foreach (IMySlimBlock remove in removeList)
					m_damagedBlocks.Remove(remove);

			m_logger.debugLog(repairable != null, () => "closest repairable block: " + repairable.getBestName(), "FindClosestRepairable()", Logger.severity.DEBUG);

			return repairable;
		}

		/// <summary>
		/// Get all the components that are in inventories on the autopilot ship.
		/// </summary>
		private void GetInventoryItems()
		{
			if (m_blocksWithInventory == null)
			{
				m_blocksWithInventory = new List<IMySlimBlock>();
				m_controlBlock.CubeGrid.GetBlocks_Safe(m_blocksWithInventory, slim => slim.FatBlock != null && (slim.FatBlock as MyEntity).HasInventory);
				m_logger.debugLog("blocks with inventory: " + m_blocksWithInventory.Count, "GetInventoryItems()");
			}

			m_components_inventory.Clear();
			foreach (IMySlimBlock slim in m_blocksWithInventory)
			{
				if (slim.FatBlock.Closed)
					continue;

				MyEntity asEntity = slim.FatBlock as MyEntity;
				int inventories = asEntity.InventoryCount;
				m_logger.debugLog("searching " + inventories + " inventories of " + slim.FatBlock.DisplayNameText, "GetInventoryItems()");
				for (int i = 0; i < inventories; i++)
				{
					List<MyPhysicalInventoryItem> allItems = null;
					MainLock.UsingShared(() => allItems = asEntity.GetInventory(i).GetItems());

					foreach (MyPhysicalInventoryItem item in allItems)
					{
						if (item.Content.TypeId != typeof(MyObjectBuilder_Component))
						{
							m_logger.debugLog("skipping " + item.Content + ", not a component", "GetInventoryItems()");
							continue;
						}

						m_logger.debugLog("item: " + item.Content.SubtypeName + ", amount: " + item.Amount, "GetInventoryItems()");
						int amount = (int)item.Amount;
						if (amount < 1)
							continue;

						string name = item.Content.SubtypeName;
						int count;

						if (!m_components_inventory.TryGetValue(name, out count))
							count = 0;
						count += amount;

						m_components_inventory[name] = count;
					}
				}
			}
		}

		/// <summary>
		/// Creates a shopper for missing components for damaged blocks.
		/// </summary>
		private void CreateShopper()
		{
			GetDamagedBlocks();

			m_components_missing.Clear();
			foreach (IMySlimBlock slim in m_damagedBlocks)
			{
				if (slim.Closed())
				{
					m_logger.debugLog("slim closed: " + slim.getBestName(), "CreateShopper()");
					continue;
				}

				MainLock.UsingShared(() => slim.GetMissingComponents(m_components_missing));
			}

			if (m_components_missing.Count != 0)
			{
				m_logger.debugLog("Created shopper", "CreateShopper()");
				m_navSet.Shopper = new Shopper(m_navSet, m_controlBlock.CubeGrid, m_components_missing);
			}
			else
				m_logger.debugLog("nothing to shop for", "CreateShopper()");
		}

	}
}
