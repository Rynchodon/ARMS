using System.Collections.Generic; // from mscorlib.dll, System.dll, System.Core.dll, and VRage.Library.dll
using System.Text; // from mscorlib.dll
using Rynchodon.AntennaRelay;
using Rynchodon.Autopilot.Data;
using Rynchodon.Autopilot.Pathfinding;
using Rynchodon.Utility;
using Sandbox.Common.ObjectBuilders;
using Sandbox.Definitions;
using Sandbox.Game.Entities; // from Sandbox.Game.dll
using Sandbox.Game.Entities.Cube;
using Sandbox.ModAPI; // from Sandbox.Common.dll
using VRage.Collections;
using VRage.Game; // from VRage.Math.dll
using VRage.Game.Entity; // from VRage.Game.dll
using VRage.Game.ModAPI;
using VRage.ModAPI;
using VRageMath;

namespace Rynchodon.Autopilot.Navigator
{
	public class WeldGrid : NavigatorMover, INavigatorRotator
	{
		private readonly bool m_shopAfter;
		private readonly GridFinder m_finder;
		private readonly MultiBlock<MyObjectBuilder_ShipWelder> m_navWeld;

		private ulong m_timeoutAt;
		private LastSeen m_currentGrid;

		private readonly HashSet<IMySlimBlock> m_damagedBlocks = new HashSet<IMySlimBlock>();
		private readonly CachingHashSet<IMySlimBlock> m_projectedBlocks = new CachingHashSet<IMySlimBlock>();
		private readonly Dictionary<string, int> m_components_missing = new Dictionary<string, int>();
		private readonly Dictionary<string, int> m_components_inventory = new Dictionary<string, int>();
		private List<IMySlimBlock> m_blocksWithInventory;

		private Logable Log
		{ get { return new Logable(m_controlBlock?.CubeBlock); } }

		public WeldGrid(Pathfinder pathfinder, string gridName, bool shopAfter)
			: base(pathfinder)
		{
			this.m_finder = new GridFinder(pathfinder.NavSet, m_controlBlock, gridName);
			this.m_shopAfter = shopAfter;

			PseudoBlock navBlock = m_navSet.Settings_Current.NavigationBlock;
			if (navBlock.Block is IMyShipWelder)
				m_navWeld = new MultiBlock<MyObjectBuilder_ShipWelder>(navBlock.Block);
			else
			{
				CubeGridCache cache = CubeGridCache.GetFor(m_controlBlock.CubeGrid);
				if (cache == null)
				{
					Log.DebugLog("failed to get cache", Logger.severity.WARNING);
					return;
				}
				if (cache.CountByType(typeof(MyObjectBuilder_ShipWelder)) < 1)
				{
					Log.DebugLog("no welders on ship", Logger.severity.WARNING);
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
					Log.DebugLog("Search timed out", Logger.severity.INFO);
					m_navSet.OnTaskComplete_NavMove();
				}

				return;
			}

			if (m_currentGrid == null || m_finder.Grid.Entity != m_currentGrid.Entity)
			{
				Log.DebugLog("grid changed from " + m_finder.Grid.Entity.getBestName() + " to " + m_finder.Grid.Entity.getBestName(), Logger.severity.INFO);
				m_currentGrid = m_finder.Grid;
				GetDamagedBlocks();
			}

			IMySlimBlock repairable = FindClosestRepairable();
			if (repairable == null)
			{
				Log.DebugLog("failed to find a repairable block", Logger.severity.DEBUG);
				GetDamagedBlocks();
				if (m_shopAfter)
					CreateShopper();
				m_navSet.OnTaskComplete_NavMove();
				m_navSet.WelderUnfinishedBlocks = m_damagedBlocks.Count + m_projectedBlocks.Count;
				m_navSet.Settings_Commands.Complaint |= InfoString.StringId.WelderNotFinished;
				return;
			}

			m_damagedBlocks.Remove(repairable);
			new WeldBlock(m_pathfinder, m_navSet, m_navWeld, repairable);
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
			m_projectedBlocks.Clear();

			// get physical blocks

			foreach (IMySlimBlock slim in Attached.AttachedGrid.AttachedSlimBlocks((IMyCubeGrid)m_currentGrid.Entity, Attached.AttachedGrid.AttachmentKind.Permanent, true))
				if (slim.CurrentDamage > 0f || slim.BuildLevelRatio < 1f)
					m_damagedBlocks.Add(slim);

			// get projections

			HashSet<IMyEntity> projections = null;
			foreach (IMyCubeGrid grid in Attached.AttachedGrid.AttachedGrids((IMyCubeGrid)m_currentGrid.Entity, Attached.AttachedGrid.AttachmentKind.Permanent, true))
			{
				if (CubeGridCache.GetFor(grid).CountByType(typeof(MyObjectBuilder_Projector)) == 0)
					continue;

				using (MainLock.AcquireSharedUsing())
				{
					if (projections == null)
					{
						projections = new HashSet<IMyEntity>();
						MyAPIGateway.Entities.GetEntities(projections, entity => entity is MyCubeGrid && ((MyCubeGrid)entity).Projector != null);

						if (projections.Count == 0)
							break;
					}

					foreach (MyCubeGrid proj in projections)
						if (proj.Projector.CubeGrid == grid)
							foreach (IMySlimBlock block in proj.CubeBlocks)
								m_projectedBlocks.Add(block);
				}

				continue;
			}

			m_projectedBlocks.ApplyAdditions();

			Log.DebugLog("damaged blocks: " + m_damagedBlocks.Count + ", projected blocks: " + m_projectedBlocks.Count);
		}

