#if DEBUG
#define TRACE
#endif

using System;
using System.Collections.Generic;
using System.Reflection;
using Rynchodon.Update;
using Sandbox.Game.World;
using Sandbox.ModAPI;

namespace Rynchodon.Utility.Network
{
	/// <summary>
	/// "Let's put these maggots to work."
	/// Allows the server to request that a client executes some task.
	/// </summary>
	/// TODO: sim speed check?
	abstract class RemoteTask
	{

		/// <summary>Identifies a field or property of a RemoteTask as an argument, it will be sent to the client.</summary>
		internal class ArgumentAttribute : Attribute { }
		/// <summary>Identifies a field or property of a RemoteTask as a result, it will be sent to the server.</summary>
		internal class ResultAttribute : Attribute { }

		private const ulong ServerKey = 0uL;
		private static readonly TimeSpan TimeoutAfter = new TimeSpan(0, 1, 0);
		private static readonly TimeSpan KeepAliveInterval = new TimeSpan(0, 0, 15);

		public enum Status : byte { None, Started, Success, Exception, Disconnect, Timeout }

		private class Client
		{
			public int Controllers;
			public int OutstandingTasks;

			public override string ToString()
			{
				return "Controllers: " + Controllers + ", OutstandingTasks: " + OutstandingTasks;
			}
		}

		/// <summary>IDs assigned to types of Tasks.</summary>
		private static Dictionary<byte, Type> m_taskTypes = new Dictionary<byte, Type>();
		/// <summary>Client indexed by steam id.</summary>
		private static Dictionary<ulong, Client> m_clients;
		private static int m_nextOutstandingTaskId;
		private static Dictionary<int, RemoteTask> m_outstandingTask;
		private static List<byte> m_message = new List<byte>();

		static RemoteTask()
		{
			MessageHandler.AddHandler(MessageHandler.SubMod.LoadDistribution, HandleMessage);
			byte index = 0;
			foreach (TypeInfo type in Assembly.GetExecutingAssembly().DefinedTypes)
				if (type.IsSubclassOf(typeof(RemoteTask)))
					m_taskTypes.Add(index++, type);
		}

		#region Load

		[OnWorldLoad]
		private static void Load()
		{
			if (!MyAPIGateway.Multiplayer.IsServer)
				return;

			MySession.Static.Players.PlayersChanged += OnPlayersChanged;
			m_clients = new Dictionary<ulong, Client>();
			m_clients.Add(ServerKey, new Client() { OutstandingTasks = int.MaxValue / 2 }); // for the server
			foreach (MyPlayer player in MySession.Static.Players.GetOnlinePlayers())
				OnPlayersChanged(true, player.Id);

			m_outstandingTask = new Dictionary<int, RemoteTask>();
		}

		[OnWorldClose]
		private static void Unload()
		{
			if (!MyAPIGateway.Multiplayer.IsServer)
				return;

			MySession.Static.Players.PlayersChanged -= OnPlayersChanged;
			m_clients = null;
			m_outstandingTask = null;
		}

		private static void OnPlayersChanged(bool added, MyPlayer.PlayerId playerId)
		{
			if (playerId.SteamId == MyAPIGateway.Multiplayer.MyId)
				return;

			if (added)
			{
				Client c;
				if (!m_clients.TryGetValue(playerId.SteamId, out c))
				{
					c = new Client();
					m_clients[playerId.SteamId] = c;
				}
				++c.Controllers;

				Logger.TraceLog("New client connected: " + playerId + ", client: " + c);
			}
			else
			{
				if (Globals.WorldClosed)
					return;

				Client c = m_clients[playerId.SteamId];
				--c.Controllers;
				if (c.Controllers == 0)
					m_clients.Remove(playerId.SteamId);

				Logger.TraceLog("Client disconnected: " + playerId + ", client: " + c);
			}
		}

		#endregion

		#region Message

		/*
		 * Start message format: Sub Mod ID, TaskId, Task Type ID[, param]
		 * Status message format: Sub Mod ID, TaskId, Status
		 * Success message format: Sub Mod ID, TaskId, Status.Success[, result]
		 */

