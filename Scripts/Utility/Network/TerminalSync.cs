#if DEBUG
#define TRACE
#endif

using System;
using System.Collections.Generic;
using System.Text;
using Rynchodon.Update;
using Sandbox.Game.Entities.Cube;
using Sandbox.Game.Gui;
using Sandbox.Game.Screens.Terminal.Controls;
using Sandbox.ModAPI;
using VRage.Collections;

namespace Rynchodon.Utility.Network
{
	/// <summary>
	/// Objects of this type synchronize and save terminal control values.
	/// </summary>
	/// TODO: saving
	/// TODO: store extra values
	public abstract class TerminalSync
	{

		public enum Id : byte
		{
			ProgrammableBlock_HandleDetected,
			ProgrammableBlock_BlockList,
		}

		private static Dictionary<Id, TerminalSync> _syncs;
		protected static MyConcurrentPool<StringBuilder> _stringBuilderPool = new MyConcurrentPool<StringBuilder>();
		private static bool _hasValues;

		static TerminalSync()
		{
			MessageHandler.AddHandler(MessageHandler.SubMod.TerminalSync, HandleValue);
			MessageHandler.AddHandler(MessageHandler.SubMod.TerminalSyncRequest, HandleRequest);
		}

		[OnWorldLoad]
		private static void OnLoad()
		{
			_syncs = new Dictionary<Id, TerminalSync>();
			_hasValues = MyAPIGateway.Multiplayer.IsServer;
		}

		[OnWorldClose]
		private static void OnClose()
		{
			_syncs = null;
		}

		private static void HandleValue(byte[] message, int position)
		{
			if (_syncs == null)
				return;

			try
			{
				Id id = (Id)ByteConverter.GetOfType(message, ref position, typeof(Id));
				Logger.TraceLog("id: " + id);
				TerminalSync instance;
				if (!_syncs.TryGetValue(id, out instance))
				{
					Logger.AlwaysLog("Missing " + typeof(TerminalSync).Name + " for " + id, Logger.severity.ERROR);
					return;
				}
				Logger.TraceLog("got instance: " + instance.GetType().Name);
				Type type = instance.ValueType;
				Logger.TraceLog("type: " + type);
				while (position < message.Length)
				{
					long blockId = ByteConverter.GetLong(message, ref position);
					Logger.TraceLog("block id: " + blockId);
					object value = type == null ? null : ByteConverter.GetOfType(message, ref position, type);
					Logger.TraceLog("value: " + value);
					instance.SetValue(blockId, value);
				}
			}
			catch (Exception ex)
			{
				Logger.AlwaysLog(ex.ToString(), Logger.severity.ERROR);
				Logger.AlwaysLog("Message: " + string.Join(",", message), Logger.severity.ERROR);
			}
		}

		private static void HandleRequest(byte[] message, int position)
		{
			if (_syncs == null)
				return;

			try
			{
				SendValues(ByteConverter.GetUlong(message, ref position));
			}
			catch (Exception ex)
			{
				Logger.AlwaysLog(ex.ToString(), Logger.severity.ERROR);
				Logger.AlwaysLog("Message: " + string.Join(",", message), Logger.severity.ERROR);
			}
		}

		private static void RequestValues()
		{
			if (MyAPIGateway.Multiplayer.IsServer)
				throw new Exception("Cannot request values, this is the server");

			List<byte> bytes; ResourcePool.Get(out bytes);
			bytes.Clear();

			ByteConverter.AppendBytes(bytes, MessageHandler.SubMod.TerminalSyncRequest);
			ByteConverter.AppendBytes(bytes, MyAPIGateway.Multiplayer.MyId);

			Logger.DebugLog("requesting values from server");

			if (!MyAPIGateway.Multiplayer.SendMessageToServer(MessageHandler.ModId, bytes.ToArray()))
				Logger.AlwaysLog("Failed to send message, length: " + bytes.Count, Logger.severity.ERROR);

			bytes.Clear();
			ResourcePool.Return(bytes);
		}

