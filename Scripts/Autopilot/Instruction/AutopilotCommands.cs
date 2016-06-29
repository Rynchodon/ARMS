using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using Rynchodon.Autopilot.Instruction.Command;
using Rynchodon.Autopilot.Movement;
using Sandbox.Game.Entities;
using Sandbox.Game.Gui;
using Sandbox.ModAPI;
using Sandbox.ModAPI.Interfaces.Terminal;
using VRage.ModAPI;
using VRage.Utils;
using VRageMath;

namespace Rynchodon.Autopilot.Instruction
{
	public class AutopilotCommands
	{

		private class StaticVariables
		{
			public Dictionary<char, List<ACommand>> dummyCommands = new Dictionary<char, List<ACommand>>();
			public AddCommandInternalNode addCommandRoot;
		}

		private static StaticVariables Static = new StaticVariables();

		static AutopilotCommands()
		{
			MyAPIGateway.Entities.OnCloseAll += Entities_OnCloseAll;

			List<AddCommandInternalNode> rootCommands = new List<AddCommandInternalNode>();

			// fly somewhere

			List<AddCommandLeafNode> flyCommands = new List<AddCommandLeafNode>();

			AddDummy(new GolisCoordinate(), flyCommands);
			flyCommands.Add(new AddCommandLeafNode(new GolisGps()));
			AddDummy(new FlyRelative(), flyCommands);
			AddDummy(new Character(), flyCommands);
			AddDummy(new LandVoxel(), flyCommands);
			AddDummy(new Orbit(), flyCommands);
			AddDummy(new Weld(), flyCommands);

			rootCommands.Add(new AddCommandInternalNode("Fly Somewhere", flyCommands.ToArray()));

			// flow control

			List<AddCommandLeafNode> flowCommands = new List<AddCommandLeafNode>();

			AddDummy(new Wait(), flowCommands);
			AddDummy(new Disable(), flowCommands);
			AddDummy(new Exit(), flowCommands);
			AddDummy(new Stop(), flowCommands);

			rootCommands.Add(new AddCommandInternalNode("Flow control", flowCommands.ToArray()));

			// terminal action/property

			List<AddCommandLeafNode> termActProp = new List<AddCommandLeafNode>();
			AddDummy(new TerminalAction(), termActProp);
			AddDummy(new TerminalPropertyBool(), termActProp);
			AddDummy(new TerminalPropertyFloat(), termActProp);
			AddDummy(new TerminalPropertyColour(), termActProp);

			rootCommands.Add(new AddCommandInternalNode("Terminal", termActProp.ToArray()));

			Static.addCommandRoot = new AddCommandInternalNode("root", rootCommands.ToArray());

			AddDummy(new BlockSearch());
		}

		private static void Entities_OnCloseAll()
		{
			MyAPIGateway.Entities.OnCloseAll -= Entities_OnCloseAll;
			Static = null;
		}

		private static void AddDummy(ACommand command, string idOrAlias)
		{
			List<ACommand> list;
			if (!Static.dummyCommands.TryGetValue(idOrAlias[0], out list))
			{
				list = new List<ACommand>();
				Static.dummyCommands.Add(idOrAlias[0], list);
			}
			list.Add(command);
		}

		private static void AddDummy(ACommand command, List<AddCommandLeafNode> children = null)
		{
			AddDummy(command, command.Identifier);
			string[] aliases = command.Aliases;
			if (aliases != null)
				foreach (string ali in aliases)
					AddDummy(command, ali);
			if (children != null)
				children.Add(new AddCommandLeafNode(command));
		}

		public static AutopilotCommands GetOrCreate(IMyTerminalBlock block)
		{
			if (Globals.WorldClosed)
				return null;
			AutopilotCommands result;
			if (!Registrar.TryGetValue(block, out result))
				result = new AutopilotCommands(block);
			return result;
		}

		/// <summary>
		/// Get the best command associated with a string.
		/// </summary>
		/// <param name="parse">The complete command string, including Identifier.</param>
		/// <returns>The best command associated with parse.</returns>
		private static ACommand GetCommand(string parse)
		{
			parse = parse.TrimStart();

			List<ACommand> list;
			if (!Static.dummyCommands.TryGetValue(parse[0], out list))
				return null;

			ACommand bestMatch = null;
			int bestMatchLength = 0;
			foreach (ACommand cmd in list)
				if (cmd.Identifier.Length > bestMatchLength && parse.StartsWith(cmd.Identifier))
				{
					bestMatchLength = cmd.Identifier.Length;
					bestMatch = cmd;
				}

			if (bestMatch == null)
				return null;
			return bestMatch.Clone();
		}

