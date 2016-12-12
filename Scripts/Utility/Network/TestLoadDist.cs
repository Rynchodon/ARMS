using System.Collections.Generic;
using static Rynchodon.Utility.Network.LoadDistribution;

namespace Rynchodon.Utility.Network
{
	static class TestLoadDist
	{

		private class TestDefn : TaskDefinition
		{
			public override bool Executor(object parameter)
			{
				Logger.DebugLog("parameter: " + parameter);
				return true;
			}

			public override void ParamSerializer(List<byte> startMessage)
			{
				ByteConverter.AppendBytes(startMessage, "Param");
			}

			public override object ParamDeserializer(byte[] startMessage, ref int pos)
			{
				string s = ByteConverter.GetString(startMessage, ref pos);
				Logger.DebugLog("String: " + s);
				return s;
			}

			public override void ResultSerializer(List<byte> resultMessage)
			{
				ByteConverter.AppendBytes(resultMessage, "Result");
			}

			public override object ResultDeserializer(byte[] resultMessage, ref int pos)
			{
				string s = ByteConverter.GetString(resultMessage, ref pos);
				Logger.DebugLog("String: " + s);
				return s;
			}
		}

		private static HashSet<ATask> m_tasks = new HashSet<ATask>();

		[OnWorldLoad]
		private static void Load()
		{
			Update.UpdateManager.Register(100, Update100);
		}

		private static void Update100()
		{
			foreach (ATask task in m_tasks)
				if (task.Completed)
				{
					Logger.DebugLog("Completed: " + task);
					m_tasks.Remove(task);
					break;
				}

			if (m_tasks.Count < 10)
				m_tasks.Add(StartTask<TestDefn>());
		}

	}
}