		private static void SendValues(ulong recipient)
		{
			if (!MyAPIGateway.Multiplayer.IsServer)
			{
				Logger.AlwaysLog("Cannot send values, this is not the server", Logger.severity.ERROR);
				return;
			}
			if (_syncs == null)
				return;

			List<byte> bytes; ResourcePool.Get(out bytes);
			bytes.Clear();
			int size = 0;

			foreach (KeyValuePair<Id, TerminalSync> termSync in _syncs)
			{
				foreach (KeyValuePair<long, object> valuePair in termSync.Value.AllValues())
				{
					int count = bytes.Count;

					ByteConverter.AppendBytes(bytes, valuePair.Key);
					ByteConverter.AppendBytes(bytes, valuePair.Value);

					size = bytes.Count - count;

					if (count + size > 4094)
						SendValues(termSync.Key, bytes, recipient);
				}
				SendValues(termSync.Key, bytes, recipient);
			}

			bytes.Clear();
			ResourcePool.Return(bytes);
		}

		private static void SendValues(Id id, List<byte> bytes, ulong recipient)
		{
			if (bytes.Count == 0)
				return;

			byte[] message = new byte[bytes.Count + 2];
			message[0] = (byte)Convert.ChangeType(MessageHandler.SubMod.TerminalSync, TypeCode.Byte);
			message[1] = (byte)Convert.ChangeType(id, TypeCode.Byte);
			bytes.CopyTo(message, 2);
			bytes.Clear();

			Logger.DebugLog("sending values for " + id + ", message length: " + message.Length);

			if (!MyAPIGateway.Multiplayer.SendMessageTo(MessageHandler.ModId, message, recipient))
				Logger.AlwaysLog("Failed to send message, length: " + bytes.Count, Logger.severity.ERROR);
		}

		protected readonly Id _id;
		protected readonly bool _save;

		protected TerminalSync(Id id, bool save = true)
		{
			Logger.TraceLog("entered");

			this._id = id;
			this._save = save;

			if (_syncs == null)
				return;
			_syncs.Add(id, this);

			if (!_hasValues)
			{
				_hasValues = true;
				RequestValues();
			}
		}

		/// <summary>
		/// Send a value to other game clients.
		/// </summary>
		/// <param name="blockId">EntityId of the block</param>
		/// <param name="value">The value to send</param>
		/// <param name="clientId">Id of the client to send to, null = all</param>
		protected void SendValue(long blockId, object value, ulong? clientId = null)
		{
			Logger.TraceLog("entered, blockId: " + blockId + ", value: " + value + ", clientId: " + clientId);

			List<byte> bytes; ResourcePool.Get(out bytes);
			bytes.Clear();

			ByteConverter.AppendBytes(bytes, MessageHandler.SubMod.TerminalSync);
			ByteConverter.AppendBytes(bytes, _id);
			ByteConverter.AppendBytes(bytes, blockId);
			if (value != null)
				ByteConverter.AppendBytes(bytes, value);

			bool result = clientId.HasValue ?
				MyAPIGateway.Multiplayer.SendMessageTo(MessageHandler.ModId, bytes.ToArray(), clientId.Value) :
				MyAPIGateway.Multiplayer.SendMessageToOthers(MessageHandler.ModId, bytes.ToArray());
			if (!result)
				Logger.AlwaysLog("Failed to send message, length: " + bytes.Count + ", value: " + value, Logger.severity.ERROR, blockId.ToString(), value?.GetType().Name);

			bytes.Clear();
			ResourcePool.Return(bytes);
		}

		/// <summary>The type of value contained.</summary>
		protected abstract Type ValueType { get; }

		/// <summary>
		/// Set the value from network message.
		/// </summary>
		/// <param name="blockId">EntityId of the block</param>
		/// <param name="value">The value as object</param>
		protected abstract void SetValue(long blockId, object value);

		protected abstract IEnumerable<KeyValuePair<long, object>> AllValues();

	}

	/// <summary>
	/// For running an event when a terminal control button is pressed.
	/// </summary>
	/// <typeparam name="TBlock">The type of block</typeparam>
	/// <typeparam name="TScript">The script that contains the value</typeparam>
	public sealed class TerminalButtonSync<TBlock, TScript> : TerminalSync where TBlock : MyTerminalBlock
	{

