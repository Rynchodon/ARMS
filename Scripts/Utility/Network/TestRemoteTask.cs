#if DEBUG

using VRage.Collections;

namespace Rynchodon.Utility.Network
{
	class TestRemoteTask : RemoteTask
	{

		private static CachingHashSet<TestRemoteTask> m_tasks = new CachingHashSet<TestRemoteTask>();

		[OnWorldLoad]
		private static void Load()
		{
			Update.UpdateManager.Register(10, Update10);
		}

		private static void Update10()
		{
			foreach (TestRemoteTask task in m_tasks)
				switch (task.CurrentStatus)
				{
					case Status.None:
					case Status.Started:
						break;
					default:
						Logger.DebugLog("Completed: " + task + ", Result: "+ task.Result);
						m_tasks.Remove(task);
						break;
				}

			m_tasks.ApplyRemovals();

			if (m_tasks.Count < 10)
			{
				TestRemoteTask task = new TestRemoteTask();
				task.Parameter = "Par";
				m_tasks.Add(task);
				StartTask(task);
			}

			m_tasks.ApplyAdditions();
		}

#pragma warning disable 414
		[Argument]
		private string Parameter;
		[Result]
		private string Result;
#pragma warning restore 414

		protected override void Start()
		{
			Logger.DebugLog("entered, Parameter: " + Parameter);
			Result = "Res";
			Completed(Status.Success);
		}

	}
}

#endif