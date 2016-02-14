using Rynchodon.Attached;
using Sandbox.ModAPI;

namespace Rynchodon.AntennaRelay
{
	/// <summary>
	/// Block that is a partial participant in a network, only connects to a single node.
	/// </summary>
	public class NetworkClient
	{

		private const ulong UpdateFrequency = 10ul;

		private readonly IMyCubeBlock m_block;

		private NetworkNode value_node;
		private ulong m_nextNodeSet;

		private NetworkNode m_node
		{
			get
			{
				if (Globals.UpdateCount < m_nextNodeSet)
					return value_node;

				if (value_node == null || !IsConnected(value_node))
				{
					value_node = null;
					Registrar.ForEach<NetworkNode>(node => {
						if (node.Block != null && IsConnected(node))
						{
							value_node = node;
							return true;
						}
						return false;
					});
				}

				m_nextNodeSet = Globals.UpdateCount + UpdateFrequency;
				return value_node;
			}
		}

		public NetworkStorage Storage
		{
			get
			{
				NetworkNode node = m_node;
				if (node == null)
					return null;
				return node.Storage;
			}
		}

		public NetworkClient(IMyCubeBlock block)
		{
			this.m_block = block;
		}

		private bool IsConnected(NetworkNode node)
		{
			return !node.Block.Closed && m_block.canConsiderFriendly(node.Block) && AttachedGrid.IsGridAttached(m_block.CubeGrid, node.Block.CubeGrid, AttachedGrid.AttachmentKind.Terminal);
		}

	}
}
