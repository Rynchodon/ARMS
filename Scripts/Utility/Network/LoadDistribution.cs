#if DEBUG
//#define TRACE
#endif

using System;
using System.Collections.Generic;
using System.Reflection;
using Sandbox.Game.World;
using Sandbox.ModAPI;

namespace Rynchodon.Utility.Network
{
	/// <summary>
	/// "Let's put these maggots to work."
	/// Allows the server to request that a client executes some task.
	/// </summary>
	/// TODO: recurring
	/// TODO: sim speed check?
	static class LoadDistribution
	{

		public abstract class TaskDefinition
		{
			public abstract bool Executor(object parameter);
			public virtual void ParamSerializer(List<byte> startMessage) { }
			public virtual object ParamDeserializer(byte[] startMessage, ref int pos) { return null; }
			public virtual void ResultSerializer(List<byte> resultMessage) { }
			public virtual object ResultDeserializer(byte[] resultMessage, ref int pos) { return null; }
		}

		public abstract class ATask
		{
			/// <summary>Steam ID of the user that is supposed to run the task.</summary>
			public readonly ulong SteamId;

			public abstract bool Completed { get; }
			public abstract bool Success { get;  }
			public abstract object Result { get; }

			protected ATask(ulong SteamId)
			{
				this.SteamId = SteamId;
			}

			public override string ToString()
			{
				return "SteamId: " + SteamId + ", Completed: " + Completed + ", Success: " + Success + ", Result: " + Result;
			}
		}

		private class Task : ATask
		{
			public Task(ulong SteamId) : base(SteamId) { }

			public bool IsCompleted, IsSuccessful;
			public object ResultObj;

			/// <summary>The client has indicated that it finished the task or the client disconnected from the server.</summary>
			public override bool Completed { get { return IsCompleted || !m_clients.ContainsKey(SteamId); } }
			/// <summary>The task finished without throwing an excetion and returned true.</summary>
			public override bool Success { get { return IsSuccessful; } }
			public override object Result { get { return ResultObj; } }
		}

		private class Client
		{
			public int Controllers;
			public int OutstandingTasks;
		}

		/// <summary>An instance of every class that extends TaskDefinition, indexed by the order of Assembly.DefinedTypes.</summary>
		private static Dictionary<byte ,TaskDefinition> m_definitions = new Dictionary<byte,TaskDefinition >();
		/// <summary>Client indexed by steam id.</summary>
		private static Dictionary<ulong, Client> m_clients;
		private static byte m_nextOutstandingTaskId;
		private static Dictionary<byte, Task> m_outstandingTask;

		static LoadDistribution()
		{
			MessageHandler.AddHandler(MessageHandler.SubMod.LoadDistribution, HandleMessage);
			byte index = 0;
			foreach (TypeInfo t in Assembly.GetExecutingAssembly().DefinedTypes)
				if (t.IsSubclassOf(typeof(TaskDefinition)))
					try { m_definitions.Add(index++, (TaskDefinition)Activator.CreateInstance(t)); }
					catch (Exception ex) { Logger.AlwaysLog("Failed to create instance of type: " + t + "\n" + ex, Logger.severity.ERROR); }
		}

		#region Load

		[OnWorldLoad]
		private static void Load()
		{
			if (!MyAPIGateway.Multiplayer.IsServer)
				return;

			MySession.Static.Players.PlayersChanged += OnPlayersChanged;
			m_clients = new Dictionary<ulong, Client>();
			m_clients.Add(0uL, new Client() { OutstandingTasks = int.MaxValue / 2 }); // for the server
			foreach (MyPlayer player in MySession.Static.Players.GetOnlinePlayers())
				OnPlayersChanged(true, player.Id);

			m_outstandingTask = new Dictionary<byte, Task>();
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
			}
			else
			{
				if (Globals.WorldClosed)
					return;

				Client c = m_clients[playerId.SteamId];
				--c.Controllers;
				if (c.Controllers == 0)
					m_clients.Remove(playerId.SteamId);
			}
		}

		#endregion

		#region Message

		/*
		 * Start message format: Sub Mod ID, TaskDefinition ID, task ID[, param]
		 * Result message format: Sub Mod ID, TaskDefinition ID, task ID, success[, result]
		 */