		public delegate void OnButtonPressed(TScript script);

		private readonly OnButtonPressed _onPress;
		private readonly bool _serverOnly;

		protected override Type ValueType { get { return null; } }

		/// <summary>
		/// Run an event when a terminal control button is presseed.
		/// </summary>
		/// <param name="id">Unique id for sending accross a network.</param>
		/// <param name="control">Button for triggering the event</param>
		/// <param name="onPress">The event to run.</param>
		/// <param name="serverOnly">If true, run the event on the server. Otherwise, run the event on all clients.</param>
		public TerminalButtonSync(Id id, MyTerminalControlButton<TBlock> control, OnButtonPressed onPress, bool serverOnly = true) : base(id, false)
		{
			Logger.TraceLog("entered", context: _id.ToString());

			_onPress = onPress;
			_serverOnly = serverOnly;

			control.Action = Pressed;
		}

		private void Pressed(TBlock block)
		{
			Logger.TraceLog("entered", context: _id.ToString());

			TScript script;
			if (Registrar.TryGetValue(block.EntityId, out script))
			{
				if (_serverOnly)
				{
					if (MyAPIGateway.Multiplayer.IsServer)
					{
						Logger.TraceLog("running here", context: _id.ToString());
						_onPress(script);
					}
					else
					{
						Logger.TraceLog("sending to server", context: _id.ToString());
						SendValue(block.EntityId, null, MyAPIGateway.Multiplayer.ServerId);
					}
				}
				else
				{
					Logger.TraceLog("running here and sending to server", context: _id.ToString());
					_onPress(script);
					SendValue(block.EntityId, null);
				}
				return;
			}

			if (!Globals.WorldClosed)
				Logger.AlwaysLog("block not found in Registrar: " + block.nameWithId(), Logger.severity.WARNING, _id.ToString());
		}

		protected override void SetValue(long blockId, object value)
		{
			Logger.TraceLog("entered", context: _id.ToString());

			TScript script;
			if (Registrar.TryGetValue(blockId, out script))
			{
				if (_serverOnly && !MyAPIGateway.Multiplayer.IsServer)
					Logger.AlwaysLog("Got button pressed event intended for the server", Logger.severity.WARNING, _id.ToString());
				else
				{
					Logger.TraceLog("running here", context: _id.ToString());
					_onPress(script);
				}
				return;
			}

			if (!Globals.WorldClosed)
				Logger.AlwaysLog("block not found in Registrar: " + blockId, Logger.severity.WARNING, _id.ToString());
		}

		protected override IEnumerable<KeyValuePair<long, object>> AllValues()
		{
			return new KeyValuePair<long, object>[0];
		}

	}

	/// <summary>
	/// Contains generic members of TerminalSync.
	/// </summary>
	/// <typeparam name="TBlock">The type of block</typeparam>
	/// <typeparam name="TValue">The type of value</typeparam>
	/// <typeparam name="TScript">The script that contains the value</typeparam>
	public abstract class TerminalSync<TBlock, TValue, TScript> : TerminalSync where TBlock : MyTerminalBlock
	{

		public delegate TValue GetterDelegate(TScript script);
		public delegate void SetterDelegate(TScript script, TValue value);
		//public delegate void AfterValueChanged(TScript scirpt);

		public readonly MyTerminalValueControl<TBlock, TValue> _control;
		protected readonly GetterDelegate _getter;
		protected readonly SetterDelegate _setter;
		//protected readonly AfterValueChanged _afterValueChanged;

		public TValue this[TBlock block]
		{
			get { return GetValue(block); }
			set { SetValue(block, value); }
		}

		public TValue this[long blockId]
		{
			get { return GetValue(blockId); }
			set { SetValue(blockId, value); }
		}

		protected override sealed Type ValueType { get { return typeof(TValue); } }