		private readonly IMyTerminalBlock m_block;
		private readonly List<ACommand> m_commandList = new List<ACommand>();
		private readonly StringBuilder m_syntaxErrors = new StringBuilder();
		private readonly Logger m_logger;

		private IMyTerminalControlListbox m_termCommandList;
		private bool m_listCommands = true, m_replace;
		private int m_insertIndex;
		private ACommand m_currentCommand;
		private Stack<AddCommandInternalNode> m_currentAddNode = new Stack<AddCommandInternalNode>();
		/// <summary>Action executed once programming ends.</summary>
		private Action m_completionCallback;

		private AutopilotCommands(IMyTerminalBlock block)
		{
			this.m_block = block;
			this.m_logger = new Logger(GetType().Name, block);

			Registrar.Add(block, this);
		}

		/// <summary>
		/// Parse the commands from AutopilotTerminal.GetAutopilotCommands.
		/// </summary>
		public void ParseCommands()
		{
			m_commandList.Clear();
			m_syntaxErrors.Clear();

			string allCommands = AutopilotTerminal.GetAutopilotCommands(m_block).ToString();
			if (string.IsNullOrWhiteSpace(allCommands))
				return;

			// TODO: GPS replace

			string[] commands = allCommands.Split(new char[] { ';', ':' });
			foreach (string cmd in commands)
			{
				if (string.IsNullOrWhiteSpace(cmd))
					continue;

				// TODO: text panel support

				ACommand apCmd = GetCommand(cmd);
				if (apCmd == null)
				{
					m_syntaxErrors.AppendLine("No command: \"" + cmd + '"');
					continue;
				}

				string msg;
				Action<Mover> execute = apCmd.SetDisplayString(cmd, out msg);
				if (execute == null)
				{
					m_syntaxErrors.Append("Error with command: \"");
					m_syntaxErrors.Append(cmd);
					m_syntaxErrors.Append("\":\n  ");
					m_syntaxErrors.AppendLine(msg);
					m_logger.debugLog("Error with command: \"" + cmd + "\":\n  " + msg, Logger.severity.INFO);
					continue;
				}

				m_commandList.Add(apCmd);
			}
		}

		public void StartGooeyProgramming(Action completionCallback)
		{
			m_logger.debugLog("entered");

			ParseCommands();
			m_currentCommand = null;
			m_listCommands = true;

			if (m_syntaxErrors.Length != 0)
				m_block.AppendCustomInfo("Syntax Errors:\n" + m_syntaxErrors);

			m_completionCallback = completionCallback;
			MyTerminalControls.Static.CustomControlGetter += CustomControlGetter;
			m_block.SwitchTerminalTo();
		}

		private void CustomControlGetter(IMyTerminalBlock block, List<IMyTerminalControl> controls)
		{
			//m_logger.debugLog("entered");

			if (block != m_block)
				return;

			controls.Clear();

			if (m_listCommands)
			{
				m_logger.debugLog("showing command list");

				if (m_termCommandList == null)
				{
					m_termCommandList = new MyTerminalControlListbox<MyShipController>("CommandList", MyStringId.GetOrCompute("Commands"), MyStringId.NullOrEmpty, false, 10);
					m_termCommandList.ListContent = ListCommands;
					m_termCommandList.ItemSelected = CommandSelected;
				}
				controls.Add(m_termCommandList);

				controls.Add(new MyTerminalControlButton<MyShipController>("AddCommand", MyStringId.GetOrCompute("Add Command"), MyStringId.NullOrEmpty, AddCommand));
				controls.Add(new MyTerminalControlButton<MyShipController>("InsertCommand", MyStringId.GetOrCompute("Insert Command"), MyStringId.NullOrEmpty, InsertCommand));
				controls.Add(new MyTerminalControlButton<MyShipController>("RemoveCommand", MyStringId.GetOrCompute("Remove Command"), MyStringId.NullOrEmpty, RemoveCommand));
				controls.Add(new MyTerminalControlButton<MyShipController>("EditCommand", MyStringId.GetOrCompute("Edit Command"), MyStringId.NullOrEmpty, EditCommand));
				controls.Add(new MyTerminalControlButton<MyShipController>("MoveCommandUp", MyStringId.GetOrCompute("Move Command Up"), MyStringId.NullOrEmpty, MoveCommandUp));
				controls.Add(new MyTerminalControlButton<MyShipController>("MoveCommandDown", MyStringId.GetOrCompute("Move Command Down"), MyStringId.NullOrEmpty, MoveCommandDown));
				controls.Add(new MyTerminalControlSeparator<MyShipController>());
				controls.Add(new MyTerminalControlButton<MyShipController>("Finished", MyStringId.GetOrCompute("Finished Programming"), MyStringId.NullOrEmpty, Finished));

				return;
			}

			if (m_currentCommand == null)
			{
				// add/insert new command
				if (m_currentAddNode.Count == 0)
					m_currentAddNode.Push(Static.addCommandRoot);

				foreach (AddCommandTreeNode child in m_currentAddNode.Peek().Children)
					controls.Add(new MyTerminalControlButton<MyShipController>(child.Name.RemoveWhitespace(), MyStringId.GetOrCompute(child.Name), MyStringId.GetOrCompute(child.Tooltip), shipController => {
						AddCommandLeafNode leaf = child as AddCommandLeafNode;
						if (leaf != null)
						{
							m_currentCommand = leaf.Command.Clone();
							if (!m_currentCommand.HasControls)
								CheckAndSave(block);
							m_currentAddNode.Clear();
						}
						else
							m_currentAddNode.Push((AddCommandInternalNode)child);
						shipController.SwitchTerminalTo();
					}));

				controls.Add(new MyTerminalControlButton<MyShipController>("UpOneLevel", MyStringId.GetOrCompute("Up one level"), MyStringId.GetOrCompute("Return to previous list"), shipController => {
					m_currentAddNode.Pop();
					if (m_currentAddNode.Count == 0)
						m_listCommands = true;
					shipController.SwitchTerminalTo();
				}));

				return;
			}

			m_logger.debugLog("showing single command: " + m_currentCommand.Identifier);

			m_currentCommand.AddControls(controls);
			controls.Add(new MyTerminalControlSeparator<MyShipController>());
			controls.Add(new MyTerminalControlButton<MyShipController>("SaveGooeyCommand", MyStringId.GetOrCompute("Check & Save"), MyStringId.GetOrCompute("Check the current command for syntax errors and save it"), CheckAndSave));
			controls.Add(new MyTerminalControlButton<MyShipController>("DiscardGooeyCommand", MyStringId.GetOrCompute("Discard"), MyStringId.GetOrCompute("Discard the current command"), Discard));
		}

