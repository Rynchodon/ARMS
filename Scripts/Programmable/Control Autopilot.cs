using System; // (partial) from mscorlib.dll
using System.Collections.Generic; // from mscorlib.dll, System.dll, System.Core.dll, and VRage.Library.dll
using System.Text; // from mscorlib.dll

using Sandbox.ModAPI.Ingame; // from Sandbox.Common.dll
using Sandbox.ModAPI.Interfaces; // (partial) from Sandbox.Common.dll
using VRage.Game; // from VRage.Game.dll
using VRageMath; // from VRage.Math.dll

/*
 * allowed namespaces for in game script:
 * 
 * System.Collections; // from mscorlib.dll
 * System.Globalization // from mscorlib.dll
 * System.Linq // from System.Core.dll
 * System.Text // from mscorlib.dll
 * System.Text.RegularExpressions // from System.dll
 * 
 * Medieval.ObjectBuilders // from MedievalEngineers.ObjectBuilders.dll
 * Medieval.ObjectBuilders.Definitions // from MedievalEngineers.ObjectBuilders.dll
 * Sandbox.Common.ObjectBuilders // from MedievalEngineers.ObjectBuilders.dll and SpaceEngineers.ObjectBuilders.dll
 * Sandbox.Common.ObjectBuilders.Definitions // from SpaceEngineers.ObjectBuilders.dll
 * Sandbox.Game.Gui // from Sandbox.Game.dll
 * VRage.ObjectBuilders // from VRage.Game.dll
 */

namespace Rynchodon
{
  public class MyGridProgram : Sandbox.ModAPI.Ingame.MyGridProgram
  {

    /*
     * Example script for controlling an Autopilot block with a Programmable block
     * For a ship hauling cargo from Sending Platform to Receiving Platform
     * This script will cause the ship to refill Uranium, Hydrogen, or Oxygen if it is low.
     */

    /// <summary>How often to check inventory/capacity for stuff.</summary>
    readonly TimeSpan updateInterval = new TimeSpan(0, 0, 10);
    /// <summary>A cargo container will be considerd full if it has less than this capacity remaining.</summary>
    readonly VRage.MyFixedPoint cargoBuffer = 2;

    /// <summary>All the tasks...</summary>
    List<Task> allTasks;
    /// <summary>Task that is in progress.</summary>
    Task currentTask;

    /// <summary>The next time uranium levels will be checked</summary>
    DateTime nextUpdate_uranium;
    /// <summary>The next time gas levels will be checked.</summary>
    DateTime nextUpdate_gas;
    /// <summary>The next time cargo volume will be checked.</summary>
    DateTime nextUpdate_cargo;

    /// <summary>Amount of uranium currently stored in the reactors.</summary>
    VRage.MyFixedPoint uraniumMass;
    /// <summary>The ratio of hydrogen in the tanks to their capacity.</summary>
    float hydrogenRatio;
    /// <summary>The ratio of oxygen in the tanks to their capacity.</summary>
    float oxygenRatio;
    /// <summary>The ratio of cargo in the containers to their capacity.</summary>
    float cargoRatio;
    /// <summary>The cargoRatio from the previous time it was updated.</summary>
    float prev_cargoRatio;

    List<IMyTerminalBlock> blocks = new List<IMyTerminalBlock>();

    /// <summary>
    /// Arguments used to create a message, there must be exactly three and in this order:
    /// target grid, target block, content
    /// </summary>
    List<TerminalActionParameter> parameters;

    public void Main(string args)
    {
      if (allTasks == null)
      {
        allTasks = new List<Task>();
        string first = "L Connector ; B Connector ; G";
        allTasks.Add(new Task(UraniumLow, UraniumHigh, first + "Uranium Platform"));
        allTasks.Add(new Task(HydrogenLow, HydrogenHigh, first + "Hydrogen Platform ; A Hydrogen Tank, Stockpile_On"));
        allTasks.Add(new Task(OxygenLow, OxygenHigh, first + "Oxygen Platform ; A Oxygen Tank, Stockpile_On"));
        allTasks.Add(new Task(CargoPartFull, CargoEmpty, first + "Receiving Platform"));
        allTasks.Add(new Task(null, CargoFull, "L Connector ; B Connector ; G Sending Platform"));

        parameters = new List<TerminalActionParameter>();
        parameters.Add(TerminalActionParameter.Get("Cargo Hauler"));
        parameters.Add(TerminalActionParameter.Get("Autopilot"));
        parameters.Add(new TerminalActionParameter());
      }

      if (currentTask != null)
      {
        if (IsDocked() && !currentTask.endWhen.Invoke())
        {
          Echo("Continuing current task");
          return;
        }
        currentTask = null;
      }

      for (int i = 0; i < allTasks.Count; i++)
        if (allTasks[i].startWhen == null || allTasks[i].startWhen.Invoke())
        {
          currentTask = allTasks[i];
          DisableStockpile();
          SendMessageToAutopilot(allTasks[i].commands);
          return;
        }
    }

