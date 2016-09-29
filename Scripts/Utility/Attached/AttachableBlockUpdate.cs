using VRage.Game.ModAPI;

namespace Rynchodon.Attached
{
	public abstract class AttachableBlockUpdate : AttachableBlockBase
	{
		//private static readonly Logger staticLogger = new Logger("AttachableBlock");

		private readonly Logger myLogger;

		public AttachableBlockUpdate(IMyCubeBlock block, AttachedGrid.AttachmentKind kind)
			: base(block, kind)
		{
			this.myLogger = new Logger(block);
			//Registrar.Add(this.myBlock, this);
		}

		public void Update()
		{
			AttachableBlockBase partner = GetPartner();
			if (partner == null)
				Detach();
			else
				Attach(partner.myBlock);
		}

		public override string ToString()
		{ return "AttachableBlock:" + myBlock.DisplayNameText; }

		protected abstract AttachableBlockBase GetPartner();
	}
}
