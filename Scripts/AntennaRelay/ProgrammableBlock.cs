using System;
using System.Text;
using Rynchodon.Instructions;
using Sandbox.ModAPI;
using VRageMath;
using Ingame = Sandbox.ModAPI.Ingame;

namespace Rynchodon.AntennaRelay
{
	public class ProgrammableBlock : BlockInstructions
	{

		private const char fieldSeparator = ',', entitySeparator = ';';
		private const string numberFormat = "e2";

		private Ingame.IMyProgrammableBlock myProgBlock;
		private NetworkClient m_networkClient;
		private Logger myLogger;

		private bool m_handleDetected;

		public ProgrammableBlock(IMyCubeBlock block)
			: base(block)
		{
			myLogger = new Logger(GetType().Name, block);
			myProgBlock = block as Ingame.IMyProgrammableBlock;
			m_networkClient = new NetworkClient(block);
		}

		public void Update100()
		{
			UpdateInstructions();

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
			if (myProgBlock.IsRunning)
				return;

			StringBuilder parameter = new StringBuilder();
			bool first = true;

			NetworkStorage store = m_networkClient.Storage;
			if (store == null)
				return;

			store.ForEachLastSeen((LastSeen seen)=>{
				ExtensionsRelations.Relations relations =( myProgBlock as IMyCubeBlock).getRelationsTo(seen.Entity, ExtensionsRelations.Relations.Enemy).highestPriority();
				bool friendly = ExtensionsRelations.toIsFriendly(relations);
				string bestName  = friendly ? seen.Entity.getBestName() : "Unknown";
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

			if (myProgBlock.TryRun(parameter.ToString()))
				myLogger.debugLog("running program, parameter:\n" + parameter.ToString(), "HandleDetected()");
			else
				myLogger.alwaysLog("Failed to run program", "HandleDetected()", Logger.severity.WARNING);
		}

	}
}
