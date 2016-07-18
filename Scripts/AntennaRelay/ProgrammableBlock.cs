using System;
using System.Text;
using System.Xml.Serialization;
using Rynchodon.Instructions;
using Rynchodon.Utility.Network;
using Sandbox.Game.Entities.Blocks;
using Sandbox.Game.Entities.Cube;
using Sandbox.Game.Gui;
using Sandbox.ModAPI;
using Sandbox.ModAPI.Interfaces;
using Sandbox.ModAPI.Interfaces.Terminal;
using VRage.Collections;
using VRage.Game.ModAPI;
using VRage.Utils;
using VRageMath;
using Ingame = Sandbox.ModAPI.Ingame;

namespace Rynchodon.AntennaRelay
{

	public class ProgrammableBlock : BlockInstructions
	{

		[Serializable]
		public class Builder_ProgrammableBlock
		{
			[XmlAttribute]
			public long BlockId;
			public bool HandleDetected;
			public string BlockCountList;
		}

		public const char fieldSeparator = '«', entitySeparator = '»';
		public const char messageSeparator = '«';
		public const string numberFormat = "e2";

		private class StaticVariables
		{
			public Logger s_logger = new Logger("ProgrammableBlock");
			public MyTerminalControlOnOffSwitch<MyProgrammableBlock> handleDetected;
			public MyTerminalControlTextbox<MyProgrammableBlock> blockCountList;
		}

		private static StaticVariables Static = new StaticVariables();

		static ProgrammableBlock()
		{
			MyTerminalAction<MyProgrammableBlock> programmable_sendMessage = new MyTerminalAction<MyProgrammableBlock>("SendMessage", new StringBuilder("Send Message"), "Textures\\GUI\\Icons\\Actions\\Start.dds")
			{
				ValidForGroups = false,
				ActionWithParameters = ProgrammableBlock_SendMessage
			};
			programmable_sendMessage.ParameterDefinitions.Add(Ingame.TerminalActionParameter.Get(string.Empty));
			programmable_sendMessage.ParameterDefinitions.Add(Ingame.TerminalActionParameter.Get(string.Empty));
			programmable_sendMessage.ParameterDefinitions.Add(Ingame.TerminalActionParameter.Get(string.Empty));
			MyTerminalControlFactory.AddAction(programmable_sendMessage);

			MyTerminalControlFactory.AddControl(new MyTerminalControlSeparator<MyProgrammableBlock>());

			Static.handleDetected = new MyTerminalControlOnOffSwitch<MyProgrammableBlock>("HandleDetected", MyStringId.GetOrCompute("Handle Detected"));
			IMyTerminalValueControl<bool> valueControl = Static.handleDetected as IMyTerminalValueControl<bool>;
			valueControl.Getter = GetHandleDetectedTerminal;
			valueControl.Setter = SetHandleDetectedTerminal;
			MyTerminalControlFactory.AddControl(Static.handleDetected);

			Static.blockCountList = new MyTerminalControlTextbox<MyProgrammableBlock>("BlockCounts", MyStringId.GetOrCompute("Blocks to Count"), MyStringId.GetOrCompute("Comma separate list of blocks to count"));
			Static.blockCountList.Visible = block => ((ITerminalProperty<bool>)((IMyTerminalBlock)block).GetProperty("HandleDetected")).GetValue(block);
			IMyTerminalControlTextbox asInterface = Static.blockCountList as IMyTerminalControlTextbox;
			asInterface.Getter = GetBlockCountList;
			asInterface.Setter = SetBlockCountList;
			MyTerminalControlFactory.AddControl(Static.blockCountList);

			MyAPIGateway.Entities.OnCloseAll += Entities_OnCloseAll;
		}

		private static void Entities_OnCloseAll()
		{
			MyAPIGateway.Entities.OnCloseAll -= Entities_OnCloseAll;
			Static = null;
		}

