using System;
using System.Collections.Generic; // from mscorlib.dll, System.dll, System.Core.dll, and VRage.Library.dll
using System.Text; // from mscorlib.dll
using Rynchodon.Autopilot.Data;
using Rynchodon.Utility;
using Sandbox.Definitions; // from Sandbox.Game.dll
using Sandbox.Game.Entities; // from Sandbox.Game.dll
using Sandbox.ModAPI;
using VRage; // from VRage.dll and VRage.Library.dll
using VRage.Game; // from VRage.Game.dll
using VRage.Game.Entity; // from VRage.Game.dll
using VRage.Game.ModAPI; // from VRage.Game.dll
using VRage.ObjectBuilders;

namespace Rynchodon.Autopilot.Navigator
{
	/// <summary>
	/// Retrieves items from connected grids.
	/// </summary>
	public class Shopper : INavigatorMover
	{
		private const float maxTransfer = 1;

		private readonly AllNavigationSettings m_navSet;
		private readonly IMyCubeGrid m_grid;
		private readonly List<IMyInventory> m_destInventory = new List<IMyInventory>();
		private readonly List<IMyInventory> m_sourceInventory = new List<IMyInventory>();

		private Action m_currentTask;
		private ulong m_nextUpdate;
		private Dictionary<string, int> m_shoppingList;


		private Logable Log
		{ get { return new Logable(m_grid); } }

		public Shopper(AllNavigationSettings navSet, IMyCubeGrid grid, Dictionary<string, int> shopList)
		{
			this.m_navSet = navSet;
			this.m_shoppingList = shopList;
			this.m_grid = grid;

			this.m_currentTask = Return;

			foreach (var pair in m_shoppingList)
				Log.DebugLog("Item: " + pair.Key + ", amount: " + pair.Value);
		}

		public void Move()
		{
			if (Globals.UpdateCount < m_nextUpdate)
				return;
			m_nextUpdate = Globals.UpdateCount + 100ul;

			if (m_sourceInventory.Count == 0)
			{
				FindSourceInventory();
				if (m_sourceInventory.Count == 0)
				{
					Log.DebugLog("No source inventories found", Logger.severity.WARNING);
					m_navSet.OnTaskComplete_NavWay();
					return;
				}
			}

			if (m_shoppingList.Count != 0)
				MyAPIGateway.Utilities.TryInvokeOnGameThread(m_currentTask);
			else
			{
				Log.DebugLog("Shopping finished, proceed to checkout", Logger.severity.INFO);
				m_navSet.OnTaskComplete_NavWay();
			}
		}

		public void AppendCustomInfo(StringBuilder customInfo)
		{
			if (m_shoppingList.Count == 0)
			{
				customInfo.AppendLine("Finished getting components");
				return;
			}

			var component = m_shoppingList.FirstPair();

			customInfo.Append("Searching for ");
			customInfo.Append(component.Value);
			customInfo.Append(" ");
			customInfo.AppendLine(component.Key);
		}

		/// <summary>
		/// Starts retreiving items, connection needs to be established within 200 updates of invoking this function.
		/// </summary>
		public void Start()
		{
			Log.DebugLog("shopper started");
			m_navSet.Shopper = null;

			this.m_grid.GetBlocks_Safe(null, slim => {
				MyEntity entity = slim.FatBlock as MyEntity;
				if (entity != null && entity is Sandbox.ModAPI.Ingame.IMyCargoContainer)
				{
					Log.DebugLog("entity: " + entity.GetBaseEntity().getBestName() + ", inventories: " + entity.InventoryCount);
					int count = entity.InventoryCount;
					for (int i = 0; i < count; i++)
						m_destInventory.Add(entity.GetInventory(i));
				}
				return false;
			});

			if (m_destInventory.Count == 0)
			{
				Log.DebugLog("no dest inventory");
				return;
			}

			m_navSet.Settings_Task_NavWay.NavigatorMover = this;
			m_navSet.Settings_Task_NavWay.PathfinderCanChangeCourse = false;

			m_nextUpdate = Globals.UpdateCount + 200ul; // give attached a chance to be registered before FindSourceInventory
		}

		/// <summary>
		/// Find inventories that items can be transferred from.
		/// </summary>
		private void FindSourceInventory()
		{
			Log.DebugLog("looking for attached blocks");

			foreach (MyEntity entity in Attached.AttachedGrid.AttachedCubeBlocks(this.m_grid, Attached.AttachedGrid.AttachmentKind.Terminal, false))
				if (entity.HasInventory && (entity.GetInventory(0) as IMyInventory).IsConnectedTo(m_destInventory[0]))
				{
					Log.DebugLog("entity: " + entity.GetBaseEntity().getBestName() + ", inventories: " + entity.InventoryCount);
					int count = entity.InventoryCount;
					for (int i = 0; i < count; i++)
						m_sourceInventory.Add(entity.GetInventory(i));
				}
		}

