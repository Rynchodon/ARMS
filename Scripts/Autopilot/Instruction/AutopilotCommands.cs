using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.RegularExpressions;
using Rynchodon.Autopilot.Instruction.Command;
using Rynchodon.Utility;
using Sandbox.Game.Entities;
using Sandbox.Game.Gui;
using Sandbox.ModAPI;
using Sandbox.ModAPI.Interfaces.Terminal;
using VRage.Game.ModAPI;
using VRage.ModAPI;
using VRage.Utils;

namespace Rynchodon.Autopilot.Instruction
{
	/// <summary>
	/// GUI programming and command interpretation.
	/// </summary>
	public class AutopilotCommands
	{

		private class StaticVariables
		{
			public readonly Regex GPS_tag = new Regex(@"[^c]\s*GPS:.*?:(-?\d+\.?\d*):(-?\d+\.?\d*):(-?\d+\.?\d*):");
			public readonly string GPS_replaceWith = @"cg $1, $2, $3";
			public Dictionary<char, List<ACommand>> dummyCommands = new Dictionary<char, List<ACommand>>();
			public AddCommandInternalNode addCommandRoot;
		}

		private static StaticVariables Static = new StaticVariables();

		static AutopilotCommands()
		{
			List<AddCommandInternalNode> rootCommands = new List<AddCommandInternalNode>();

			// fly somewhere

			List<AddCommandLeafNode> commands = new List<AddCommandLeafNode>();

			AddDummy(new GolisCoordinate(), commands);
			AddDummy(new GolisGps(), commands);
			AddDummy(new FlyRelative(), commands);
			AddDummy(new Character(), commands);

			rootCommands.Add(new AddCommandInternalNode("Fly Somewhere", commands.ToArray()));

			// friendly grid

			commands.Clear();

			AddDummy(new TargetBlockSearch(), commands);
			AddDummy(new LandingBlock(), commands);
			AddDummy(new Offset(), commands);
			AddDummy(new Form(), commands);
			AddDummy(new GridDestination(), commands);
			AddDummy(new Unland(), commands);
			AddDummy(new UnlandBlock(), commands);

			rootCommands.Add(new AddCommandInternalNode("Fly to a Ship", commands.ToArray()));

			// complex task

			commands.Clear();

			AddDummy(new Enemy(), commands);
			AddDummy(new HarvestVoxel(), commands);
			AddDummy(new Grind(), commands);
			AddDummy(new Weld(), commands);
			AddDummy(new LandVoxel(), commands);
			AddDummy(new Orbit(), commands);
			AddDummy(new NavigationBlock(), commands);
			AddDummy(new FaceMove(), commands);

			rootCommands.Add(new AddCommandInternalNode("Tasks", commands.ToArray()));

			// variables

			commands.Clear();

			AddDummy(new Proximity(), commands);
			AddDummy(new SpeedLimit(), commands);
			AddDummy(new Jump(), commands);
			AddDummy(new StraightLine(), commands);
			AddDummy(new Asteroid(), commands);

			rootCommands.Add(new AddCommandInternalNode("Variables", commands.ToArray()));

			// flow control

			commands.Clear();

			AddDummy(new TextPanel(), commands);
			AddDummy(new Wait(), commands);
			AddDummy(new Exit(), commands);
			AddDummy(new Stop(), commands);
			AddDummy(new Disable(), commands);
			AddDummy(new WaitForBatteryRecharge(), commands);

			rootCommands.Add(new AddCommandInternalNode("Flow Control", commands.ToArray()));

			// terminal action/property

			commands.Clear();

			AddDummy(new TerminalAction(), commands);
			AddDummy(new TerminalPropertyBool(), commands);
			AddDummy(new TerminalPropertyFloat(), commands);
			AddDummy(new TerminalPropertyColour(), commands);

			rootCommands.Add(new AddCommandInternalNode("Terminal", commands.ToArray()));

			Static.addCommandRoot = new AddCommandInternalNode("root", rootCommands.ToArray());
		}

		private static void AddDummy(ACommand command, string idOrAlias)
		{
			List<ACommand> list;
			if (!Static.dummyCommands.TryGetValue(idOrAlias[0], out list))
			{
				list = new List<ACommand>();
				Static.dummyCommands.Add(idOrAlias[0], list);
			}
			if (!list.Contains(command))
				list.Add(command);
		}