    /// <returns>True iff the uranium mass is low</returns>
    bool UraniumLow()
    {
      UpdateReactorUranium();
      return uraniumMass < 3;
    }

    /// <returns>True iff the uranium mass is high</returns>
    bool UraniumHigh()
    {
      UpdateReactorUranium();
      return uraniumMass > 5;
    }

    /// <returns>True iff the hydrogne levels are low</returns>
    bool HydrogenLow()
    {
      UpdateGasStored();
      return hydrogenRatio < 0.5f;
    }

    /// <returns>True iff the hydrogen levels are high</returns>
    bool HydrogenHigh()
    {
      UpdateGasStored();
      return hydrogenRatio > 0.99f;
    }

    /// <returns>True iff the oxygen levels are low</returns>
    bool OxygenLow()
    {
      UpdateGasStored();
      return oxygenRatio < 0.25f;
    }

    /// <returns>True iff the oxygen levels are high</returns>
    bool OxygenHigh()
    {
      UpdateGasStored();
      return oxygenRatio > 0.99f;
    }

    /// <returns>True iff the cargo is partially full</returns>
    bool CargoPartFull()
    {
      UpdateCargoRatio();
      return cargoRatio > 0.5f;
    }

    /// <returns>True iff the cargo is empty.</returns>
    bool CargoEmpty()
    {
      UpdateCargoRatio();
      FiddleWithSorters();
      return cargoRatio < 0.01f;
    }

    /// <returns>True iff the cargo is full</returns>
    bool CargoFull()
    {
      UpdateCargoRatio();
      FiddleWithSorters();
      return cargoRatio > 0.99f;
    }

    /// <summary>
    /// Checks that a block is on the same grid as Me.
    /// </summary>
    /// <param name="block">The block that may by on my grid.</param>
    /// <returns>True iff block is on the same grid as Me.</returns>
    bool BlockOnMyGrid(IMyTerminalBlock block)
    {
      return block.CubeGrid == Me.CubeGrid;
    }

    /// <returns>True iff the ship is connected to another ship/station.</returns>
    bool IsDocked()
    {
      GridTerminalSystem.GetBlocksOfType<IMyShipConnector>(blocks, BlockOnMyGrid);
      for (int bi = 0; bi < blocks.Count; bi++)
        if ((blocks[bi] as IMyShipConnector).IsConnected)
          return true;
      return false;
    }

    /// <summary>
    /// Updates uraniumMass with values from the reactors.
    /// </summary>
    void UpdateReactorUranium()
    {
      if (DateTime.UtcNow < nextUpdate_uranium)
        return;
      nextUpdate_uranium = DateTime.UtcNow + updateInterval;

      uraniumMass = 0;

      GridTerminalSystem.GetBlocksOfType<IMyReactor>(blocks, BlockOnMyGrid);
      for (int bi = 0; bi < blocks.Count; bi++)
      {
        int inventoryCount = blocks[bi].GetInventoryCount();
        for (int ii = 0; ii < inventoryCount; ii++)
        {
          VRage.ModAPI.IMyInventory inventory = blocks[bi].GetInventory(ii);

          uraniumMass += inventory.CurrentMass;
        }
      }

      Echo("Uranium: " + uraniumMass);
    }

    /// <summary>
    /// Updates hydrogenStored and oxygenStored with values from the tanks.
    /// </summary>
    void UpdateGasStored()
    {
      if (DateTime.UtcNow < nextUpdate_gas)
        return;
      nextUpdate_gas = DateTime.UtcNow + updateInterval;

      float hydrogenStored = 0f, maxHydrogen = 0f, oxygenStored = 0f, maxOxygen = 0f;

      GridTerminalSystem.GetBlocksOfType<IMyOxygenTank>(blocks, BlockOnMyGrid);
      for (int bi = 0; bi < blocks.Count; bi++)
      {
        string name = blocks[bi].BlockDefinition.SubtypeId;
        if (name == "OxygenTankSmall")
        {
          oxygenStored += (blocks[bi] as IMyOxygenTank).GetOxygenLevel() * 50000f;
          maxOxygen += 50000f;
        }
        else if (name == "")
        {
          oxygenStored += (blocks[bi] as IMyOxygenTank).GetOxygenLevel() * 100000f;
          maxOxygen += 100000f;
        }
        else if (name == "LargeHydrogenTank")
        {
          hydrogenStored += (blocks[bi] as IMyOxygenTank).GetOxygenLevel() * 2500000f;
          maxHydrogen += 2500000f;
        }
        else if (name == "SmallHydrogenTank")
        {
          hydrogenStored += (blocks[bi] as IMyOxygenTank).GetOxygenLevel() * 40000f;
          maxHydrogen += 40000f;
        }
        else
          Echo("Unknown gas storage: " + blocks[bi].BlockDefinition.SubtypeId);
      }

      hydrogenRatio = hydrogenStored / maxHydrogen;
      oxygenRatio = oxygenStored / maxOxygen;

      Echo("Hydrogen: " + ToPercent(hydrogenRatio) + ", Oxygen: " + ToPercent(oxygenRatio));
    }

