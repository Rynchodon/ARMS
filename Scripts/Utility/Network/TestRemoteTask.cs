#if DEBUG

using System.Collections.Generic;
using VRage.Collections;
using static Rynchodon.Utility.Network.RemoteTask;

namespace Rynchodon.Utility.Network
{
	static class TestRemoteTask
	{

		private class TestTask : RemoteTask
		{
			private string Parameter, Result;

			protected override void Start()
			{
				Logger.DebugLog("entered");
				Result = "Res";
				Completed(Status.Success);
			}

			protected override void ParamSerialize(List<byte> startMessage)
			{
				Logger.DebugLog("entered");
				ByteConverter.AppendBytes(startMessage, "Param");
			}

			protected override void ParamDeserialize(byte[] startMessage, int pos)
			{
				Logger.DebugLog("entered");
				Parameter = ByteConverter.GetString(startMessage, ref pos);
				Logger.DebugLog("Parameter: " + Parameter);
			}

			protected override void ResultSerialize(List<byte> resultMessage)
			{
				Logger.DebugLog("entered");
				ByteConverter.AppendBytes(resultMessage, Result);
			}

			protected override void ResultDeserialize(byte[] resultMessage, int pos)
			{
				Logger.DebugLog("entered");
				Result = ByteConverter.GetString(resultMessage, ref pos);
				Logger.DebugLog("Result: " + Result);
			}
		}

		private static CachingHashSet<TestTask> m_tasks = new CachingHashSet<TestTask>();

		[OnWorldLoad]
		private static void Load()
		{
			Update.UpdateManager.Register(10, Update10);
		}

		private static void Update10()
		{
			foreach (TestTask task in m_tasks)
				switch (task.CurrentStatus)
				{
					case Status.None:
					case Status.Started:
						break;
					default:
						Logger.DebugLog("Completed: " + task);
						m_tasks.Remove(task);
						break;
				}

			m_tasks.ApplyRemovals();

			if (m_tasks.Count < 10)
			{
				TestTask task = new TestTask();
				m_tasks.Add(task);
				StartTask(task);
			}

			m_tasks.ApplyAdditions();
		}

	}
}

#endif