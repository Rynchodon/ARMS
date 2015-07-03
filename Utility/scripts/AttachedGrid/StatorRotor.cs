using System.Collections.Generic;
using Sandbox.Common.ObjectBuilders;
using Sandbox.ModAPI;
using VRage.ModAPI;

namespace Rynchodon.AttachedGrid
{
	public static class StatorRotor
	{
		private static readonly Logger myLogger = new Logger("StatorRotor");

		/// <summary>
		/// Tries to get a rotor attached to a stator.
		/// </summary>
		/// <param name="stator">stator attached to rotor</param>
		/// <param name="rotor">rotor attached to stator</param>
		/// <returns>true iff successful</returns>
		/// Not an extension because TryGetStator() is not an extension.
		public static bool TryGetRotor(IMyMotorStator stator, out IMyCubeBlock rotor)
		{
			Stator value;
			if (!Stator.registry.TryGetValue(stator, out value))
			{
				myLogger.alwaysLog("failed to get stator from registry: " + stator.DisplayNameText, "TryGetRotor()", Logger.severity.WARNING);
				rotor = null;
				return false;
			}
			if (value.partner == null)
			{
				rotor = null;
				return false;
			}
			rotor = value.partner.myRotor;
			return true;
		}

		/// <summary>
		/// Tries to get a stator attached to a rotor.
		/// </summary>
		/// <param name="rotor">rotor attached to stator</param>
		/// <param name="stator">stator attached to rotor</param>
		/// <returns>true iff successful</returns>
		/// Not an extension because IMyCubeBlock are rarely rotors.
		public static bool TryGetStator(IMyCubeBlock rotor, out IMyMotorStator stator)
		{
			Rotor value;
			if (!Rotor.registry.TryGetValue(rotor, out value))
			{
				myLogger.alwaysLog("failed to get rotor from registry: " + rotor.DisplayNameText, "TryGetRotor()", Logger.severity.WARNING);
				stator = null;
				return false;
			}
			if (value.partner == null)
			{
				stator = null;
				return false;
			}
			stator = value.partner.myStator;
			return true;
		}

		public class Stator
		{
			internal static Dictionary<IMyMotorStator, Stator> registry = new Dictionary<IMyMotorStator, Stator>();

			internal readonly IMyMotorStator myStator;
			internal Rotor partner;

			private readonly Logger myLogger;

			public Stator(IMyCubeBlock block)
			{
				this.myLogger = new Logger("Stator", block);
				this.myStator = block as IMyMotorStator;
				registry.Add(this.myStator, this);
				this.myStator.OnClosing += myStator_OnClosing;
			}

			private void myStator_OnClosing(IMyEntity obj)
			{
				myStator.OnClosing -= myStator_OnClosing;
				registry.Remove(myStator);
			}

			/// This will not work correctly if a rotor is replaced in less than 10 updates.
			public void Update10()
			{
				if (partner == null)
				{
					if (myStator.IsAttached)
					{
						MyObjectBuilder_MotorStator statorBuilder = (myStator as IMyCubeBlock).GetSlimObjectBuilder_Safe() as MyObjectBuilder_MotorStator; // could this ever be null?
						IMyEntity rotorE;
						if (MyAPIGateway.Entities.TryGetEntityById(statorBuilder.RotorEntityId, out rotorE))
						{
							if (Rotor.registry.TryGetValue((rotorE as IMyCubeBlock), out partner))
							{
								myLogger.debugLog("Set partner to " + partner.myRotor.DisplayNameText, "Update10()", Logger.severity.INFO);
								partner.partner = this;
							}
							else
								myLogger.alwaysLog("Failed to set partner, Rotor not in registry.", "Update10()", Logger.severity.WARNING);
						}
						else
							myLogger.alwaysLog("Failed to set partner, entity not found", "Update10()", Logger.severity.WARNING);
					}
				}
				else // partner != null
					if (!myStator.IsAttached)
					{
						partner.partner = null;
						partner = null;
					}
			}
		}

		public class Rotor
		{
			internal static Dictionary<IMyCubeBlock, Rotor> registry = new Dictionary<IMyCubeBlock, Rotor>();

			internal readonly IMyCubeBlock myRotor;
			internal Stator partner;

			public Rotor(IMyCubeBlock block)
			{
				this.myRotor = block;
				registry.Add(this.myRotor, this);
				this.myRotor.OnClosing += myRotor_OnClosing;
			}

			private void myRotor_OnClosing(IMyEntity obj)
			{
				myRotor.OnClosing -= myRotor_OnClosing;
				registry.Remove(myRotor);
			}
		}
	}
}