    /// <summary>
    /// Updates cargoVolume, cargoMaxVolume, and prev_cargo with values from
    /// the cargo containers.
    /// </summary>
    void UpdateCargoRatio()
    {
      if (DateTime.UtcNow < nextUpdate_cargo)
        return;
      nextUpdate_cargo = DateTime.UtcNow + updateInterval;

      prev_cargoRatio = cargoRatio;
      VRage.MyFixedPoint cargoVolume = 0, cargoMaxVolume = 0;

      GridTerminalSystem.GetBlocksOfType<IMyCargoContainer>(blocks, BlockOnMyGrid);
      for (int bi = 0; bi < blocks.Count; bi++)
      {
        int inventoryCount = blocks[bi].GetInventoryCount();
        for (int ii = 0; ii < inventoryCount; ii++)
        {
          VRage.ModAPI.IMyInventory inventory = blocks[bi].GetInventory(ii);

          VRage.MyFixedPoint cv = inventory.CurrentVolume;
          VRage.MyFixedPoint mv = inventory.MaxVolume;

          if (cv + cargoBuffer < mv)
            cargoVolume += cv;
          else
            cargoVolume += mv;

          cargoMaxVolume += mv;
        }
      }

      cargoRatio = (float)cargoVolume / (float)cargoMaxVolume;

      Echo ("Cargo: " + ToPercent(cargoRatio));
    }

    /// <summary>
    /// Turns Stockpile off for every tank.
    /// </summary>
    void DisableStockpile()
    {
      GridTerminalSystem.GetBlocksOfType<IMyOxygenTank>(blocks, BlockOnMyGrid);
      for (int bi = 0; bi < blocks.Count; bi++)
        if (blocks[bi].GetValue<bool>("Stockpile"))
          blocks[bi].ApplyAction("Stockpile_Off");
    }

    /// <summary>
    /// Sometimes the sorters stop working.
    /// Toggling them off and on seems to fix the problem.
    /// </summary>
    void FiddleWithSorters()
    {
      if (cargoRatio != prev_cargoRatio)
        return;

      Echo("Fiddling with sorters");

      GridTerminalSystem.GetBlocksOfType<IMyConveyorSorter>(blocks);
      for (int bi = 0; bi < blocks.Count; bi++)
      {
        IMyFunctionalBlock sorter = blocks[bi] as IMyFunctionalBlock;
        if (sorter.Enabled)
        {
          sorter.RequestEnable(false);
          sorter.RequestEnable(true);
        }
      }
    }

    /// <summary>
    /// Sends commands to the autopilot.
    /// </summary>
    /// <param name="content">The commands for the autopilot.</param>
    void SendMessageToAutopilot(string content)
    {
      Echo("AP: " + content);

      parameters[2] = TerminalActionParameter.Get(content);

      ITerminalAction action = Me.GetActionWithName("SendMessage");
      if (action == null)
        Echo("ARMS actions are not loaded. ARMS must be present in the first world loaded after " +
          "launching Space Engineers for actions to be loaded.");
      else
        Me.ApplyAction("SendMessage", parameters);
    }

    string ToPercent(float value)
    {
      return (value * 100f).ToString("F0") + " %";
    }

    class Task
    {
      /// <summary>When this function returns true, the task shall be started.</summary>
      public readonly Func<bool> startWhen;
      /// <summary>When this function returns true, the task shall be stopped.</summary>
      public readonly Func<bool> endWhen;
      /// <summary>The commands sent to autopilot when the task starts.</summary>
      public readonly string commands;

      public Task(Func<bool> start, Func<bool> end, string commands)
      {
        this.startWhen = start;
        this.endWhen = end;
        this.commands = commands;
      }
    }

  }
}
