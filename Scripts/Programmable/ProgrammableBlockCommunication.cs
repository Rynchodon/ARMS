using System.Collections.Generic;
using Sandbox.ModAPI.Ingame;
using Sandbox.ModAPI.Interfaces;

namespace Rynchodon.Programmable
{
  public class ProgrammableBlockCommunication : MyGridProgram
  {

    /// <summary>This is defined by Rynchodon.AntennaRelay.ProgrammableBlock</summary>
    const char messageSeparator = '«';

    List<TerminalActionParameter> arguments = new List<TerminalActionParameter>();

    public void Main(string args)
    {
      if (!string.IsNullOrWhiteSpace(args))
      {
        // receive a message
        Message msg;
        if (Message.TryDeserialize(args, out msg))
          Echo(msg.Content);
        return;
      }

      SendMessage("TargetGrid", "TargetBlock", "Message");
    }

    void SendMessage(string targetGrid, string targetBlock, string content)
    {
      arguments.Clear();
      arguments.Add(TerminalActionParameter.Get(targetGrid));
      arguments.Add(TerminalActionParameter.Get(targetBlock));
      arguments.Add(TerminalActionParameter.Get(content));

      ITerminalAction action = Me.GetActionWithName("SendMessage");
      if (action == null)
        Echo("ARMS actions are not loaded. ARMS must be present in the first world loaded after launching Space Engineers for actions to be loaded.");
      else
        Me.ApplyAction("SendMessage", arguments);
    }

    class Message
    {

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

      public string SourceGrid;
      public string SourceBlock;
      public string Content;

      private Message() { }

    }

  }
}