		private static void AddDummy(ACommand command, List<AddCommandLeafNode> children = null)
		{
			foreach (string idOrAlias in command.IdAndAliases())
				AddDummy(command, idOrAlias);
			
			if (children != null)
				children.Add(new AddCommandLeafNode(command));
		}

		public static AutopilotCommands GetOrCreate(IMyTerminalBlock block)
		{
			if (Globals.WorldClosed || block.Closed)
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
			parse = parse.TrimStart().ToLower();

			List<ACommand> list;
			if (!Static.dummyCommands.TryGetValue(parse[0], out list))
				return null;

			ACommand bestMatch = null;
			int bestMatchLength = 0;
			foreach (ACommand cmd in list)
				foreach (string idOrAlias in cmd.IdAndAliases())
					if (idOrAlias.Length > bestMatchLength && parse.StartsWith(idOrAlias))
					{
						bestMatchLength = idOrAlias.Length;
						bestMatch = cmd;
					}

			if (bestMatch == null)
				return null;
			return bestMatch.Clone();
		}

		private readonly IMyTerminalBlock m_block;
		/// <summary>Command list for GUI programming, not to be used by others</summary>
		private readonly List<ACommand> m_commandList = new List<ACommand>();

		/// <summary>Shared from any command source</summary>
		private readonly StringBuilder m_syntaxErrors = new StringBuilder();
		/// <summary>Action list for GUI programming and commands text box, not to be used for messaged commands.</summary>
		private readonly AutopilotActionList m_actionList = new AutopilotActionList();

		private IMyTerminalControlListbox m_termCommandList;
		private bool m_listCommands = true, m_replace;
		private int m_insertIndex;
		private ACommand m_currentCommand;
		private Stack<AddCommandInternalNode> m_currentAddNode = new Stack<AddCommandInternalNode>();
		private string m_infoMessage, m_commands;

		private Logable Log { get { return new Logable(m_block); } }

		/// <summary>
		/// The most recent commands from either terminal or a message.
		/// </summary>
		public string Commands
		{
			get { return m_commands; }
			private set
			{
				m_commands = value;
				Log.AlwaysLog("Commands: " + m_commands); // for bug reports
			}
		}

		public bool HasSyntaxErrors { get { return m_syntaxErrors.Length != 0; } }

		private AutopilotCommands(IMyTerminalBlock block)
		{
			this.m_block = block;

			m_block.AppendingCustomInfo += m_block_AppendSyntaxErrors;

			Registrar.Add(block, this);
		}

		/// <summary>
		/// Invoke when commands textbox changed.
		/// </summary>
		public void OnCommandsChanged()
		{
			Log.DebugLog("entered");
			m_actionList.Clear();
		}

		public void StartGooeyProgramming()
		{
			Log.DebugLog("entered");

			using (MainLock.AcquireSharedUsing())
			{
				m_currentCommand = null;
				m_listCommands = true;
				m_commandList.Clear();
				m_syntaxErrors.Clear();

				MyTerminalControls.Static.CustomControlGetter += CustomControlGetter;
				m_block.AppendingCustomInfo += m_block_AppendingCustomInfo;

				Commands = AutopilotTerminal.GetAutopilotCommands(m_block).ToString();
				foreach (ACommand comm in ParseCommands(Commands))
					m_commandList.Add(comm);
				if (m_syntaxErrors.Length != 0)
					m_block.UpdateCustomInfo();
				m_block.RebuildControls();
			}
		}

		public AutopilotActionList GetActions()
		{
			using (MainLock.AcquireSharedUsing())
			{
				if (!m_actionList.IsEmpty)
				{
					m_actionList.Reset();
					return m_actionList;
				}
				m_syntaxErrors.Clear();

				Commands = AutopilotTerminal.GetAutopilotCommands(m_block).ToString();
				List<ACommand> commands = new List<ACommand>();
				GetActions(Commands, m_actionList);
				if (m_syntaxErrors.Length != 0)
					m_block.UpdateCustomInfo();
				return m_actionList;
			}
		}