		private void ListCommands(IMyTerminalBlock block, List<MyTerminalControlListBoxItem> allItems, List<MyTerminalControlListBoxItem> selected)
		{
			m_logger.debugLog(block != m_block, "block != m_block", Logger.severity.FATAL);

			foreach (ACommand command in m_commandList)
			{
				// this will leak memory, as MyTerminalControlListBoxItem uses MyStringId for some stupid reason
				MyTerminalControlListBoxItem item = new MyTerminalControlListBoxItem(MyStringId.GetOrCompute(command.DisplayString), MyStringId.GetOrCompute(command.Description), command);
				allItems.Add(item);
				if (command == m_currentCommand && selected.Count == 0)
					selected.Add(item);
			}
		}

		private void CommandSelected(IMyTerminalBlock block, List<MyTerminalControlListBoxItem> selected)
		{
			m_logger.debugLog(block != m_block, "block != m_block", Logger.severity.FATAL);
			m_logger.debugLog(selected.Count > 1, "selected.Count: " + selected.Count, Logger.severity.ERROR);

			if (selected.Count == 0)
			{
				m_currentCommand = null;
				m_logger.debugLog("selection cleared");
			}
			else
			{
				m_currentCommand = (ACommand)selected[0].UserData;
				m_logger.debugLog("selected: " + m_currentCommand.DisplayString);
			}
		}

		#region Button Action

		private void Discard(IMyTerminalBlock block)
		{
			m_logger.debugLog("entered");

			m_logger.debugLog(block != m_block, "block != m_block", Logger.severity.FATAL);
			m_logger.debugLog(m_currentCommand == null, "m_currentCommand == null", Logger.severity.FATAL);

			m_currentCommand = null;
			m_listCommands = true;

			m_block.SwitchTerminalTo();
		}

		private void CheckAndSave(IMyTerminalBlock block)
		{
			m_logger.debugLog(block != m_block, "block != m_block", Logger.severity.FATAL);
			m_logger.debugLog(m_currentCommand == null, "m_currentCommand == null", Logger.severity.FATAL);

			string msg;
			Action<Mover> execute = m_currentCommand.ValidateControls(out msg);
			if (execute != null)
			{
				if (m_commandList.Contains(m_currentCommand))
				{
					m_logger.debugLog("edited command: " + m_currentCommand.DisplayString);
				}
				else
				{
					if (m_insertIndex == -1)
					{
						m_logger.debugLog("new command: " + m_currentCommand.DisplayString);
						m_commandList.Add(m_currentCommand);
					}
					else
					{
						if (m_replace)
						{
							m_commandList.RemoveAt(m_insertIndex);
							m_logger.debugLog("replace at " + m_insertIndex + ": " + m_currentCommand.DisplayString);
						}
						else
							m_logger.debugLog("new command at " + m_insertIndex + ": " + m_currentCommand.DisplayString);
						m_commandList.Insert(m_insertIndex, m_currentCommand);
					}
				}
				m_currentCommand = null;
				m_listCommands = true;
			}
			else
			{
				m_logger.debugLog("failed to save command: " + m_currentCommand.DisplayString + ", reason: " + msg);
				m_block.AppendCustomInfo(msg);
			}

			m_block.SwitchTerminalTo();
		}

