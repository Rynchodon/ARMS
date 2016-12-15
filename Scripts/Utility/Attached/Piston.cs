using Sandbox.ModAPI;
using VRage.Game.ModAPI;

namespace Rynchodon.Attached
{
	public static class Piston
	{
		public class PistonBase : AttachableBlockUpdate
		{
			public PistonBase(IMyCubeBlock block)
				: base(block, AttachedGrid.AttachmentKind.Piston)
			{ }

			protected override IMyCubeBlock GetPartner()
			{
				IMyPistonBase piston = (IMyPistonBase)myBlock;
				if (piston.IsAttached)
					return piston.Top;
				else
					return null;
			}
		}
	}
}
