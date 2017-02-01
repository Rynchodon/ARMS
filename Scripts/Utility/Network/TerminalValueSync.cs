#if DEBUG
#define TRACE
#endif

using System.Collections.Generic;
using Sandbox.Game.Entities.Cube;
using Sandbox.Game.Screens.Terminal.Controls;

namespace Rynchodon.Utility.Network
{
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
		/// <param name="save">Iff true, save the value to disk.</param>
		public TerminalValueSync(Id id, MyTerminalValueControl<TBlock, TValue> control, GetterDelegate getter, SetterDelegate setter, bool save = true) : base(id, control, getter, setter, save) { }

		protected override void SetValue(long blockId, TScript script, TValue value, bool send)
		{
			_logger.traceLog("entered");

			TValue currentValue = _getter(script);
			if (!EqualityComparer<TValue>.Default.Equals(value, currentValue))
			{
				_logger.traceLog("value changed from " + currentValue + " to " + value);
				_setter(script, value);
				if (send)
					SendValue(blockId, value);
				_control.UpdateVisual();
			}
			else
				_logger.traceLog("equals previous value");
		}

	}
}
