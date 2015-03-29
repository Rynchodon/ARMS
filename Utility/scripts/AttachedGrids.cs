#define LOG_ENABLED // remove on build

using System;
using System.Collections.Generic;
//using System.Linq;
//using System.Text;

using Sandbox.Common.ObjectBuilders;
using Sandbox.ModAPI;
using ingame = Sandbox.ModAPI.Ingame;
using VRageMath;

namespace Rynchodon
{
	// TODO: If MyAPIGateway.Entities.GetEntityById(long entityId) works for blocks, use it as a replacement for tracking/searching attached parts of grids.
	// TODO: Rework so we can get the largest attached grid (public), and use the largest attached grid for isGridAttached()
	public class AttachedGrids
	{
		private static Dictionary<IMyCubeGrid, AttachedGrids> registry = new Dictionary<IMyCubeGrid, AttachedGrids>();
		private HashSet<AttachedGrids> attachedToMe = new HashSet<AttachedGrids>();

		private HashSet<IMySlimBlock> allPistonBases = new HashSet<IMySlimBlock>(); // for objectbuilder.TopBlockId
		private HashSet<IMySlimBlock> allPistonTops = new HashSet<IMySlimBlock>();
		private HashSet<IMySlimBlock> allMotorBases = new HashSet<IMySlimBlock>(); // for objectbuilder.RotorEntityId
		private HashSet<IMySlimBlock> allMotorRotors = new HashSet<IMySlimBlock>();
		private Dictionary<IMySlimBlock, wasConnected> allConnectors = new Dictionary<IMySlimBlock, wasConnected>(); // for objectbuilder.ConnectedEntityId
		private HashSet<IMySlimBlock> allLandingGears = new HashSet<IMySlimBlock>(); // horribly complicated

		private class wasConnected { public bool connected = false; }

		private IMyCubeGrid myGrid;

		private Logger myLogger;
		[System.Diagnostics.Conditional("LOG_ENABLED")]
		private void log(string toLog, string method = null, Logger.severity level = Logger.severity.DEBUG)
		{ alwaysLog(toLog, method, level); }
		private void alwaysLog(string toLog, string method = null, Logger.severity level = Logger.severity.DEBUG)
		{
			if (myLogger == null) myLogger = new Logger(myGrid.DisplayName, "AttachedGrids");
			myLogger.log(level, method, toLog);
		}

		private AttachedGrids() { }

		public static AttachedGrids getFor(IMyCubeGrid myGrid)
		{
			AttachedGrids instance;
			if (registry.TryGetValue(myGrid, out instance))
				return instance;
			instance = new AttachedGrids();
			instance.myGrid = myGrid;

			List<IMySlimBlock> allBlocks = new List<IMySlimBlock>();
			myGrid.GetBlocks(allBlocks);
			foreach (IMySlimBlock block in allBlocks)
				instance.myGrid_OnBlockAdded(block);

			myGrid.OnBlockAdded += instance.myGrid_OnBlockAdded;
			myGrid.OnBlockRemoved += instance.myGrid_OnBlockRemoved;
			myGrid.OnClosing += instance.myGrid_OnClosing;
			registry.Add(myGrid, instance);
			instance.log("created for: " + myGrid.DisplayName, "getFor()");
			return instance;
		}

		private void myGrid_OnClosing(IMyEntity obj)
		{ registry.Remove(myGrid); }

		private void myGrid_OnBlockAdded(IMySlimBlock added)
		{ addRemove(added, true); }

		private void myGrid_OnBlockRemoved(IMySlimBlock removed)
		{ addRemove(removed, false); }

		private static readonly MyObjectBuilderType type_pistonTop = (new MyObjectBuilder_PistonTop()).TypeId;
		private static readonly MyObjectBuilderType type_motorRotor = (new MyObjectBuilder_MotorRotor()).TypeId;

		private void addRemove(IMySlimBlock block, bool add)
		{
			IMyCubeBlock fatblock = block.FatBlock;
			if (fatblock == null)
				return;

			if (fatblock is IMyPistonBase)
				addRemove(allPistonBases, block, add);
			else if (fatblock.BlockDefinition.TypeId == type_pistonTop)
				addRemove(allPistonTops, block, add);
			else if (fatblock is ingame.IMyMotorBase)
				addRemove(allMotorBases, block, add);
			else if (fatblock.BlockDefinition.TypeId == type_motorRotor)
				addRemove(allMotorRotors, block, add);
			else if (fatblock is ingame.IMyShipConnector)
				addRemove(allConnectors, block, add);
			else if (fatblock is IMyLandingGear)
				addRemove(allLandingGears, block, add);
		}