		/// <summary>
		/// Start a task on a client if possible, otherwise it will run on the server.
		/// </summary>
		/// <typeparam name="T">The TaskDefinition for the created task.</typeparam>
		/// <returns>An object used to monitor the success and results of the task.</returns>
		public static ATask StartTask<T>() where T : TaskDefinition, new()
		{
			if (!MyAPIGateway.Multiplayer.IsServer)
				throw new Exception("Not server");

			Type t = typeof(T);
			foreach (KeyValuePair<byte, TaskDefinition> pair in m_definitions)
				if (pair.Value is T && pair.Value.GetType() == t)
				{
					Task task = new Task(GetIdleMostClient());

					byte taskId = m_nextOutstandingTaskId++;
					while (m_outstandingTask.ContainsKey(taskId))
						taskId = m_nextOutstandingTaskId++;

					m_outstandingTask.Add(taskId, task);
					if (m_outstandingTask.Count >= 100)
						throw new Exception("Too many outstanding tasks");

					List<byte> startMessage = new List<byte>(11);
					ByteConverter.AppendBytes(startMessage, MessageHandler.SubMod.LoadDistribution);
					ByteConverter.AppendBytes(startMessage, pair.Key);
					ByteConverter.AppendBytes(startMessage, taskId);
					((T)pair.Value).ParamSerializer(startMessage);

					Logger.TraceLog("Sending task request: " + typeof(T) + ", " + pair.Key + ", " + taskId + ", task: " + task);

					if (task.SteamId == 0uL || task.SteamId == MyAPIGateway.Multiplayer.MyId)
						HandleStartRequest(startMessage.ToArray(), 1);
					else if (!MyAPIGateway.Multiplayer.SendMessageTo(MessageHandler.ModId, startMessage.ToArray(), task.SteamId))
						throw new Exception("Message is too long");

					return task;
				}

			throw new Exception("No task defintion found of type: " + typeof(T));
		}

		private static void HandleMessage(byte[] message, int position)
		{
			if (MyAPIGateway.Multiplayer.IsServer)
				HandleResult(message, position);
			else
				HandleStartRequest(message, position);
		}

		/// <summary>
		/// Handle a task request from the server.
		/// </summary>
		private static void HandleStartRequest(byte[] startMessage, int position)
		{
			byte taskDefnId = ByteConverter.GetByte(startMessage, ref position);
			TaskDefinition defn;
			if (!m_definitions.TryGetValue(taskDefnId, out defn))
				throw new Exception("Failed to get TaskDefinition for " + taskDefnId);

			byte taskId = ByteConverter.GetByte(startMessage, ref position);
			object parameter = defn.ParamDeserializer(startMessage, ref position);

			Logger.TraceLog("Got task request: " + defn.GetType() + ", " + taskDefnId + ", " + taskId);

			bool success;
			try { success = defn.Executor(parameter); }
			catch (Exception ex)
			{
				Logger.AlwaysLog("Exception: " + ex, Logger.severity.ERROR);
				success = false;
			}

			Logger.TraceLog("Executed. Success: " + success);

			// send result message
			List<byte> resultMessage = new List<byte>(11);
			ByteConverter.AppendBytes(resultMessage, MessageHandler.SubMod.LoadDistribution);
			ByteConverter.AppendBytes(resultMessage, taskDefnId);
			ByteConverter.AppendBytes(resultMessage, taskId);
			ByteConverter.AppendBytes(resultMessage, success);
			if (success)
				defn.ResultSerializer(resultMessage);

			Logger.TraceLog("Sending results");

			if (MyAPIGateway.Multiplayer.IsServer)
				HandleResult(resultMessage.ToArray(), 1);
			else if (!MyAPIGateway.Multiplayer.SendMessageToServer(resultMessage.ToArray()))
				throw new Exception("Message is too long");
		}

		/// <summary>
		/// Handle a message with the result from the client.
		/// </summary>
		private static void HandleResult(byte[] resultMessage, int position)
		{
			byte taskDefnId = ByteConverter.GetByte(resultMessage, ref position);
			TaskDefinition defn;
			if (!m_definitions.TryGetValue(taskDefnId, out defn))
				throw new Exception("Failed to get TaskDefinition for " + taskDefnId);

			byte taskId = ByteConverter.GetByte(resultMessage, ref position);
			Task task = m_outstandingTask[taskId];
			task.IsSuccessful = ByteConverter.GetBool(resultMessage, ref position);
			if (task.IsSuccessful)
				task.ResultObj = defn.ResultDeserializer(resultMessage, ref position);

			Logger.TraceLog("Got result: " + defn.GetType() + ", " + taskDefnId + ", " + taskId + ", " + task.IsSuccessful);

			m_outstandingTask.Remove(taskId);
			TryDecrementOutstandingTasks(task.SteamId);
			task.IsCompleted = true;
		}

		#endregion

		/// <summary>
		/// Get the steam ID of the client with the fewest outstanding tasks.
		/// </summary>
		private static ulong GetIdleMostClient()
		{
			KeyValuePair<ulong, Client> idleMost = new KeyValuePair<ulong, Client>(0uL, new Client() { OutstandingTasks = int.MaxValue });
			foreach (KeyValuePair<ulong, Client> c in m_clients)
				if (c.Value.OutstandingTasks < idleMost.Value.OutstandingTasks)
					idleMost = c;

			Logger.TraceLog("idleMost: " + idleMost.Key + ", " + idleMost.Value.OutstandingTasks);

			Client client = idleMost.Value;
			++client.OutstandingTasks;
			return idleMost.Key;
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

	}
}
