#if DEBUG
//#define TRACE
#endif

using System;
using System.Collections.Generic;
using System.Text;
using Rynchodon.Update;
using Sandbox.ModAPI;
using Sandbox.ModAPI.Interfaces.Terminal;

namespace Rynchodon.Utility.Network
{
	/// <summary>
	/// Synchronize and save a StringBuilder associated with a terminal control. The StringBuilder is synchronized from time to time.
	/// </summary>
	/// <typeparam name="TScript">The script that contains the value</typeparam>
	public sealed class TerminalStringBuilderSync<TScript> : TerminalSync<StringBuilder, TScript>
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
		/// <param name="save">Iff true, save the value to disk.</param>
		public TerminalStringBuilderSync(Id id, IMyTerminalControlTextbox control, GetterDelegate getter, SetterDelegate setter, bool save = true)
			: base(id, control, getter, setter, save)
		{
			_logger.traceLog("entered");

			// MyTerminalControlTextbox has different Getter/Setter
			control.Getter = GetValue;
			control.Setter = SetValue;
		}

		/// <summary>
		/// If, after updateMethod is invoked, the StringBuilder passed to it does not match the current value, update the current value.
		/// </summary>
		/// <param name="block">The block whose StringBuilder may have changed.</param>
		/// <param name="updateMethod">Method to populate the StringBuilder</param>
		public void Update(IMyTerminalBlock block, Action<StringBuilder> updateMethod)
		{
			_logger.traceLog("entered");

			StringBuilder temp = _stringBuilderPool.Get(), current = GetValue(block);

			updateMethod.Invoke(temp);
			if (temp.EqualsIgnoreCapacity(current))
			{
				_logger.traceLog("equals previous value");
				temp.Clear();
				_stringBuilderPool.Return(temp);
			}
			else
			{
				_logger.traceLog("value changed from " + current + " to " + temp);
				SetValue(block, temp);
				current.Clear();
				_stringBuilderPool.Return(current);
			}
		}

		public override void SetValue(long blockId, string value)
		{
			SetValue(blockId, new StringBuilder(value));
		}

		protected override void SetValue(long blockId, TScript script, StringBuilder value, bool send)
		{
			_logger.traceLog("entered");

			_logger.traceLog("set value to " + value);
			_setter(script, value);
			if (send)
				EnqueueSend(blockId);

			UpdateVisual();
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
			_logger.traceLog("entered");

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
			_logger.traceLog("entered");

			if (_waitUntil > Globals.UpdateCount)
				return;

			foreach (long block in _updatedBlocks)
				SendValue(block, GetValue(block));

			UpdateManager.Unregister(10, Update10);
			_updatedBlocks = null;
		}

	}
}