		/// <param name="args">Recipient grid, recipient block, message</param>
		private static void ProgrammableBlock_SendMessage(MyFunctionalBlock block, ListReader<Ingame.TerminalActionParameter> args)
		{
			if (args.Count != 3)
			{
				Static.s_logger.debugLog("Wrong number of arguments, expected 3, got " + args.Count, Logger.severity.WARNING);
				if (MyAPIGateway.Session.Player != null)
					block.AppendCustomInfo("Failed to send message:\nWrong number of arguments, expected 3, got " + args.Count + '\n');
				return;
			}

			string[] stringArgs = new string[3];
			for (int i = 0; i < 3; i++)
			{
				if (args[i].TypeCode != TypeCode.String)
				{
					Static.s_logger.debugLog("TerminalActionParameter #" + i + " is of wrong type, expected String, got " + args[i].TypeCode, Logger.severity.WARNING);
					if (MyAPIGateway.Session.Player != null)
						block.AppendCustomInfo("Failed to send message:\nTerminalActionParameter #" + i + " is of wrong type, expected String, got " + args[i].TypeCode + '\n');
					return;
				}

				stringArgs[i] = (string)args[i].Value;
			}

			int count = Message.CreateAndSendMessage(block.EntityId, stringArgs[0], stringArgs[1], stringArgs[2]);
			if (MyAPIGateway.Session.Player != null)
				(block as IMyTerminalBlock).AppendCustomInfo("Sent message to " + count + " block" + (count == 1 ? "" : "s"));
		}

		private static bool GetHandleDetectedTerminal(IMyTerminalBlock block)
		{
			ProgrammableBlock pb;
			if (!Registrar.TryGetValue(block, out pb))
			{
				if (Static.s_logger == null)
					return false;
				throw new ArgumentException("block id not found in registrar");
			}

			return pb.m_handleDetectedTerminal;
		}

		private static void SetHandleDetectedTerminal(IMyTerminalBlock block, bool value)
		{
			ProgrammableBlock pb;
			if (!Registrar.TryGetValue(block, out pb))
			{
				if (Static.s_logger == null)
					return;
				throw new ArgumentException("block id not found in registrar");
			}

			pb.m_handleDetectedTerminal = value;
			block.SwitchTerminalTo();
		}

		private static StringBuilder GetBlockCountList(IMyTerminalBlock block)
		{
			ProgrammableBlock pb;
			if (!Registrar.TryGetValue(block, out pb))
			{
				if (Static.s_logger == null)
					return new StringBuilder();
				throw new ArgumentException("block id not found in registrar");
			}

			return pb.m_blockCountList_sb;
		}

		private static void SetBlockCountList(IMyTerminalBlock block, StringBuilder value)
		{
			ProgrammableBlock pb;
			if (!Registrar.TryGetValue(block, out pb))
			{
				if (Static.s_logger == null)
					return;
				throw new ArgumentException("block id not found in registrar");
			}

			pb.m_blockCountList_sb = value;
		}

		private static void UpdateVisual()
		{
			Static.handleDetected.UpdateVisual();
			Static.blockCountList.UpdateVisual();
		}

		private readonly Ingame.IMyProgrammableBlock m_progBlock;
		private readonly RelayClient m_networkClient;
		private readonly Logger m_logger;

		private readonly EntityValue<bool> m_handleDetectedTerminal_ev;
		private readonly EntityStringBuilder m_blockCountList_ev;
	
		private bool m_handleDetected;

		private bool m_handleDetectedTerminal
		{
			get { return m_handleDetectedTerminal_ev.Value; }
			set { m_handleDetectedTerminal_ev.Value = value; }
		}

		private StringBuilder m_blockCountList_sb
		{
			get { return m_blockCountList_ev.Value; }
			set { m_blockCountList_ev.Value = value; }
		}

		private BlockTypeList m_blockCountList_btl;

		public ProgrammableBlock(IMyCubeBlock block)
			: base(block)
		{
			m_logger = new Logger(GetType().Name, block);
			m_progBlock = block as Ingame.IMyProgrammableBlock;
			m_networkClient = new RelayClient(block, HandleMessage);

			byte index = 0;
			m_handleDetectedTerminal_ev = new EntityValue<bool>(block, index++, UpdateVisual);
			m_blockCountList_ev = new EntityStringBuilder(block, index++, () => {
				UpdateVisual();
				m_blockCountList_btl = new BlockTypeList(m_blockCountList_sb.ToString().LowerRemoveWhitespace().Split(','));
			});

			Registrar.Add(block, this);
		}

		public void ResumeFromSave(Builder_ProgrammableBlock builder)
		{
			if (this.m_block.EntityId != builder.BlockId)
				throw new ArgumentException("Serialized block id " + builder.BlockId + " does not match block id " + this.m_block.EntityId);

			this.m_handleDetectedTerminal = builder.HandleDetected;
			this.m_blockCountList_sb = new StringBuilder(builder.BlockCountList);
		}

		public Builder_ProgrammableBlock GetBuilder()
		{
			// do not save if default
			if (!this.m_handleDetectedTerminal && this.m_blockCountList_sb.Length == 0)
				return null;

			return new Builder_ProgrammableBlock()
			{
				BlockId = this.m_block.EntityId,
				HandleDetected = this.m_handleDetectedTerminal,
				BlockCountList = this.m_blockCountList_sb.ToString()
			};
		}