		/// <summary>
		/// Start a task on a client if possible, otherwise it will run on the server.
		/// </summary>
		/// <typeparam name="T">The type of task to start.</typeparam>
		/// <param name="task">The task to start.</param>
		public static void StartTask<T>(T task, bool runOnServer = false) where T : RemoteTask, new()
		{
			if (!MyAPIGateway.Multiplayer.IsServer)
				throw new Exception("Not server");
			if (m_outstandingTask.Count >= 100)
			{
#if DEBUG
				foreach (KeyValuePair<int, RemoteTask> pair in m_outstandingTask)
					Logger.DebugLog("Task ID: " + pair.Key + ", Task: " + pair.Value);
#endif
				throw new Exception("Too many outstanding tasks");
			}

			Type taskType = task.GetType();
			foreach (KeyValuePair<byte, Type> pair in m_taskTypes)
				if (pair.Value == taskType)
				{
					task.SteamId = runOnServer ? GetServerClient() : GetIdlemostClient();

					task.TaskId = m_nextOutstandingTaskId++;
					while (m_outstandingTask.ContainsKey(task.TaskId))
						task.TaskId = m_nextOutstandingTaskId++;

					m_message.Clear();
					ByteConverter.AppendBytes(m_message, MessageHandler.SubMod.LoadDistribution);
					ByteConverter.AppendBytes(m_message, task.TaskId);
					ByteConverter.AppendBytes(m_message, pair.Key);

					foreach (FieldInfo field in taskType.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
						if (field.HasAttribute<ArgumentAttribute>())
							ByteConverter.AppendBytes(m_message, field.GetValue(task));
					foreach (PropertyInfo property in taskType.GetProperties(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
						if (property.HasAttribute<ArgumentAttribute>())
							ByteConverter.AppendBytes(m_message, property.GetValue(task));

					Logger.TraceLog("Sending task request: " + typeof(T) + ", " + pair.Key + ", " + task.TaskId + ", task: " + task + ", m_outstandingTask.Count: " + m_outstandingTask.Count);

					m_outstandingTask.Add(task.TaskId, task);
					task.StartMonitorTask();

					if (task.SteamId == ServerKey || task.SteamId == MyAPIGateway.Multiplayer.MyId)
						HandleStartRequest(m_message.ToArray(), 1);
					else
						MyAPIGateway.Multiplayer.SendMessageTo(m_message.ToArray(), task.SteamId);

					return;
				}

			throw new Exception("No task found of type: " + typeof(T));
		}

		private static void HandleMessage(byte[] message, int position)
		{
			if (MyAPIGateway.Multiplayer.IsServer)
				HandleStatus(message, position);
			else
				HandleStartRequest(message, position);
		}

		/// <summary>
		/// Handle a task request from the server.
		/// </summary>
		private static void HandleStartRequest(byte[] startMessage, int position)
		{
			int taskId = ByteConverter.GetInt(startMessage, ref position);
			byte taskTypeId = ByteConverter.GetByte(startMessage, ref position);

			Type taskType;
			if (!m_taskTypes.TryGetValue(taskTypeId, out taskType))
				throw new Exception("Failed to get TaskDefinition for " + taskTypeId);

			Logger.TraceLog("Got task request: " + taskType + ", " + taskTypeId + ", " + taskId);

			RemoteTask task = (RemoteTask)Activator.CreateInstance(taskType);
			task.TaskId = taskId;

			foreach (FieldInfo field in taskType.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
				if (field.HasAttribute<ArgumentAttribute>())
					field.SetValue(task, ByteConverter.GetOfType(startMessage, ref position, field.FieldType));
			foreach (PropertyInfo property in taskType.GetProperties(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
				if (property.HasAttribute<ArgumentAttribute>())
					property.SetValue(task, ByteConverter.GetOfType(startMessage, ref position, property.PropertyType));

			Logger.TraceLog("Created Task with ID: " + task.TaskId + ", Task: " + task);

			try
			{
				task.CurrentStatus = Status.Started;
				task.StartTask();
			}
			catch (Exception ex)
			{
				task.CurrentStatus = Status.Exception;
				Logger.AlwaysLog("Exception: " + ex, Logger.severity.ERROR);
			}
		}

		private static void SendStatus(RemoteTask task)
		{
			m_message.Clear();
			ByteConverter.AppendBytes(m_message, MessageHandler.SubMod.LoadDistribution);
			ByteConverter.AppendBytes(m_message, task.TaskId);
			ByteConverter.AppendBytes(m_message, task.CurrentStatus);
			if (task.CurrentStatus == Status.Success)
			{
				Type taskType = task.GetType();
				foreach (FieldInfo field in taskType.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
					if (field.HasAttribute<ResultAttribute>())
						ByteConverter.AppendBytes(m_message, field.GetValue(task));
				foreach (PropertyInfo property in taskType.GetProperties(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
					if (property.HasAttribute<ResultAttribute>())
						ByteConverter.AppendBytes(m_message, property.GetValue(task));
			}

			Logger.TraceLog("Sending status for " + task);

			if (MyAPIGateway.Multiplayer.IsServer)
				HandleStatus(m_message.ToArray(), 1);
			else
				MyAPIGateway.Multiplayer.SendMessageToServer(m_message.ToArray());
		}

		private static void HandleStatus(byte[] resultMessage, int position)
		{
			int taskId = ByteConverter.GetInt(resultMessage, ref position);
			Status status = (Status)ByteConverter.GetOfType(resultMessage, ref position, typeof(Status));

			RemoteTask task;
			if (!m_outstandingTask.TryGetValue(taskId, out task))
			{
				Logger.DebugLog("Task ID not found: " + taskId, Logger.severity.WARNING);
				return;
			}
			task.CurrentStatus = status;
			task.KeepAlive();


			switch (status)
			{
				case Status.None:
					throw new Exception("None status");
				case Status.Started:
					Logger.TraceLog("Got heartbeat for "  + task);
					return;
				case Status.Success:
					Type taskType = task.GetType();
					foreach (FieldInfo field in taskType.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
						if (field.HasAttribute<ResultAttribute>())
							field.SetValue(task, ByteConverter.GetOfType(resultMessage, ref position, field.FieldType));
					foreach (PropertyInfo property in taskType.GetProperties(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
						if (property.HasAttribute<ResultAttribute>())
							property.SetValue(task, ByteConverter.GetOfType(resultMessage, ref position, property.PropertyType));
					break;
			}

			Logger.TraceLog("Got result for " + task);
			task.EndMonitorTask();
		}

		#endregion

		/// <summary>
		/// Get the steam ID of the client with the fewest outstanding tasks.
		/// </summary>
		private static ulong GetIdlemostClient()
		{
			KeyValuePair<ulong, Client> idlemost = new KeyValuePair<ulong, Client>(0uL, new Client() { OutstandingTasks = int.MaxValue });
			foreach (KeyValuePair<ulong, Client> c in m_clients)
				if (c.Value.OutstandingTasks < idlemost.Value.OutstandingTasks)
					idlemost = c;

			Logger.TraceLog("Idlemost: Steam ID: " + idlemost.Key + ", Client: " + idlemost.Value);

			Client client = idlemost.Value;
			++client.OutstandingTasks;
			return idlemost.Key;
		}

		private static ulong GetServerClient()
		{
			Client client = m_clients[ServerKey];
			++client.OutstandingTasks;
			return ServerKey;
		}

		/// <summary>
		/// Decrement OutstandingTasks for a steamId, if it is in m_clients.
		/// </summary>
		private static void TryDecrementOutstandingTasks(ulong steamId)
		{
			Client c;
			if (m_clients.TryGetValue(steamId, out c))
			{
				--c.OutstandingTasks;

				Logger.TraceLog("Decremented for " + steamId + ", outstanding: " + c.OutstandingTasks);
			}
		}

		private int TaskId;
		/// <summary>Steam ID of the client that is supposed to run the task.</summary>
		public ulong SteamId { get; private set; }
		/// <summary>The last time there was a response from the client.</summary>
		public DateTime LastResponse { get; private set; }
		public Status CurrentStatus { get; private set; }

		private void KeepAlive()
		{
			LastResponse = DateTime.UtcNow;
		}

		private void StartMonitorTask()
		{
			Logger.TraceLog("entered");

			KeepAlive();
			UpdateManager.Register(100, Monitor);
		}

		private void Monitor()
		{
			if (!m_clients.ContainsKey(SteamId))
			{
				Logger.TraceLog("Client disconnected");

				CurrentStatus = Status.Disconnect;
				EndMonitorTask();
			}
			else if (TimeoutAfter < DateTime.UtcNow - LastResponse)
			{
				Logger.TraceLog("Client stopped responding: " + ToString());

				CurrentStatus = Status.Timeout;
				EndMonitorTask();
			}
		}

		private void EndMonitorTask()
		{
			Logger.TraceLog("entered");

			UpdateManager.Unregister(100, Monitor);
			if (!m_outstandingTask.Remove(TaskId))
				Logger.AlwaysLog("Failed to remove task: " + TaskId + ", " + this, Logger.severity.ERROR);
			TryDecrementOutstandingTasks(SteamId);
			OnComplete();
		}

		/// <summary>
		/// Starts the task and ensures that hearbeats are sent to the server.
		/// </summary>
		private void StartTask()
		{
			Logger.TraceLog("entered");

			KeepAlive();
			UpdateManager.Register(100, Heartbeat);
			Start();
		}

		/// <summary>
		/// Sends CurrentStatus to the server.
		/// </summary>
		private void Heartbeat()
		{
			if (KeepAliveInterval < DateTime.UtcNow - LastResponse)
			{
				Logger.TraceLog("Sending heartbeat");
				KeepAlive();
				SendStatus(this);
			}
		}

		/// <summary>
		/// Subclass shall invoke when the task is complete, to send the status and results to the server.
		/// </summary>
		/// <param name="result">The final status of the task</param>
		protected void Completed(Status result)
		{
			Logger.TraceLog("entered");

			if (CurrentStatus != Status.Started)
				throw new Exception("Cannot change status, already " + CurrentStatus);

			UpdateManager.Unregister(100, Heartbeat);
			CurrentStatus = result;
			SendStatus(this);
		}

		/// <summary>
		/// Invoked on the client to start the task.
		/// </summary>
		protected abstract void Start();

		/// <summary>
		/// Invoked on the server after the task is completed.
		/// </summary>
		protected virtual void OnComplete() { }

		public override string ToString()
		{
			return GetType().Name + ": SteamId: " + SteamId + ", CurrentStatus: " + CurrentStatus + ", LastResponse: " + LastResponse + ", TaskId: " + TaskId;
		}

	}
}
