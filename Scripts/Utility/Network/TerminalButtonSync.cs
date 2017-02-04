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
	/// For running an event when a terminal control button is pressed.
	/// </summary>
	/// <typeparam name="TScript">The script that contains the value</typeparam>
	public sealed class TerminalButtonSync<TScript> : ASync
	{

		public delegate void OnButtonPressed(TScript script);

		private readonly OnButtonPressed _onPress;
		private readonly bool _serverOnly;

		protected override Type ValueType { get { return null; } }

		/// <summary>
		/// Run an event when a terminal control button is presseed.
		/// </summary>
		/// <param name="control">Button for triggering the event</param>
		/// <param name="onPress">The event to run.</param>
		/// <param name="serverOnly">If true, run the event on the server. Otherwise, run the event on all clients.</param>
		public TerminalButtonSync(IMyTerminalControlButton control, OnButtonPressed onPress, bool serverOnly = true)
			: base(typeof(TScript), control.Id, false)
		{
			traceLog("entered");

			_onPress = onPress;
			_serverOnly = serverOnly;

			control.Action = Pressed;
		}

		public void Pressed(IMyTerminalBlock block)
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
						SendValue(block.EntityId, null, MyAPIGateway.Multiplayer.ServerId);
					}
				}
				else
				{
					traceLog("running here and sending to server");
					_onPress(script);
					SendValue(block.EntityId, null);
				}
				return;
			}

			if (!Globals.WorldClosed)
				LogMissingFromRegistrar(block.EntityId, false);
		}

		public override void SetValue(long blockId, string value)
		{
			throw new NotSupportedException();
		}

		protected override void SetValue(SyncMessage sync)
		{
			for (int index = sync.entityId.Count - 1; index >= 0; --index)
			{
				long blockId = sync.entityId[index];

				TScript script;
				if (Registrar.TryGetValue(blockId, out script))
				{
					if (_serverOnly && !MyAPIGateway.Multiplayer.IsServer)
						Logger.AlwaysLog("Got button pressed event intended for the server", Logger.severity.WARNING, _id.ToString());
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

		protected override IEnumerable<KeyValuePair<long, object>> AllValues()
		{
			return new KeyValuePair<long, object>[0];
		}

	}

}