		public AutopilotActionList GetActions(string allCommands)
		{
			using (MainLock.AcquireSharedUsing())
			{
				Commands = allCommands;
				m_syntaxErrors.Clear();

				AutopilotActionList actList = new AutopilotActionList();
				GetActions(Commands, actList);
				if (m_syntaxErrors.Length != 0)
					m_block.UpdateCustomInfo();
				return actList;
			}
		}

		public void GetActions(string allCommands, AutopilotActionList actionList)
		{
			GetActions(ParseCommands(allCommands), actionList);
		}

		private IEnumerable<ACommand> ParseCommands(string allCommands)
		{
			if (string.IsNullOrWhiteSpace(allCommands))
			{
				Logger.DebugLog("no commands");
				yield break;
			}

			allCommands = Static.GPS_tag.Replace(allCommands, Static.GPS_replaceWith);

			string[] commands = allCommands.Split(new char[] { ';', ':' });
			foreach (string cmd in commands)
			{
				if (string.IsNullOrWhiteSpace(cmd))
				{
					Logger.DebugLog("empty command");
					continue;
				}

				ACommand apCmd = GetCommand(cmd);
				if (apCmd == null)
				{
					m_syntaxErrors.AppendLine("No command: \"" + cmd + '"');
					Logger.DebugLog("No command: \"" + cmd + '"');
					continue;
				}

				string msg;
				if (!apCmd.SetDisplayString((IMyCubeBlock)m_block, cmd, out msg))
				{
					m_syntaxErrors.Append("Error with command: \"");
					m_syntaxErrors.Append(cmd);
					m_syntaxErrors.Append("\":\n  ");
					m_syntaxErrors.AppendLine(msg);
					Logger.DebugLog("Error with command: \"" + cmd + "\":\n  " + msg, Logger.severity.INFO);
					continue;
				}

				yield return apCmd;
			}
		}

		private void GetActions(IEnumerable<ACommand> commandList, AutopilotActionList actionList)
		{
			int count = 0;
			const int limit = 1000;

			foreach (ACommand cmd in commandList)
			{
				TextPanel tp = cmd as TextPanel;
				if (tp == null)
				{
					if (cmd.Action == null)
					{
						Logger.AlwaysLog("Command is missing action: " + cmd.DisplayString, Logger.severity.ERROR);
						continue;
					}

					if (++count > limit)
					{
						Logger.DebugLog("Reached command limit");
						m_syntaxErrors.AppendLine("Reached command limit");
						return;
					}
					Logger.DebugLog("yield: " + cmd.DisplayString);
					actionList.Add(cmd.Action);
					continue;
				}

				TextPanelMonitor textPanelMonitor = tp.GetTextPanelMonitor(m_block, this);
				if (textPanelMonitor == null)
				{
					Logger.DebugLog("Text panel not found: " + tp.SearchPanelName);
					m_syntaxErrors.Append("Text panel not found: ");
					m_syntaxErrors.AppendLine(tp.SearchPanelName);
					continue;
				}
				if (textPanelMonitor.AutopilotActions.IsEmpty)
				{
					Logger.DebugLog(textPanelMonitor.TextPanel.DisplayNameText + " has no commands");
					m_syntaxErrors.Append(textPanelMonitor.TextPanel.DisplayNameText);
					m_syntaxErrors.AppendLine(" has no commands");
					continue;
				}

				actionList.Add(textPanelMonitor);
			}

			actionList.Reset();
		}

		private void CustomControlGetter(IMyTerminalBlock block, List<IMyTerminalControl> controls)
		{
			//Log.DebugLog("entered");

			if (block != m_block)
				return;

			controls.Clear();

			if (m_listCommands)
			{
				Log.DebugLog("showing command list");

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
				controls.Add(new MyTerminalControlButton<MyShipController>("Finished", MyStringId.GetOrCompute("Save & Exit"), MyStringId.GetOrCompute("Save all commands and exit"), b => Finished(true)));
				controls.Add(new MyTerminalControlButton<MyShipController>("DiscardAll", MyStringId.GetOrCompute("Discard & Exit"), MyStringId.GetOrCompute("Discard all commands and exit"), b => Finished(false)));

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
						ClearMessage();
					}));