		protected TerminalSync(Id id, MyTerminalValueControl<TBlock, TValue> control, GetterDelegate getter, SetterDelegate setter, /*AfterValueChanged afterValueChanged = null,*/ bool save = true)
			: base(id, save)
		{
			Logger.TraceLog("entered", context: _id.ToString());

			_control = control;
			_getter = getter;
			_setter = setter;
			//_afterValueChanged = afterValueChanged;

			control.Getter = GetValue;
			control.Setter = SetValue;
		}

		protected TValue GetValue(TBlock block)
		{
			return GetValue(block.EntityId);
		}

		protected TValue GetValue(long blockId)
		{
			Logger.TraceLog("entered", context: _id.ToString());

			TScript script;
			if (Registrar.TryGetValue(blockId, out script))
				return _getter(script);

			if (!Globals.WorldClosed)
				Logger.AlwaysLog("block not found in Registrar: " + blockId, Logger.severity.WARNING, _id.ToString());
			return _control.GetDefaultValue(null);
		}

		protected void SetValue(TBlock block, TValue value)
		{
			SetValue(block.EntityId, value, true);
		}

		protected override sealed void SetValue(long blockId, object value)
		{
			SetValue(blockId, (TValue)value, false);
		}

		protected abstract void SetValue(long blockId, TValue value, bool send);

		protected override IEnumerable<KeyValuePair<long, object>> AllValues()
		{
			foreach (KeyValuePair<long, TScript> pair in Registrar.IdScripts<TScript>())
			{
				TValue value = _getter(pair.Value);
				if (EqualityComparer<TValue>.Default.Equals(value, default(TValue)))
					continue;
				yield return new KeyValuePair<long, object>(pair.Key, value);
			}
		}

	}

	/// <summary>
	/// For synchronizing and saving terminal control values where the value is synchronized everytime it changes.
	/// </summary>
	/// <typeparam name="TBlock">The type of block</typeparam>
	/// <typeparam name="TValue">The type of value</typeparam>
	/// <typeparam name="TScript">The script that contains the value</typeparam>
	public sealed class TerminalValueSync<TBlock, TValue, TScript> : TerminalSync<TBlock, TValue, TScript> where TBlock : MyTerminalBlock where TValue : struct
	{

		/// <summary>
		/// Synchronize and save a value associated with a terminal control. The value will be synchronized everytime it changes.
		/// </summary>
		/// <param name="id">Unique id for sending accross a network.</param>
		/// <param name="control">GUI control for getting/setting the value.</param>
		/// <param name="getter">Function to get the value from a script.</param>
		/// <param name="setter">Function to set a value in a script.</param>
		///// <param name="afterValueChanged">Function invoked after value changes. Do not put UpdateVisual here, it is always invoked.</param>
		/// <param name="save">Iff true, save the value to disk.</param>
		public TerminalValueSync(Id id, MyTerminalValueControl<TBlock, TValue> control, GetterDelegate getter, SetterDelegate setter, /*AfterValueChanged afterValueChanged = null,*/ bool save = true) : base(id, control, getter, setter, /*afterValueChanged,*/ save) { }

		protected override void SetValue(long blockId, TValue value, bool send)
		{
			Logger.TraceLog("entered", context: _id.ToString());

			TScript script;
			if (Registrar.TryGetValue(blockId, out script))
			{
				TValue currentValue = _getter(script);
				if (!EqualityComparer<TValue>.Default.Equals(value, currentValue))
				{
					Logger.TraceLog("value changed from " + currentValue + " to " + value);
					_setter(script, value);
					if (send)
						SendValue(blockId, value);
					_control.UpdateVisual();
					//_afterValueChanged?.Invoke(script);
				}
				else
					Logger.TraceLog("equals previous value", context: _id.ToString());
				return;
			}

			if (!Globals.WorldClosed)
				Logger.AlwaysLog("block not found in Registrar: " + blockId, Logger.severity.WARNING, _id.ToString());
		}

	}

	/// <summary>
	/// Synchronize and save a StringBuilder associated with a terminal control. The StringBuilder is synchronized from time to time.
	/// </summary>
	/// <typeparam name="TBlock">The type of block</typeparam>
	/// <typeparam name="TScript">The script that contains the value</typeparam>
	public sealed class TerminalStringBuilderSync<TBlock, TScript> : TerminalSync<TBlock, StringBuilder, TScript> where TBlock : MyTerminalBlock
	{

