#if DEBUG
#define TRACE
#endif

using Sandbox.ModAPI.Interfaces.Terminal;

namespace Rynchodon.Utility.Network
{
	/// <summary>
	/// Objects of this type synchronize and save terminal values.
	/// </summary>
	/// <typeparam name="TValue">The type of value</typeparam>
	/// <typeparam name="TScript">The script that contains the value</typeparam>
	public abstract class ATerminalValueSync<TValue, TScript> : AValueSync<TValue, TScript>
	{

		private readonly IMyTerminalValueControl<TValue> _control;

		protected ATerminalValueSync(IMyTerminalValueControl<TValue> control, GetterDelegate getter, SetterDelegate setter, bool save = true)
			: base(control.Id, getter, setter, save)
		{
			traceLog("entered");

			_control = control;

			control.Getter = GetValue;
			control.Setter = SetValue;
		}

		protected void UpdateVisual()
		{
			if (_control is IMyTerminalControl)
				((IMyTerminalControl)_control).UpdateVisual();
		}

	}
}
