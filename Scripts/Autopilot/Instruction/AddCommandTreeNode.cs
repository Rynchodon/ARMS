using System.Linq;
using Rynchodon.Autopilot.Instruction.Command;

namespace Rynchodon.Autopilot.Instruction
{
	public abstract class AddCommandTreeNode
	{

		public readonly string Name, Tooltip;

		protected AddCommandTreeNode(string name, string tooltip)
		{
			this.Name = name;
			this.Tooltip = tooltip;
		}

	}

	public class AddCommandInternalNode : AddCommandTreeNode
	{

		public readonly AddCommandTreeNode[] Children;

		public AddCommandInternalNode(string name, AddCommandTreeNode[] children)
			: base(name, string.Join("/", children.Select(child => child.Name)))
		{
			this.Children = children;
		}

	}

	public class AddCommandLeafNode : AddCommandTreeNode
	{

		public readonly ACommand Command;

		public AddCommandLeafNode(ACommand command)
			: base(command.AddName, command.AddDescription)
		{
			this.Command = command;
		}

	}

}