		private static HashSet<long> _updatedBlocks;
		private static ulong _waitUntil;

		/// <summary>
		/// Synchronize and save a StringBuilder associated with a MyTerminalControlTextbox. The StringBuilder is synchronized from time to time.
		/// </summary>
		/// <param name="id">Unique id for sending across a network</param>
		/// <param name="control">GUI control for getting/setting the value.</param>
		/// <param name="getter">Function to get the StringBuilder from a script.</param>
		/// <param name="setter">Function to set a StringBuilder in a script.</param>
		///// <param name="afterValueChanged">Function invoked after StringBuilder changes. Do not put UpdateVisual here, it is always invoked.</param>
		/// <param name="save">Iff true, save the value to disk.</param>
		public TerminalStringBuilderSync(Id id, MyTerminalControlTextbox<TBlock> control, GetterDelegate getter, SetterDelegate setter, /*AfterValueChanged afterValueChanged = null,*/ bool save = true)
			: base(id, control, getter, setter, /*afterValueChanged,*/ save)
		{
			Logger.TraceLog("entered", context: _id.ToString());

			// MyTerminalControlTextbox has different Getter/Setter
			control.Getter = GetValue;
			control.Setter = SetValue;
		}

		/// <summary>
		/// If, after updateMethod is invoked, the StringBuilder passed to it does not match the current value, update the current value.
		/// </summary>
		/// <param name="block">The block whose StringBuilder may have changed.</param>
		/// <param name="updateMethod">Method to populate the StringBuilder</param>
		public void Update(TBlock block, Action<StringBuilder> updateMethod)
		{
			Logger.TraceLog("entered", context: _id.ToString());

			StringBuilder temp = _stringBuilderPool.Get(), current = GetValue(block);

			updateMethod.Invoke(temp);
			if (temp.EqualsIgnoreCapacity(current))
			{
				Logger.TraceLog("equals previous value", context: _id.ToString());
				temp.Clear();
				_stringBuilderPool.Return(temp);
			}
			else
			{
				Logger.TraceLog("value changed from " + current + " to " + temp, context: _id.ToString());
				SetValue(block, temp);
				current.Clear();
				_stringBuilderPool.Return(current);
			}
		}

		protected override void SetValue(long blockId, StringBuilder value, bool send)
		{
			Logger.TraceLog("entered", context: _id.ToString());

			TScript script;
			if (Registrar.TryGetValue(blockId, out script))
			{
				Logger.TraceLog("set value to " + value);
				_setter(script, value);
				if (send)
					EnqueueSend(blockId);
				_control.UpdateVisual();
				//_afterValueChanged?.Invoke(script);
				return;
			}

			if (!Globals.WorldClosed)
				Logger.AlwaysLog("block not found in Registrar: " + blockId, Logger.severity.WARNING, _id.ToString());
		}

		protected override IEnumerable<KeyValuePair<long, object>> AllValues()
		{
			foreach (KeyValuePair<long, TScript> pair in Registrar.IdScripts<TScript>())
			{
				StringBuilder value = _getter(pair.Value);
				if (value == null || value.Length == 0)
					continue;
				yield return new KeyValuePair<long, object>(pair.Key, value);
			}
		}

		private void EnqueueSend(long blockId)
		{
			Logger.TraceLog("entered", context: _id.ToString());

			if (_updatedBlocks == null)
			{
				_updatedBlocks = new HashSet<long>();
				UpdateManager.Register(10, Update10);
			}

			_updatedBlocks.Add(blockId);
			_waitUntil = Globals.UpdateCount + 120uL;
		}

		private void Update10()
		{
			Logger.TraceLog("entered", context: _id.ToString());

			if (_waitUntil > Globals.UpdateCount)
				return;

			foreach (long block in _updatedBlocks)
				SendValue(block, GetValue(block));

			UpdateManager.Unregister(10, Update10);
			_updatedBlocks = null;
		}

	}
}
