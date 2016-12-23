using Sandbox.ModAPI;
using VRage.Game.ModAPI;

namespace Rynchodon.Attached
{
	public static class StatorRotor
	{
		/// <summary>
		/// Tries to get a rotor attached to a stator.
		/// </summary>
		/// <param name="stator">stator attached to rotor</param>
		/// <param name="rotor">rotor attached to stator</param>
		/// <returns>true iff successful</returns>
		/// Not an extension because TryGetStator() is not an extension.
		public static bool TryGetRotor(IMyMotorStator stator, out IMyCubeBlock rotor)
		{
			rotor = stator.Rotor;
			return rotor != null;
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
			stator = ((IMyMotorRotor)rotor).Stator as IMyMotorStator;
			return stator != null;
		}

		public class Stator : AttachableBlockUpdate
		{
			public Stator(IMyCubeBlock block)
				: base(block, AttachedGrid.AttachmentKind.Motor)
			{ }

			protected override IMyCubeBlock GetPartner()
			{
				IMyMotorBase block = (IMyMotorBase)myBlock;
				if (block.IsAttached)
					return block.Rotor;
				return null;
			}
		}
	}
}