		/// <summary>
		/// Puts components back
		/// </summary>
		private void Return()
		{
			float allowedVolume = maxTransfer; // volume is in m³ not l

			foreach (IMyInventory sourceInv in m_destInventory)
				if (sourceInv.CurrentVolume > MyFixedPoint.Zero)
				{
					var items = sourceInv.GetItems();
					for (int i = 0; i < items.Count; i++)
					{
						MyObjectBuilderType baseType = items[i].Content.GetType();
						if (baseType != typeof(MyObjectBuilder_Component))
							continue;

						SerializableDefinitionId defId = new SerializableDefinitionId(baseType, items[i].Content.SubtypeName);
						float oneVol = MyDefinitionManager.Static.GetPhysicalItemDefinition(defId).Volume;

						foreach (IMyInventory destInv in m_sourceInventory)
							if (destInv.CurrentVolume < destInv.MaxVolume - 1 && sourceInv.IsConnectedTo(destInv))
							{
								int allowedAmount = (int)(allowedVolume / oneVol);
								if (allowedAmount <= 0)
								{
									Log.DebugLog("allowedAmount < 0", Logger.severity.FATAL, condition: allowedAmount < 0);
									Log.DebugLog("reached max transfer for this update", Logger.severity.DEBUG);
									return;
								}

								int invSpace = (int)((float)(destInv.MaxVolume - destInv.CurrentVolume) / oneVol);
								MyFixedPoint transferAmount = Math.Min(allowedAmount, invSpace);
								if (transferAmount > items[i].Amount)
									transferAmount = items[i].Amount;

								if (transferAmount > invSpace)
									transferAmount = invSpace;

								MyFixedPoint volumeBefore = destInv.CurrentVolume;
								if (destInv.TransferItemFrom(sourceInv, 0, stackIfPossible: true, amount: transferAmount))
								{
									//Log.DebugLog("transfered item from " + (sourceInv.Owner as IMyEntity).getBestName() + " to " + (destInv.Owner as IMyEntity).getBestName() +
									//	", amount: " + transferAmount + ", volume: " + (destInv.CurrentVolume - volumeBefore), "Return()");
									allowedVolume += (float)(volumeBefore - destInv.CurrentVolume);
								}
							}
					}
				}

			Log.DebugLog("finished emptying inventories", Logger.severity.INFO);
			m_currentTask = Shop;
		}

		/// <summary>
		/// Retreive items.
		/// </summary>
		private void Shop()
		{
			float allowedVolume = maxTransfer; // volume is in m³ not l

			var component = m_shoppingList.FirstPair();
			//Log.DebugLog("shopping for " + component.Key, "Move()");
			SerializableDefinitionId defId = new SerializableDefinitionId(typeof(MyObjectBuilder_Component), component.Key);
			float oneVol = MyDefinitionManager.Static.GetPhysicalItemDefinition(defId).Volume;
			foreach (IMyInventory source in m_sourceInventory)
			{
				//Log.DebugLog("source: " + (source.Owner as IMyEntity).getBestName(), "Move()");

				int amountToMove, sourceIndex;
				GetComponentInInventory(source, defId, out amountToMove, out sourceIndex);
				if (amountToMove < 1)
					continue;
				//Log.DebugLog("found: " + component.Key + ", count: " + amountToMove, "Move()");
				if (amountToMove > component.Value)
				{
					//Log.DebugLog("reducing amountToMove to " + component.Value, "Move()");
					amountToMove = component.Value;
				}

				foreach (IMyInventory destination in m_destInventory)
				{
					if (amountToMove < 1)
						break;

					//Log.DebugLog("destination: " + (destination.Owner as IMyEntity).getBestName(), "Move()");

					int allowedAmount = (int)(allowedVolume / oneVol);
					if (allowedAmount < 1)
					{
						Log.DebugLog("allowed amount less than 1");
						return;
					}
					if (allowedAmount < amountToMove)
					{
						//Log.DebugLog("allowedAmount(" + allowedAmount + ") < amountToMove(" + amountToMove + ")", "Move()");
						amountToMove = allowedAmount;
					}

					MyFixedPoint amountInDest = destination.GetItemAmount(defId);
					if (destination.TransferItemFrom(source, sourceIndex, amount: amountToMove))
					{
						MyFixedPoint amountNow = destination.GetItemAmount(defId);
						if (amountNow == amountInDest)
						{
							Log.DebugLog("no transfer took place");
						}
						else
						{
							int transferred = (int)(amountNow - amountInDest);
							m_shoppingList[component.Key] -= transferred;
							allowedVolume -= transferred * oneVol;
							amountToMove -= transferred;
							//Log.DebugLog("transfered: " + transferred + ", remaining volume: " + allowedVolume + " remaining amount: " + amountToMove, "Move()");
							component = m_shoppingList.FirstPair();
							if (amountToMove < 1)
							{
								if (component.Value < 1)
								{
									Log.DebugLog("final transfer for " + component.Key);
									m_shoppingList.Remove(component.Key);
								}
								return;
							}
						}
					}
					else
						Log.DebugLog("transfer failed", Logger.severity.WARNING);
				}
			}

			if (allowedVolume == maxTransfer)
			{
				Log.DebugLog("no items were transferred, dropping item from list: " + component.Key, Logger.severity.DEBUG);
				m_shoppingList.Remove(component.Key);
			}
			else
				Log.DebugLog("value: " + m_shoppingList[component.Key], Logger.severity.DEBUG);
		}

		/// <summary>
		/// Get the component amount and index from an inventory.
		/// </summary>
		/// <param name="inventory">Inventory to search.</param>
		/// <param name="defId">Component definition</param>
		/// <param name="amount">Amount of components in inventory</param>
		/// <param name="index">Component position in inventory</param>
		private void GetComponentInInventory(IMyInventory inventory, MyDefinitionId defId, out int amount, out int index)
		{
			amount = (int)inventory.GetItemAmount(defId);
			if (amount == 0)
			{
				index = -1;
				return;
			}

			var allItems = inventory.GetItems();
			for (index = 0; index < allItems.Count; index++)
				if (allItems[index].Content.GetId() == defId)
					return;
		}

	}
}
