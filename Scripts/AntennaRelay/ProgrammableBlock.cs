using System;
using System.Text;
using Rynchodon.Autopilot;
using Rynchodon.Instructions;
using Rynchodon.Utility.Network;
using Sandbox.ModAPI;
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

		private static readonly Logger s_logger = new Logger("ProgrammableBlock");

		static ProgrammableBlock()
		{
			MessageHandler.Handlers.Add(MessageHandler.SubMod.PB_SendMessage_Client, Handler_SendMessage);
			MessageHandler.Handlers.Add(MessageHandler.SubMod.PB_SendMessage_Server, Handler_SendMessage);
		}

		private static void Handler_SendMessage(byte[] message, int pos)
		{
			bool clientSent = (MessageHandler.SubMod)message[pos - 1] == MessageHandler.SubMod.PB_SendMessage_Client;

			long programId = ByteConverter.GetLong(message, ref pos);

			ProgrammableBlock program;
			if (!Registrar.TryGetValue(programId, out program))
			{
				s_logger.alwaysLog("Programmble block not found in registrar: " + programId, "Handler_SendMessage()", Logger.severity.ERROR);
				return;
			}

			s_logger.debugLog("Found programmable block with id: " + programId, "Handler_SendMessage()");

			string[] text = ByteConverter.GetString(message, ref pos).Split(messageSeparator);

			if (text.Length != 3)
			{
				s_logger.alwaysLog("text has wrong length, expected 3, got " + text.Length, "Handler_SendMessage()", Logger.severity.ERROR);
				return;
			}

			bool sendToServer;
			program.SendMessage(text[0], text[1], text[2], clientSent, out sendToServer);
			if (sendToServer)
				MyAPIGateway.Multiplayer.SendMessageToServer(message);
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
			//	m_logger.debugLog("running program, parameter:\n" + parameter.ToString(), "HandleDetected()");
			//else
				m_logger.alwaysLog("Failed to run program", "HandleDetected()", Logger.severity.WARNING);
		}

		private void SendMessage(string recipientGrid, string recipientBlock, string message, bool clientSent, out bool sendToServer)
		{
			bool sentToSelf = !clientSent || !MyAPIGateway.Multiplayer.IsServer;
			sendToServer = false;
			m_logger.debugLog("client sent: " + clientSent + ", is server: " + MyAPIGateway.Multiplayer.IsServer + ", sentToSelf: " + sentToSelf, "SendMessage()");

			NetworkStorage store = m_networkClient.GetStorage();
			if (store == null)
			{
				m_logger.debugLog("not connected to a network", "SendMessage()", Logger.severity.DEBUG);
				if (sentToSelf && MyAPIGateway.Session.Player != null)
					(m_block as IMyTerminalBlock).AppendCustomInfo("Failed to send message:\nNot connected to a network");
				return;
			}

			byte count = 0;

			if (sentToSelf)
				Registrar.ForEach((ProgrammableBlock program) => {
					IMyCubeBlock block = program.m_block;
					IMyCubeGrid grid = block.CubeGrid;
					if (m_block.canControlBlock(block) && grid.DisplayName.looseContains(recipientGrid) && block.DisplayNameText.looseContains(recipientBlock))
					{
						m_logger.debugLog("sending message to " + block.gridBlockName() + ", content: " + message, "SendMessage()", Logger.severity.DEBUG);
						store.Receive(new Message(message, block, m_block));
						count++;
					}
				});

			bool sts = false;

			Registrar.ForEach((ShipController_Autopilot autopilot) => {
				IMyCubeBlock block = autopilot.m_block.CubeBlock;
				IMyCubeGrid grid = block.CubeGrid;
				if (m_block.canControlBlock(block) && grid.DisplayName.looseContains(recipientGrid) && block.DisplayNameText.looseContains(recipientBlock))
				{
					if (MyAPIGateway.Multiplayer.IsServer)
					{
						m_logger.debugLog("sending message to " + block.gridBlockName() + ", content: " + message, "SendMessage()", Logger.severity.DEBUG);
						store.Receive(new Message(message, block, m_block));
					}
					else
						sts = true;
					count++;
				}
			});

			sendToServer = sts;

			if (sentToSelf && MyAPIGateway.Session.Player != null)
				(m_block as IMyTerminalBlock).AppendCustomInfo("Sent message to " + count + " block" + (count == 1 ? "" : "s"));
		}

		private void HandleMessage(Message received)
		{
			string param = received.SourceGridName + messageSeparator + received.SourceBlockName + messageSeparator + received.Content;

			if (m_progBlock.TryRun(param))
			{
				m_logger.debugLog("Sent message to program", "HandleMessage()", Logger.severity.DEBUG);
				if (MyAPIGateway.Session.Player != null)
					(m_block as IMyTerminalBlock).AppendCustomInfo("Received message");
			}
			else
			{
				m_logger.debugLog("Failed to send message to program", "HandleMessage()", Logger.severity.WARNING);
				if (MyAPIGateway.Session.Player != null)
					(m_block as IMyTerminalBlock).AppendCustomInfo("Received message but failed to run program.");
			}
		}

	}
}
