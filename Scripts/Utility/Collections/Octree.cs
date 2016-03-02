using System; // (partial) from mscorlib.dll
using System.Collections.Generic; // from mscorlib.dll, System.dll, System.Core.dll, and VRage.Library.dll
using VRage.Library.Utils; // from VRage.Library.dll
using VRageMath; // from VRage.Math.dll

namespace Rynchodon
{
	public class Octree<T>
	{

		private struct Node
		{

			public T Value;
			public Node[] Children;

			public bool HasChildren { get { return Children != null; } }

			/// <summary>
			/// Makes this Node a leaf, iff all children are identical leaves.
			/// </summary>
			/// <returns>True iff this node was converted to a leaf node.</returns>
			public bool TryMakeLeaf()
			{
				if (!HasChildren)
					throw new Exception("Children == null");

				if (Children[0].HasChildren)
					return false;
				T value = Children[0].Value;

				for (int i = 1; i < 8; i++)
				{
					if (Children[i].HasChildren)
						return false;
					if (!Children[i].Value.Equals(value))
						return false;
				}

				Value = value;
				return true;
			}

		}

		private readonly float m_minSize;
		private readonly BoundingBox m_box;

		private Node m_root;
		private readonly Stack<Node> m_nodeStack = new Stack<Node>();
		private readonly Stack<int> m_indexStack = new Stack<int>();
		private readonly FastResourceLock m_lock = new FastResourceLock();

		public Octree(BoundingBox range, float minSize = 1f)
		{
			this.m_box = range;
			this.m_minSize = minSize;
		}

		public Octree(ICollection<Vector3> collection, T value, float minSize = 1f)
		{
			this.m_minSize = minSize;

			foreach (Vector3 v in collection)
				m_box.Include(v);

			foreach (Vector3 v in collection)
				SetValue(v, value);
		}

		public T GetValue(Vector3 vector)
		{
			return GetValue(ref vector);
		}

		public T GetValue(ref Vector3 vector)
		{
			using (m_lock.AcquireSharedUsing())
			{
				if (m_box.Contains(vector) == ContainmentType.Disjoint)
					throw new Exception("vector is not within octree");

				Vector3 currentNodePosition = m_box.Center;
				Node currentNode = m_root;
				float side = m_box.GetLongestDim();

				while (currentNode.HasChildren)
				{
					Vector3 octantPosition = vector - currentNodePosition;
					int nodeIndex = 0;

					if (octantPosition.X >= 0)
					{
						octantPosition.X = 1f;
						nodeIndex += 1;
					}
					else
					{
						octantPosition.X = -1f;
					}
					if (octantPosition.Y >= 0)
					{
						octantPosition.Y = 1f;
						nodeIndex += 2;
					}
					else
					{
						octantPosition.Y = -1f;
					}
					if (octantPosition.Z >= 0)
					{
						octantPosition.Z = 1f;
						nodeIndex += 4;
					}
					else
					{
						octantPosition.Z = -1f;
					}

					currentNode = currentNode.Children[nodeIndex];
					side *= 0.5f;
					currentNodePosition = currentNodePosition + octantPosition * side;
				}

				return currentNode.Value;
			}
		}

		public void SetValue(Vector3 vector, T value)
		{
			SetValue(ref vector, value);
		}

		public void SetValue(ref Vector3 vector, T value)
		{
			using (m_lock.AcquireExclusiveUsing())
			{
				if (m_box.Contains(vector) == ContainmentType.Disjoint)
					throw new Exception("Need to resize tree, not implemented");

				float side = m_box.GetLongestDim();

				if (side <= m_minSize)
				{
					m_root.Value = value;
					return;
				}

				Vector3 currentNodePosition = m_box.Center;
				if (!m_root.HasChildren)
					m_root.Children = new Node[8];
				m_nodeStack.Push(m_root);

				while (side > m_minSize)
				{
					Vector3 octantPosition = vector - currentNodePosition;
					int nodeIndex = 0;

					if (octantPosition.X >= 0)
					{
						octantPosition.X = 1f;
						nodeIndex += 1;
					}
					else
					{
						octantPosition.X = -1f;
					}
					if (octantPosition.Y >= 0)
					{
						octantPosition.Y = 1f;
						nodeIndex += 2;
					}
					else
					{
						octantPosition.Y = -1f;
					}
					if (octantPosition.Z >= 0)
					{
						octantPosition.Z = 1f;
						nodeIndex += 4;
					}
					else
					{
						octantPosition.Z = -1f;
					}

					m_indexStack.Push(nodeIndex);
					Node currentNode = m_nodeStack.Peek();
					if (currentNode.HasChildren)
						m_nodeStack.Push(currentNode.Children[nodeIndex]);
					else
						m_nodeStack.Push(new Node());

					side *= 0.5f;
					currentNodePosition = currentNodePosition + octantPosition * side;
				}

				Node child = m_nodeStack.Pop();
				child.Value = value;
				bool setChild = true, makeLeaf = true;

				while (m_nodeStack.Count != 0)
				{
					Node parent = m_nodeStack.Pop();

					if (setChild)
					{
						if (parent.HasChildren)
							setChild = false;
						else
							parent.Children = new Node[8];
						parent.Children[m_indexStack.Pop()] = child;
						child = parent;
					}

					if (!setChild)
					{
						if (makeLeaf && !parent.TryMakeLeaf())
							makeLeaf = false;

						if (!makeLeaf)
						{
							m_nodeStack.Clear();
							m_indexStack.Clear();
							return;
						}
					}
				}
			}
		}

		/// <summary>
		/// Recursively counts nodes.
		/// </summary>
		/// <returns>The number of nodes in this octree</returns>
		public int CountNodes()
		{
			return CountNodes(m_root);
		}

		private int CountNodes(Node n)
		{
			if (n.HasChildren)
			{
				int count = 0;
				foreach (Node c in n.Children)
					count += CountNodes(c);
				return count;
			}
			else
				return 1;
		}

	}
}