		private void addRemove(HashSet<IMySlimBlock> blockSet, IMySlimBlock block, bool add)
		{
			needsRebuild = true;
			if (add)
				blockSet.Add(block);
			else
				blockSet.Remove(block);
		}

		private void addRemove(Dictionary<IMySlimBlock, wasConnected> blockSet, IMySlimBlock block, bool add)
		{
			needsRebuild = true;
			if (add)
				blockSet.Add(block, new wasConnected());
			else
				blockSet.Remove(block);
		}

		private void destructAttached()
		{
			if (attachedToMe.Count == 0)
				return;
			log("destruct for : " + myGrid.DisplayName, "destructAttached()", Logger.severity.TRACE);
			HashSet<AttachedGrids> tempAttached = attachedToMe;
			attachedToMe = new HashSet<AttachedGrids>();
			foreach (AttachedGrids attached in tempAttached)
				attached.destructAttached();
		}

		private void rebuildAttached()
		{
			destructAttached();
			buildAttached(++build_iteration_ID);
		}

		private static int build_iteration_ID = 0;
		private int last_build_ID = 0;

		private void buildAttached(int buildID)
		{
			if (last_build_ID == buildID)
			{
				//log("already building for : " + myGrid.DisplayName, "buildAttached()", Logger.severity.TRACE);
				return;
			}
			last_build_ID = buildID;
			log("building for : " + myGrid.DisplayName, "buildAttached()", Logger.severity.TRACE);

			// get all the potentially attached grids
			BoundingBoxD world = myGrid.WorldAABB;
			MatrixD scale = MatrixD.CreateScale(1.1);
			BoundingBoxD searchBox = world.Transform(scale);
			searchBox = searchBox.Translate(world.Center - searchBox.Center);
			foreach (IMyEntity entity in MyAPIGateway.Entities.GetEntitiesInAABB(ref searchBox))
			{
				IMyCubeGrid grid = entity as IMyCubeGrid;
				if (grid == null || grid == myGrid)
					continue;
				AttachedGrids partner = getFor(grid);
				if (attachedToMe.Contains(partner))
				{
					//log("already attached: " + grid.DisplayName, "buildAttached()", Logger.severity.TRACE);
					continue;
				}

				// check each grid for isAttached
				log("checking grid: " + grid.DisplayName, "buildAttached()", Logger.severity.TRACE);
				if (isAttached_piston(partner) || partner.isAttached_piston(this)
					|| isAttached_motor(partner) || partner.isAttached_motor(this)
					|| isAttached_connector(partner) || partner.isAttached_connector(this)
					|| isAttached_landingGear(partner) || partner.isAttached_landingGear(this))
					continue;
			}
			HashSet<AttachedGrids> copy = new HashSet<AttachedGrids>(attachedToMe);
			foreach (AttachedGrids attached in copy)
			{
				//log("building for attached: " + grid.DisplayName, "buildAttached()", Logger.severity.TRACE);
				attached.buildAttached(buildID);
			}
		}

		private bool tryAddAttached(AttachedGrids toAttach)
		{
			if (toAttach == this || attachedToMe.Contains(toAttach))
			{
				//log("already attached " + toAttach.DisplayName, "tryAddAttached()", Logger.severity.TRACE);
				return false;
			}
			attachedToMe.Add(toAttach);
			toAttach.tryAddAttached(this);
			log("attached " + toAttach.myGrid.DisplayName, "tryAddAttached()", Logger.severity.TRACE);
			return true;
		}

		/// <summary>
		/// 
		/// </summary>
		/// <param name="partner"></param>
		/// <returns>true iff a new grid was attached</returns>
		private bool isAttached_piston(AttachedGrids partner)
		{
			foreach (IMySlimBlock pistonBase in allPistonBases)
			{
				MyObjectBuilder_PistonBase builder_base = pistonBase.GetObjectBuilder() as MyObjectBuilder_PistonBase;
				long topBlockId = builder_base.TopBlockId;
				foreach (IMySlimBlock pistonTop in partner.allPistonTops)
					if (topBlockId == pistonTop.FatBlock.EntityId)
					{
						log("matched " + myGrid.DisplayName + " : " + pistonBase.FatBlock.DefinitionDisplayNameText + " to " + partner.myGrid.DisplayName + " : " + pistonTop.FatBlock.DefinitionDisplayNameText, "isAttached_piston()", Logger.severity.TRACE);
						tryAddAttached(partner);
						return true;
					}
			}
			return false;
		}

