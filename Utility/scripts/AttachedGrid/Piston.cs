using System;
using Sandbox.Common.ObjectBuilders;
using Sandbox.ModAPI;

namespace Rynchodon.AttachedGrid
{
	public static class Piston
	{
		public class PistonBase : AttachableBlock
		{
			private readonly Logger myLogger;

			public PistonBase(IMyCubeBlock block)
				: base(block)
			{
				myLogger = new Logger("PistonBase", block);
				AttachmentKind = AttachedGrid.AttachmentKind.Piston;
			}

			protected override AttachableBlock GetPartner(AttachableBlock current)
			{
				var builder = myBlock.GetSlimObjectBuilder_Safe() as MyObjectBuilder_ExtendedPistonBase;
				if (builder == null)
					throw new NullReferenceException("builder");
				return GetPartner(builder.TopBlockId);
			}
		}

		public class PistonTop : AttachableBlock
		{
			private readonly Logger myLogger;

			public PistonTop(IMyCubeBlock block)
				: base(block)
			{
				myLogger = new Logger("PistonTop", block);
				AttachmentKind = AttachedGrid.AttachmentKind.Piston;
			}

			protected override AttachableBlock GetPartner(AttachableBlock current)
			{
				var builder = myBlock.GetSlimObjectBuilder_Safe() as MyObjectBuilder_PistonTop;
				if (builder == null)
					throw new NullReferenceException("builder");
				return GetPartner(builder.PistonBlockId);
			}
		}
	}
}
