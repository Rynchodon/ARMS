using System;
using System.Text;
using Rynchodon.Instructions;
using Sandbox.Game.Entities.Blocks;
using Sandbox.Game.Entities.Cube;
using Sandbox.Game.Gui;
using Sandbox.ModAPI;
using VRage.Collections;
using VRage.Game.ModAPI;
using VRageMath;
using Ingame = Sandbox.ModAPI.Ingame;

namespace Rynchodon.AntennaRelay
{
	public class ProgrammableBlock : BlockInstructions
	{

		public const char fieldSeparator = '«', entitySeparator = '»';
		public const char messageSeparator = '«';
		public const string numberFormat = "e2";

		private static Logger s_logger = new Logger("ProgrammableBlock");

		static ProgrammableBlock()
		{
			MyTerminalAction<MyProgrammableBlock> programmable_sendMessage = new MyTerminalAction<MyProgrammableBlock>("SendMessage", new StringBuilder("Send Message"), "Textures\\GUI\\Icons\\Actions\\Start.dds")
			{
				ValidForGroups = false,
				ActionWithParameters = ProgrammableBlock_SendMessage
			};
			MyTerminalControlFactory.AddAction(programmable_sendMessage);

			MyAPIGateway.Entities.OnCloseAll += Entities_OnCloseAll;
		}

		private static void Entities_OnCloseAll()
		{
			MyAPIGateway.Entities.OnCloseAll -= Entities_OnCloseAll;
			s_logger = null;
		}

		/// <param name="args">Recipient grid, recipient block, message</param>
		private static void ProgrammableBlock_SendMessage(MyFunctionalBlock block, ListReader<Ingame.TerminalActionParameter> args)
		{
			if (args.Count != 3)
			{
				s_logger.debugLog("Wrong number of arguments, expected 3, got " + args.Count, Logger.severity.WARNING);
				if (MyAPIGateway.Session.Player != null)
					block.AppendCustomInfo("Failed to send message:\nWrong number of arguments, expected 3, got " + args.Count + '\n');
				return;
			}

			string[] stringArgs = new string[3];
			for (int i = 0; i < 3; i++)
			{
				if (args[i].TypeCode != TypeCode.String)
				{
					s_logger.debugLog("TerminalActionParameter #" + i + " is of wrong type, expected String, got " + args[i].TypeCode, Logger.severity.WARNING);
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

		private Ingame.IMyProgrammableBlock m_progBlock;
		private NetworkClient m_networkClient;
		private Logger m_logger;

		private bool m_handleDetected;

		public ProgrammableBlock(IMyCubeBlock block)
			: base(block)
		{
			m_logger = new Logger(GetType().Name, block);
			m_progBlock = block as Ingame.IMyProgrammableBlock;
			m_networkClient = new NetworkClient(block, HandleMessage);

			Registrar.Add(block, this);
		}

		public void Update100()
		{
			m_networkClient.GetStorage(); // force update so messages do not get stuck

			UpdateInstructions();

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

			NetworkStorage store = m_networkClient.GetStorage();
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
				parameter.Append(predictedPosition.X.ToString(numberFormat)); parameter.Append(fieldSeparator);
				parameter.Append(predictedPosition.Y.ToString(numberFormat)); parameter.Append(fieldSeparator);
				parameter.Append(predictedPosition.Z.ToString(numberFormat)); parameter.Append(fieldSeparator);
				parameter.Append(seen.LastKnownVelocity.X.ToString(numberFormat)); parameter.Append(fieldSeparator);
				parameter.Append(seen.LastKnownVelocity.Y.ToString(numberFormat)); parameter.Append(fieldSeparator);
				parameter.Append(seen.LastKnownVelocity.Z.ToString(numberFormat)); parameter.Append(fieldSeparator);

				if (seen.Info != null)
					parameter.Append(seen.Info.Volume);
				parameter.Append(fieldSeparator);
			});

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