		private void AddCommand(IMyTerminalBlock block)
		{
			m_logger.debugLog(block != m_block, "block != m_block", Logger.severity.FATAL);

			m_insertIndex = -1;
			m_replace = false;
			m_currentCommand = null;
			m_listCommands = false;
			m_block.SwitchTerminalTo();
			m_logger.debugLog("adding new command at end");
		}

		private void InsertCommand(IMyTerminalBlock block)
		{
			m_logger.debugLog(block != m_block, "block != m_block", Logger.severity.FATAL);
			
			if (m_currentCommand == null)
			{
				LogAndInfo("nothing selected");
				return;
			}

			m_insertIndex = m_commandList.IndexOf(m_currentCommand);
			m_replace = false;
			m_currentCommand = null;
			m_listCommands = false;
			m_block.SwitchTerminalTo();
			m_logger.debugLog("inserting new command at " + m_insertIndex);
		}

		private void RemoveCommand(IMyTerminalBlock block)
		{
			m_logger.debugLog("entered");

			m_logger.debugLog(block != m_block, "block != m_block", Logger.severity.FATAL);

			if (m_currentCommand == null)
			{
				LogAndInfo("nothing selected");
				return;
			}

			m_commandList.Remove(m_currentCommand);
			m_currentCommand = null;
			m_termCommandList.UpdateVisual();
		}

		private void EditCommand(IMyTerminalBlock block)
		{
			m_logger.debugLog(block != m_block, "block != m_block", Logger.severity.FATAL);

			if (m_currentCommand == null)
			{
				LogAndInfo("nothing selected");
				return;
			}

			if (!m_currentCommand.HasControls)
			{
				LogAndInfo("This command cannot be edited");
				return;
			}

			m_logger.debugLog("editing: " + m_currentCommand.DisplayString);

			m_insertIndex = m_commandList.IndexOf(m_currentCommand);
			m_replace = true;
			m_currentCommand = m_currentCommand.Clone();
			m_listCommands = false;
			m_block.SwitchTerminalTo();
		}

		private void MoveCommandUp(IMyTerminalBlock block)
		{
			m_logger.debugLog(block != m_block, "block != m_block", Logger.severity.FATAL);
			
			if (m_currentCommand == null)
			{
				LogAndInfo("nothing selected");
				return;
			}

			int index = m_commandList.IndexOf(m_currentCommand);
			if (index == 0)
			{
				LogAndInfo("already first element: " + m_currentCommand.DisplayString);
				return;
			}
			m_logger.debugLog("moved up: " + m_currentCommand.DisplayString);
			m_commandList.Swap(index, index - 1);
			m_termCommandList.UpdateVisual();
		}

		private void MoveCommandDown(IMyTerminalBlock block)
		{
			m_logger.debugLog(block != m_block, "block != m_block", Logger.severity.FATAL);
			
			if (m_currentCommand == null)
			{
				LogAndInfo("nothing selected");
				return;
			}

			int index = m_commandList.IndexOf(m_currentCommand);
			if (index == m_commandList.Count - 1)
			{
				LogAndInfo("already last element: " + m_currentCommand.DisplayString);
				return;
			}
			m_logger.debugLog("moved down: " + m_currentCommand.DisplayString);
			m_commandList.Swap(index, index + 1);
			m_termCommandList.UpdateVisual();
		}

		private void Finished(IMyTerminalBlock block)
		{
			m_logger.debugLog("entered");

			m_logger.debugLog(block != m_block, "block != m_block", Logger.severity.FATAL);

			AutopilotTerminal.SetAutopilotCommands(m_block, new StringBuilder(string.Join(" ; ", m_commandList.Select(cmd => cmd.DisplayString))));
			MyTerminalControls.Static.CustomControlGetter -= CustomControlGetter;
			m_block.SwitchTerminalTo();

			m_termCommandList = null;
			m_currentCommand = null;

			if (m_completionCallback != null)
			{
				m_completionCallback.Invoke();
				m_completionCallback = null;
			}
		}

		#endregion Button Action

		private void LogAndInfo(string message, [CallerMemberName] string member = null, [CallerLineNumber] int lineNumber = 0)
		{
			m_logger.debugLog(message, member: member, lineNumber: lineNumber);
			m_block.AppendCustomInfo(message);
			m_block.SwitchTerminalTo();
		}

	}
}
