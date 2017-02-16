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
	/// Synchronize and save a StringBuilder. The StringBuilder is synchronized from time to time.
	/// </summary>
	/// <typeparam name="TScript">The script that contains the value</typeparam>
	public sealed class StringBuilderSync<TScript> : AValueSync<StringBuilder, TScript>
	{

		private readonly IMyTerminalControl _control;

		private HashSet<long> _updatedBlocks;
		private ulong _waitUntil;

		protected override IEqualityComparer<StringBuilder> EqualityComparer { get { return EqualityComparer_StringBuilder.Instance; } }

		/// <summary>
		/// Synchronize and save a StringBuilder associated with a MyTerminalControlTextbox. The StringBuilder is synchronized from time to time.
		/// </summary>
		/// <param name="control">GUI control for getting/setting the value.</param>
		/// <param name="getter">Function to get the StringBuilder from a script.</param>
		/// <param name="setter">Function to set a StringBuilder in a script.</param>
		/// <param name="save">Iff true, save the value to disk.</param>
		public StringBuilderSync(IMyTerminalControlTextbox control, GetterDelegate getter, SetterDelegate setter, bool save = true)
			: base(((IMyTerminalControl)control).Id, getter, setter, save)
		{
			// MyTerminalControlTextbox has different Getter/Setter
			control.Getter = GetValue;
			control.Setter = SetValue;

			_control = control;
		}

		public StringBuilderSync(string id, GetterDelegate getter, SetterDelegate setter, bool save = true)
			: base(id, getter, setter, save) { }

		/// <summary>
		/// Synchronize and save a StringBuilder associated with a MyTerminalControlTextbox. The StringBuilder is synchronized from time to time.
		/// </summary>
		/// <param name="control">GUI control for getting/setting the value.</param>
		/// <param name="fieldName">The name of a field in the script to get/set the value from/to.</param>
		/// <param name="save">Iff true, save the value to disk.</param>
		public StringBuilderSync(IMyTerminalControlTextbox control, string fieldName, bool save = true)
			: base(((IMyTerminalControl)control).Id, fieldName, save)
		{
			// MyTerminalControlTextbox has different Getter/Setter
			control.Getter = GetValue;
			control.Setter = SetValue;

			_control = control;
		}

		public StringBuilderSync(string id, string fieldName, bool save = true)
			: base(id, fieldName, save) { }

		protected override StringBuilder GetDefaultValue()
		{
			return new StringBuilder();
		}

		/// <summary>
		/// If, after updateMethod is invoked, the StringBuilder passed to it does not match the current value, update the current value.
		/// </summary>
		/// <param name="block">The block whose StringBuilder may have changed.</param>
		/// <param name="updateMethod">Method to populate the StringBuilder</param>
		public void Update(IMyTerminalBlock block, Action<StringBuilder> updateMethod)
		{
			traceLog("entered");

			StringBuilder temp = _stringBuilderPool.Get(), current = GetValue(block);

			updateMethod.Invoke(temp);
			if (temp.EqualsIgnoreCapacity(current))
			{
				traceLog("equals previous value");
				temp.Clear();
				_stringBuilderPool.Return(temp);
			}
			else
			{
				traceLog("value changed from " + current + " to " + temp);
				SetValue(block, temp);
				current.Clear();
				_stringBuilderPool.Return(current);
			}
		}

		protected override void SetValue(long blockId, TScript script, StringBuilder value, bool send)
		{
			traceLog("entered");

			traceLog("set value to " + value);
			_setter(script, value);
			if (send)
				EnqueueSend(blockId);

			UpdateVisual();
		}

		protected override bool IsDefault(StringBuilder value)
		{
			return value.IsNullOrWhitespace();
		}

		private void EnqueueSend(long blockId)
		{
			traceLog("entered");

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
			traceLog("entered");

			if (_waitUntil > Globals.UpdateCount)
				return;

			foreach (long block in _updatedBlocks)
				SendValue(block, GetValue(block));

			UpdateManager.Unregister(10, Update10);
			_updatedBlocks = null;
		}

		private void UpdateVisual()
		{
			_control?.UpdateVisual();
		}

	}
}