		/// <summary>
		/// Move blocks in m_projectedBlocks to m_damagedBlocks if they are touching any real blocks
		/// </summary>
		private void ProjectedToDamaged()
		{
			foreach (IMySlimBlock block in m_projectedBlocks)
			{
				MyCubeGrid projected = (MyCubeGrid)block.CubeGrid;
				MyCubeGrid projectorGrid = projected.Projector.CubeGrid;

				if (projected.Closed || projected.Projector.Closed || !projected.Projector.IsWorking || projectorGrid.Closed)
				{
					Log.DebugLog("projection closed");
					continue;
				}

				Vector3I min = projectorGrid.WorldToGridInteger(projected.GridIntegerToWorld(block.Min()));

				if (projectorGrid.GetCubeBlock(min) != null)
				{
					Log.DebugLog("space is occupied: " + min);
					m_projectedBlocks.Remove(block);
					continue;
				}

				IMyCubeBlock cubeBlock = block.FatBlock;
				if (cubeBlock != null)
				{
					Vector3I max = projectorGrid.WorldToGridInteger(projected.GridIntegerToWorld(block.Max()));

					MatrixD invOrient = projectorGrid.PositionComp.WorldMatrixNormalizedInv.GetOrientation();
					Vector3 forward = Vector3D.Transform(cubeBlock.WorldMatrix.Forward, ref invOrient);
					Vector3 up = Vector3D.Transform(cubeBlock.WorldMatrix.Up, ref invOrient);

					MyBlockOrientation orient = new MyBlockOrientation(Base6Directions.GetClosestDirection(ref forward), Base6Directions.GetClosestDirection(ref up));

					if (projectorGrid.CanPlaceBlock(min, max, orient, ((MyCubeBlock)cubeBlock).BlockDefinition))
					{
						Log.DebugLog("can place fatblock: " + cubeBlock.DisplayNameText + ", position: " + min + ", world: " + projectorGrid.GridIntegerToWorld(min));
						m_damagedBlocks.Add(block);
						m_projectedBlocks.Remove(block);
					}

					continue;
				}

				// no fatblock, cannot get definition
				if (projectorGrid.IsTouchingAnyNeighbor(min, min))
				{
					Log.DebugLog("can place slimblock: " + block.ToString() + ", position: " + min + ", world: " + projectorGrid.GridIntegerToWorld(min));
					m_damagedBlocks.Add(block);
					m_projectedBlocks.Remove(block);
				}
			}

			m_projectedBlocks.ApplyRemovals();
		}

