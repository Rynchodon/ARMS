
using Sandbox.ModAPI;
using Ingame = Sandbox.ModAPI.Ingame;

namespace Rynchodon
{
	public class PanelInstructions
	{

		public readonly Ingame.IMyTextPanel Block;
		public readonly string PublicText;


		public PanelInstructions(Ingame.IMyTextPanel block)
		{
			Block = block;
			PublicText = Block.GetPublicText();
		}

		public bool PublicTextChanged()
		{
			return PublicText != Block.GetPublicText();
		}

	}
}