		private bool isAttached_motor(AttachedGrids partner)
		{
			foreach (IMySlimBlock motorBase in allMotorBases)
			{
				MyObjectBuilder_MotorBase builder_base = motorBase.GetObjectBuilder() as MyObjectBuilder_MotorBase;
				long rotorEntityId = builder_base.RotorEntityId;
				foreach (IMySlimBlock motorRotor in partner.allMotorRotors)
					if (rotorEntityId == motorRotor.FatBlock.EntityId)
					{
						log("matched " + myGrid.DisplayName + " : " + motorBase.FatBlock.DefinitionDisplayNameText + " to " + partner.myGrid.DisplayName + " : " + motorRotor.FatBlock.DefinitionDisplayNameText, "isAttached_motor()", Logger.severity.TRACE);
						tryAddAttached(partner);
						return true;
					}
			}
			return false;
		}

		private bool isAttached_connector(AttachedGrids partner)
		{
			foreach (KeyValuePair<IMySlimBlock, wasConnected> pair in allConnectors)
			{
				IMySlimBlock connector = pair.Key;
				MyObjectBuilder_ShipConnector builder_conn = connector.GetObjectBuilder() as MyObjectBuilder_ShipConnector;
				pair.Value.connected = builder_conn.Connected;
				if (!builder_conn.Connected)
					continue;

				long connectedEntityId = builder_conn.ConnectedEntityId;
				foreach (IMySlimBlock connectPartner in partner.allConnectors.Keys)
					if (connectedEntityId == connectPartner.FatBlock.EntityId)
					{
						log("matched " + myGrid.DisplayName + " : " + connector.FatBlock.DefinitionDisplayNameText + " to " + partner.myGrid.DisplayName + " : " + connectPartner.FatBlock.DefinitionDisplayNameText, "isAttached_connector()", Logger.severity.TRACE);
						tryAddAttached(partner);
						return true;
					}
			}
			return false;
		}

		private bool isAttached_landingGear(AttachedGrids partner)
		{
			return false;
		}

		private static int searchAttached_ID = 0;
		private int mySearchAttached_ID = 0;

		private bool needsRebuild = true;

		public static bool isGridAttached(IMyCubeGrid grid1, IMyCubeGrid grid2)
		{
			if (grid1 == grid2)
				return true;
			return getFor(grid1).isGridAttached(grid2);
		}

		public bool isGridAttached(IMyCubeGrid grid)
		{
			if (myGrid == grid)
				return true;
			if (needsRebuild || connectedChanged())
			{
				rebuildAttached();
				needsRebuild = false;
			}
			return isGridAttached(getFor(grid), ++searchAttached_ID); 
		}

		private bool isGridAttached(AttachedGrids searchFor, int searchID)
		{
			if (searchID == mySearchAttached_ID)
				return false; // already searching
			mySearchAttached_ID = searchID;

			if (attachedToMe.Contains(searchFor))
				return true; // found it
			foreach (AttachedGrids attached in attachedToMe)
				if (attached.isGridAttached(searchFor, searchID))
					return true; // attached through another

			return false; // not attached
		}

		private static readonly TimeSpan timeBetweenConnectedChanged = new TimeSpan(0, 0, 1);
		private DateTime nextConnectedChanged = DateTime.UtcNow;

		private static int connectedChanged_ID = 0;
		private int myConnectedChanged_ID = 0;

		private bool connectedChanged()
		{
			if (DateTime.UtcNow.CompareTo(nextConnectedChanged) < 0) // not time to check again
				return false;
			return connectedChanged(++connectedChanged_ID);
		}

		/// <summary>
		/// compares builder.connected to wasConnected. if any changed return true
		/// </summary>
		/// <returns></returns>
		private bool connectedChanged(int searchID)
		{
			if (myConnectedChanged_ID == searchID) // already searching
				return false;
			nextConnectedChanged = DateTime.UtcNow + timeBetweenConnectedChanged;
			myConnectedChanged_ID = searchID;

			foreach (KeyValuePair<IMySlimBlock, wasConnected> pair in allConnectors)
			{
				MyObjectBuilder_ShipConnector builder = pair.Key.GetObjectBuilder() as MyObjectBuilder_ShipConnector;
				if (builder.Connected != pair.Value.connected)
					return true;
			}
			foreach (AttachedGrids connected in attachedToMe)
				if (connected.connectedChanged(searchID))
					return true;
			return false;
		}
	}
}
