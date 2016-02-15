using System.Collections.Generic;
using Sandbox.ModAPI.Ingame;
using Sandbox.ModAPI.Interfaces;

namespace Rynchodon.Programmable
{
  public class ProgrammableBlockCommunication : MyGridProgram
  {

    /*
     * Sends and receives messages for a Programmable block using ARMS' relay network.
     * Messages are sent by applying an action on the Programmable block, see SendMessage below
     * ARMS will run the programmable block when a message is received, for parsing the message
     * see Message below
     */

    /// <summary>This is defined by Rynchodon.AntennaRelay.ProgrammableBlock</summary>
    const char messageSeparator = '«';

    /// <summary>
    /// Arguments used to create a message, there must be exactly three and in this order:
    /// target grid, target block, content
    /// </summary>
    List<TerminalActionParameter> arguments = new List<TerminalActionParameter>();

    public void Main(string args)
    {
      if (!string.IsNullOrWhiteSpace(args))
      {
        // Example of receiving a message, prints the message to custom info
        Message msg;
        if (Message.TryDeserialize(args, out msg))
          Echo(msg.Content);
        else
          Echo("Failed to deserialize message: " + args);
        return;
      }

      // Example of sending a message, sends the message "Message" to "TargetBlock" on "TargetGrid"
      SendMessage("TargetGrid", "TargetBlock", "Message");
    }

    /// <summary>
    /// Sends a message to one or more programmable blocks.
    /// </summary>
    /// <param name="targetGrid">Every grid with targetGrid in the name will receive a message</param>
    /// <param name="targetBlock">Every Programmable Block with targetBloc in the name will receive a 
    /// message</param>
    /// <param name="content">The content of the message</param>
    void SendMessage(string targetGrid, string targetBlock, string content)
    {
      arguments.Clear();
      arguments.Add(TerminalActionParameter.Get(targetGrid));
      arguments.Add(TerminalActionParameter.Get(targetBlock));
      arguments.Add(TerminalActionParameter.Get(content));

      ITerminalAction action = Me.GetActionWithName("SendMessage");
      if (action == null)
        Echo("ARMS actions are not loaded. ARMS must be present in the first world loaded after " +
          "launching Space Engineers for actions to be loaded.");
      else
        Me.ApplyAction("SendMessage", arguments);
    }

    /// <summary>
    /// A message that was received from another Programmable block
    /// </summary>
    class Message
    {

      /// <summary>
      /// Attempts to create a message from a string.
      /// </summary>
      /// <param name="serial">The args parameter that was supplied to Main()</param>
      /// <param name="msg">The resultant message</param>
      /// <returns>True iff the message could be deserialized.</returns>
      public static bool TryDeserialize(string serial, out Message msg)
      {
        msg = new Message();
        string[] parts = serial.Split(messageSeparator);
        if (parts.Length != 3)
          return false;

        msg.SourceGrid = parts[0];
        msg.SourceBlock = parts[1];
        msg.Content = parts[2];

        return true;
      }

      /// <summary>The name of the grid that has the block that sent the message.</summary>
      public string SourceGrid;
      /// <summary>The name of the block that sent the message.</summary>
      public string SourceBlock;
      /// <summary>The content of the message.</summary>
      public string Content;

      private Message() { }

    }

  }
}