		public void Update100()
		{
			m_networkClient.GetStorage(); // force update so messages do not get stuck

			UpdateInstructions();

			if (m_handleDetectedTerminal)
			{
				HandleDetected();
				return;
			}

			if (!HasInstructions)
				return;

			if (m_handleDetected)
				HandleDetected();
		}

		protected override bool ParseAll(string instructions)
		{
			m_handleDetected = instructions.looseContains("Handle Detected");
			return m_handleDetected;
		}

		/// <summary>
		/// Creates the parameter for the block and runs the program.
		/// </summary>
		private void HandleDetected()
		{
			if (m_progBlock.IsRunning)
				return;

			StringBuilder parameter = new StringBuilder();
			bool first = true;

			RelayStorage store = m_networkClient.GetStorage();
			if (store == null)
				return;

			store.ForEachLastSeen((LastSeen seen) => {
				ExtensionsRelations.Relations relations = (m_progBlock as IMyCubeBlock).getRelationsTo(seen.Entity, ExtensionsRelations.Relations.Enemy).highestPriority();
				bool friendly = ExtensionsRelations.toIsFriendly(relations);
				string bestName = friendly ? seen.Entity.getBestName() : seen.HostileName();
				TimeSpan sinceSeen;
				Vector3D predictedPosition = seen.predictPosition(out sinceSeen);

				if (first)
					first = false;
				else
					parameter.Append(entitySeparator);

				parameter.Append(seen.Entity.EntityId); parameter.Append(fieldSeparator);
				parameter.Append((byte)relations); parameter.Append(fieldSeparator);
				parameter.Append((byte)seen.Type); parameter.Append(fieldSeparator);
				parameter.Append(bestName); parameter.Append(fieldSeparator);
				parameter.Append(seen.isRecent_Radar()); parameter.Append(fieldSeparator);
				parameter.Append(seen.isRecent_Jam()); parameter.Append(fieldSeparator);
				parameter.Append((int)sinceSeen.TotalSeconds); parameter.Append(fieldSeparator);
				parameter.Append(Math.Round(predictedPosition.X, 1)); parameter.Append(fieldSeparator);
				parameter.Append(Math.Round(predictedPosition.Y, 1)); parameter.Append(fieldSeparator);
				parameter.Append(Math.Round(predictedPosition.Z, 1)); parameter.Append(fieldSeparator);
				parameter.Append(Math.Round(seen.LastKnownVelocity.X, 1)); parameter.Append(fieldSeparator);
				parameter.Append(Math.Round(seen.LastKnownVelocity.Y, 1)); parameter.Append(fieldSeparator);
				parameter.Append(Math.Round(seen.LastKnownVelocity.Z, 1)); parameter.Append(fieldSeparator);

				if (seen.Info != null)
					parameter.Append(seen.Info.Volume);
				else
					parameter.Append(0f);

				if (!friendly && seen.Type == LastSeen.EntityType.Grid && m_blockCountList_sb.Length > 2 && seen.isRecent() && m_blockCountList_btl != null)
				{
					int[] blockCounts = m_blockCountList_btl.Count(CubeGridCache.GetFor((IMyCubeGrid)seen.Entity));
					if (blockCounts.Length != 0)
					{
						parameter.Append(fieldSeparator);
						parameter.Append(string.Join(fieldSeparator.ToString(), blockCounts));
					}
				}
			});

			if (parameter.Length == 0)
			{
				m_logger.debugLog("no detected entities");
				return;
			}

			//m_logger.debugLog("parameters:\n" + parameter.ToString().Replace(string.Empty + entitySeparator, entitySeparator + "\n"));
			if (!m_progBlock.TryRun(parameter.ToString()))
				m_logger.alwaysLog("Failed to run program", Logger.severity.WARNING);
		}

		private void HandleMessage(Message received)
		{
			string param = received.SourceGridName + messageSeparator + received.SourceBlockName + messageSeparator + received.Content;

			if (m_progBlock.TryRun(param))
			{
				m_logger.debugLog("Sent message to program", Logger.severity.DEBUG);
				if (MyAPIGateway.Session.Player != null)
					(m_block as IMyTerminalBlock).AppendCustomInfo("Received message");
			}
			else
			{
				m_logger.debugLog("Failed to send message to program", Logger.severity.WARNING);
				if (MyAPIGateway.Session.Player != null)
					(m_block as IMyTerminalBlock).AppendCustomInfo("Received message but failed to run program.");
			}
		}

	}
}
