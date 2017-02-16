using Sandbox.ModAPI;
using VRage.Game.ModAPI;

namespace Rynchodon.Attached
{
	public static class StatorRotor
	{
		public class Stator : AttachableBlockUpdate
		{
			public Stator(IMyCubeBlock block)
				: base(block, AttachedGrid.AttachmentKind.Motor)
			{ }

			protected override IMyCubeBlock GetPartner()
			{
				IMyMotorBase block = (IMyMotorBase)myBlock;
				if (block.IsAttached)
					return block.Top;
				return null;
			}
		}
	}
}