		/// <summary>
		/// Find the closest damaged block that can be repaired; either the missing components are available or there are no missing components.
		/// </summary>
		/// <returns>The closest repairable block to the welders.</returns>
		private IMySlimBlock FindClosestRepairable()
		{
			Log.DebugLog("searching for closest repairable block");

			if (m_damagedBlocks.Count == 0)
			{
				if (m_projectedBlocks.Count != 0)
					ProjectedToDamaged();
				if (m_damagedBlocks.Count == 0)
				{
					GetDamagedBlocks();
					if (m_damagedBlocks.Count == 0)
						return null;
				}
			}

			GetInventoryItems();

			IMySlimBlock repairable = null;
			double closest = float.MaxValue;
			List<IMySlimBlock> removeList = null;
			foreach (IMySlimBlock slim in m_damagedBlocks)
			{
				if (slim.Closed())
				{
					Log.DebugLog("slim closed: " + slim.getBestName());
					continue;
				}

				if (slim.Damage() == 0f && ((MyCubeGrid)slim.CubeGrid).Projector == null)
				{
					Log.DebugLog("already repaired: " + slim.getBestName(), Logger.severity.DEBUG);
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

					if (slim.Damage() != 0f)
						GetMissingComponents(slim);
					else
						// slim is projection
						GetAllComponents(slim);

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

			Log.DebugLog(() => "closest repairable block: " + repairable.getBestName(), Logger.severity.DEBUG, condition: repairable != null);

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
				Log.DebugLog("blocks with inventory: " + m_blocksWithInventory.Count);
			}

			m_components_inventory.Clear();
			foreach (IMySlimBlock slim in m_blocksWithInventory)
			{
				if (slim.FatBlock.Closed)
					continue;

				MyEntity asEntity = slim.FatBlock as MyEntity;
				int inventories = asEntity.InventoryCount;
				Log.DebugLog("searching " + inventories + " inventories of " + slim.FatBlock.DisplayNameText);
				for (int i = 0; i < inventories; i++)
				{
					List<MyPhysicalInventoryItem> allItems = null;
					MainLock.UsingShared(() => allItems = asEntity.GetInventory(i).GetItems());

					foreach (MyPhysicalInventoryItem item in allItems)
					{
						if (item.Content.TypeId != typeof(MyObjectBuilder_Component))
						{
							Log.DebugLog("skipping " + item.Content + ", not a component");
							continue;
						}

						Log.DebugLog("item: " + item.Content.SubtypeName + ", amount: " + item.Amount);
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
			// we just came from GetDamagedBlocks() so there should be no projected blocks in damaged blocks

			m_components_missing.Clear();
			foreach (IMySlimBlock slim in m_damagedBlocks)
			{
				if (slim.Closed())
				{
					Log.DebugLog("slim closed: " + slim.getBestName());
					continue;
				}

				GetMissingComponents(slim);
			}

			foreach (IMySlimBlock slim in m_projectedBlocks)
				GetAllComponents(slim);

			if (m_components_missing.Count != 0)
			{
				Log.DebugLog("Created shopper");
				m_navSet.Shopper = new Shopper(m_navSet, m_controlBlock.CubeGrid, m_components_missing);
			}
			else
				Log.DebugLog("nothing to shop for");
		}

		private void GetMissingComponents(IMySlimBlock slim)
		{
			using (MainLock.AcquireSharedUsing())
				slim.GetMissingComponents(m_components_missing);
		}

		private void GetAllComponents(IMySlimBlock slim)
		{
			using (MainLock.AcquireSharedUsing())
			{
				foreach (MyCubeBlockDefinition.Component component in ((MySlimBlock)slim).BlockDefinition.Components)
				{
					int currentCount;
					if (!m_components_missing.TryGetValue(component.Definition.Id.SubtypeName, out currentCount))
						currentCount = 0;
					currentCount += component.Count;
					m_components_missing[component.Definition.Id.SubtypeName] = currentCount;
					//Log.DebugLog("missing " + component.Definition.Id.SubtypeName + ": " + component.Count + ", total: " + currentCount, "GetAllComponents()");
				}
			}
		}

	}
}
