#if DEBUG
#define TRACE
#endif

using System;
using System.Collections.Generic;
using Sandbox.ModAPI;
using Sandbox.ModAPI.Interfaces.Terminal;

namespace Rynchodon.Utility.Network
{

	/// <summary>
	/// For running an event.
	/// </summary>
	/// <typeparam name="TScript">The script that contains the value</typeparam>
	public sealed class EventSync<TScript> : ASync
	{

		public delegate void Event(TScript script);

		private class OutgoingMessage
		{
			public readonly ulong? ClientId;
			public readonly List<long> EntityId = new List<long>();

			public OutgoingMessage(long EntityId, ulong? ClientId)
			{
				this.EntityId.Add(EntityId);
				this.ClientId = ClientId;
			}
		}

		private readonly Event _onPress;
		private readonly bool _serverOnly;

		private OutgoingMessage _outgoing;

		/// <summary>
		/// Run an event when a terminal control button is presseed.
		/// </summary>
		/// <param name="control">Button for triggering the event</param>
		/// <param name="onPress">The event to run.</param>
		/// <param name="serverOnly">If true, run the event on the server. Otherwise, run the event on all clients.</param>
		public EventSync(IMyTerminalControlButton control, Event onPress, bool serverOnly = true)
			: base(typeof(TScript), control.Id, false)
		{
			traceLog("entered");

			_onPress = onPress;
			_serverOnly = serverOnly;

			control.Action = RunEvent;
		}

		/// <summary>
		/// Run an event when RunEvent is invoked.
		/// </summary>
		/// <param name="controlId">Name of event.</param>
		/// <param name="onPress">The event to run.</param>
		/// <param name="serverOnly">If true, run the event on the server. Otherwise, run the event on all clients.</param>
		public EventSync(string controlId, Event onPress, bool serverOnly = true)
			: base(typeof(TScript), controlId, false)
		{
			traceLog("entered");

			_onPress = onPress;
			_serverOnly = serverOnly;
		}

		public void RunEvent(IMyTerminalBlock block)
		{
			traceLog("entered");

			TScript script;
			if (Registrar.TryGetValue(block.EntityId, out script))
			{
				if (_serverOnly)
				{
					if (MyAPIGateway.Multiplayer.IsServer)
					{
						traceLog("running here");
						_onPress(script);
					}
					else
					{
						traceLog("sending to server");
						SendValue(block.EntityId, MyAPIGateway.Multiplayer.ServerId);
					}
				}
				else
				{
					traceLog("running here and sending to all");
					_onPress(script);
					SendValue(block.EntityId);
				}
				return;
			}

			if (!Globals.WorldClosed)
				LogMissingFromRegistrar(block.EntityId, false);
		}

		public override void SetValueFromSave(long blockId, string value)
		{
			throw new NotSupportedException();
		}

		protected override void SetValueFromNetwork(byte[] message, int position)
		{
			while (position < message.Length)
			{
				long blockId = ByteConverter.GetLong(message, ref position);

				TScript script;
				if (Registrar.TryGetValue(blockId, out script))
				{
					if (_serverOnly && !MyAPIGateway.Multiplayer.IsServer)
						Logger.AlwaysLog("Got event intended for the server", Logger.severity.WARNING, _id.ToString());
					else
					{
						traceLog("running here");
						_onPress(script);
					}
					return;
				}

				if (!Globals.WorldClosed)
					LogMissingFromRegistrar(blockId, true);
			}
		}

		#region SendValue

		protected override void SendAllToClient(ulong clientId) { }

		private void SendValue(long entityId, ulong? clientId = null)
		{
			if (_outgoing == null)
			{
				_outgoing = new OutgoingMessage(entityId, clientId);
				MyAPIGateway.Utilities.InvokeOnGameThread(SendOutgoing);
			}
			else if (_outgoing.ClientId == clientId)
			{
				_outgoing.EntityId.Add(entityId);
			}
			else
			{
				SendOutgoing();
				_outgoing = new OutgoingMessage(entityId, clientId);
			}
		}

		private void SendOutgoing()
		{
			const int maxSize = 4088;

			List<byte> bytes; ResourcePool.Get(out bytes);
			AddIdAndValue(bytes);

			int initCount = bytes.Count;

			if (initCount > maxSize)
			{
				Logger.AlwaysLog("Cannot send message, value is too large. byte count: " + initCount);
				_outgoing = null;
				return;
			}

			for (int entityIdIndex = _outgoing.EntityId.Count - 1; entityIdIndex >= 0; --entityIdIndex)
			{
				ByteConverter.AppendBytes(bytes, _outgoing.EntityId[entityIdIndex]);
				if (bytes.Count > maxSize)
				{
					Logger.TraceLog("Over max size, splitting message");
					SendOutgoing(bytes);
					AddIdAndValue(bytes);
				}
			}

			SendOutgoing(bytes);
			_outgoing = null;
			bytes.Clear();
			ResourcePool.Return(bytes);
		}

		private void AddIdAndValue(List<byte> bytes)
		{
			bytes.Clear();
			ByteConverter.AppendBytes(bytes, MessageHandler.SubMod.Sync);
			ByteConverter.AppendBytes(bytes, _id);
		}

		private void SendOutgoing(List<byte> bytes)
		{
			Logger.TraceLog("sending to: " + _outgoing.ClientId + ", entities: " + string.Join(",", _outgoing.EntityId));
			bool result = _outgoing.ClientId.HasValue ?
				MyAPIGateway.Multiplayer.SendMessageTo(MessageHandler.ModId, bytes.ToArray(), _outgoing.ClientId.Value) :
				MyAPIGateway.Multiplayer.SendMessageToOthers(MessageHandler.ModId, bytes.ToArray());
			if (!result)
				Logger.AlwaysLog("Failed to send message, length: " + bytes.Count, Logger.severity.ERROR);
		}

		#endregion

		protected override void WriteToSave(List<byte> bytes)
		{
			throw new NotSupportedException();
		}

		protected override void ReadFromSave(byte[] bytes, ref int position)
		{
			throw new NotSupportedException();
		}

	}
}