				controls.Add(new MyTerminalControlButton<MyShipController>("UpOneLevel", MyStringId.GetOrCompute("Up one level"), MyStringId.GetOrCompute("Return to previous list"), shipController => {
					m_currentAddNode.Pop();
					if (m_currentAddNode.Count == 0)
						m_listCommands = true;
					shipController.RebuildControls();
				}));

				return;
			}

			Log.DebugLog("showing single command: " + m_currentCommand.Identifier);

			m_currentCommand.AddControls(controls);
			controls.Add(new MyTerminalControlSeparator<MyShipController>());
			controls.Add(new MyTerminalControlButton<MyShipController>("SaveGooeyCommand", MyStringId.GetOrCompute("Check & Save"), MyStringId.GetOrCompute("Check the current command for syntax errors and save it"), CheckAndSave));
			controls.Add(new MyTerminalControlButton<MyShipController>("DiscardGooeyCommand", MyStringId.GetOrCompute("Discard"), MyStringId.GetOrCompute("Discard the current command"), Discard));
		}

		private void ListCommands(IMyTerminalBlock block, List<MyTerminalControlListBoxItem> allItems, List<MyTerminalControlListBoxItem> selected)
		{
			Log.DebugLog("block != m_block", Logger.severity.FATAL, condition: block != m_block);

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
			Log.DebugLog("block != m_block", Logger.severity.FATAL, condition: block != m_block);
			Log.DebugLog("selected.Count: " + selected.Count, Logger.severity.ERROR, condition: selected.Count > 1);

			if (selected.Count == 0)
			{
				m_currentCommand = null;
				Log.DebugLog("selection cleared");
			}
			else
			{
				m_currentCommand = (ACommand)selected[0].UserData;
				Log.DebugLog("selected: " + m_currentCommand.DisplayString);
			}
		}

		#region Button Action

		private void Discard(IMyTerminalBlock block)
		{
			Log.DebugLog("entered");

			Log.DebugLog("block != m_block", Logger.severity.FATAL, condition: block != m_block);
			Log.DebugLog("m_currentCommand == null", Logger.severity.FATAL, condition: m_currentCommand == null);

			m_currentCommand = null;
			m_listCommands = true;

			ClearMessage();
		}

		private void CheckAndSave(IMyTerminalBlock block)
		{
			Log.DebugLog("block != m_block", Logger.severity.FATAL, condition: block != m_block);
			Log.DebugLog("m_currentCommand == null", Logger.severity.FATAL, condition: m_currentCommand == null);

			string msg;
			if (m_currentCommand.ValidateControls((IMyCubeBlock)m_block, out msg))
			{
				if (m_commandList.Contains(m_currentCommand))
				{
					Log.DebugLog("edited command: " + m_currentCommand.DisplayString);
				}
				else
				{
					if (m_insertIndex == -1)
					{
						Log.DebugLog("new command: " + m_currentCommand.DisplayString);
						m_commandList.Add(m_currentCommand);
					}
					else
					{
						if (m_replace)
						{
							m_commandList.RemoveAt(m_insertIndex);
							Log.DebugLog("replace at " + m_insertIndex + ": " + m_currentCommand.DisplayString);
						}
						else
							Log.DebugLog("new command at " + m_insertIndex + ": " + m_currentCommand.DisplayString);
						m_commandList.Insert(m_insertIndex, m_currentCommand);
					}
				}
				m_currentCommand = null;
				m_listCommands = true;
				ClearMessage();
			}
			else
			{
				Log.DebugLog("failed to save command: " + m_currentCommand.DisplayString + ", reason: " + msg);
				LogAndInfo(msg);
			}
		}

		private void AddCommand(IMyTerminalBlock block)
		{
			Log.DebugLog("block != m_block", Logger.severity.FATAL, condition: block != m_block);

			m_insertIndex = -1;
			m_replace = false;
			m_currentCommand = null;
			m_listCommands = false;
			ClearMessage();
			Log.DebugLog("adding new command at end");
		}

		private void InsertCommand(IMyTerminalBlock block)
		{
			Log.DebugLog("block != m_block", Logger.severity.FATAL, condition: block != m_block);

			if (m_currentCommand == null)
			{
				LogAndInfo("nothing selected");
				return;
			}

			m_insertIndex = m_commandList.IndexOf(m_currentCommand);
			m_replace = false;
			m_currentCommand = null;
			m_listCommands = false;
			ClearMessage();
			Log.DebugLog("inserting new command at " + m_insertIndex);
		}

		private void RemoveCommand(IMyTerminalBlock block)
		{
			Log.DebugLog("entered");

			Log.DebugLog("block != m_block", Logger.severity.FATAL, condition: block != m_block);

			if (m_currentCommand == null)
			{
				LogAndInfo("nothing selected");
				return;
			}

			m_commandList.Remove(m_currentCommand);
			m_currentCommand = null;
			ClearMessage();
		}

		private void EditCommand(IMyTerminalBlock block)
		{
			Log.DebugLog("block != m_block", Logger.severity.FATAL, condition: block != m_block);

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

			Log.DebugLog("editing: " + m_currentCommand.DisplayString);

			m_insertIndex = m_commandList.IndexOf(m_currentCommand);
			m_replace = true;
			m_currentCommand = m_currentCommand.Clone();
			m_listCommands = false;
			ClearMessage();
		}

		private void MoveCommandUp(IMyTerminalBlock block)
		{
			Log.DebugLog("block != m_block", Logger.severity.FATAL, condition: block != m_block);

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
			Log.DebugLog("moved up: " + m_currentCommand.DisplayString);
			m_commandList.Swap(index, index - 1);
			ClearMessage();
		}

		private void MoveCommandDown(IMyTerminalBlock block)
		{
			Log.DebugLog("block != m_block", Logger.severity.FATAL, condition: block != m_block);

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
			Log.DebugLog("moved down: " + m_currentCommand.DisplayString);
			m_commandList.Swap(index, index + 1);
			ClearMessage();
		}

		private void Finished(bool save)
		{
			Log.DebugLog("entered");

			if (save)
			{
				Commands = string.Join(" ; ", m_commandList.Select(cmd => cmd.DisplayString));
				AutopilotTerminal.SetAutopilotCommands(m_block, new StringBuilder(Commands));
				m_actionList.Clear();
				GetActions(Commands, m_actionList);
			}

			MyTerminalControls.Static.CustomControlGetter -= CustomControlGetter;
			m_block.AppendingCustomInfo -= m_block_AppendingCustomInfo;

			m_block.UpdateCustomInfo();
			m_block.RebuildControls();

			Cleanup();
		}

		#endregion Button Action

		private void LogAndInfo(string message, [CallerMemberName] string member = null, [CallerLineNumber] int lineNumber = 0)
		{
			Log.DebugLog(message, member: member, lineNumber: lineNumber);
			m_infoMessage = message;
			m_block.UpdateCustomInfo();
			m_block.RebuildControls();
		}

		private void ClearMessage()
		{
			m_infoMessage = null;
			m_block.UpdateCustomInfo();
			m_block.RebuildControls();
		}

		private void m_block_AppendingCustomInfo(IMyTerminalBlock arg1, StringBuilder arg2)
		{
			Log.DebugLog("entered");

			if (!string.IsNullOrWhiteSpace(m_infoMessage))
			{
				Log.DebugLog("appending info message: " + m_infoMessage);
				arg2.AppendLine();
				arg2.AppendLine(m_infoMessage);
			}

			if (!m_listCommands && m_currentCommand != null)
			{
				Log.DebugLog("appending command info");
				arg2.AppendLine();
				m_currentCommand.AppendCustomInfo(arg2);
				arg2.AppendLine();
			}
		}

		private void m_block_AppendSyntaxErrors(IMyTerminalBlock arg1, StringBuilder arg2)
		{
			if (m_syntaxErrors.Length != 0)
			{
				Log.DebugLog("appending syntax errors");
				arg2.AppendLine();
				arg2.Append("Syntax Errors:\n");
				arg2.Append(m_syntaxErrors);
			}
		}

		private void Cleanup()
		{
			m_commandList.Clear();

			m_infoMessage = null;
			m_termCommandList = null;
			m_currentCommand = null;
		}

	}
}
