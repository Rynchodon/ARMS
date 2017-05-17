using System;
using System.Collections.Generic;
using Rynchodon.Utility;
using VRage.Collections;
using VRageMath;

namespace Rynchodon.Autopilot.Pathfinding
{

	struct PathNode
	{
		/// <summary>The key of the parent to this node.</summary>
		public long ParentKey;
		/// <summary>Distance from start to this node.</summary>
		public float DistToCur;
		/// <summary>The position of this node, relative to obstruction.</summary>
		public Vector3D Position;
		/// <summary>The direction from parent to this node.</summary>
		public Vector3 DirectionFromParent;

		/// <summary>The position hash for this node, used for indexing.</summary>
		public long Key { get { return Position.GetHash(); } }

		public PathNode(ref PathNode parent, ref Vector3D position)
		{
			Profiler.StartProfileBlock();
			this.ParentKey = parent.Position.GetHash();
			this.Position = position;
			Vector3D disp; Vector3D.Subtract(ref position, ref parent.Position, out disp);
			this.DirectionFromParent = disp;
			this.DistToCur = parent.DistToCur + this.DirectionFromParent.Normalize();
			Profiler.EndProfileBlock();
		}
	}

	abstract class PathNodeSet : IComparable<PathNodeSet>
	{
		/// <summary>Nodes will be a discrete distance from this position. Relative to obstruction.</summary>
		public Vector3D m_referencePosition;
		/// <summary>Where this set starts from. Relative to obstruction.</summary>
		public Vector3D m_startPosition;
		/// <summary>PathNodeSet that this PathNodeSet is trying to reach.</summary>
		public IEnumerable<PathNodeSet> m_targets;

		public abstract IEnumerable<Vector3D> BlueSkyNodes { get; }

		public abstract int CompareTo(PathNodeSet other);
		public abstract bool HasReached(long key);
		public abstract bool TryGetReached(long key, out PathNode reached);
		public abstract void Setup(ref Vector3D referencePosition, ref Vector3D startPosition, bool m_canChangeCourse, int maxNodeDistance);
	}

	//class RootNode : PathNodeSet
	//{
	//	public override IEnumerable<Vector3D> BlueSkyNodes { get { yield break; } }

	//	public override int CompareTo(PathNodeSet other) { throw new NotImplementedException(); }

	//	public override bool HasReached(long key) { return m_startPosition.GetHash() == key; }

	//	public override void Setup(ref Vector3D referencePosition, ref Vector3D startPosition, bool m_canChangeCourse)
	//	{
	//		m_referencePosition = referencePosition;
	//		m_startPosition = startPosition;
	//	}

	//	public override bool TryGetReached(long key, out PathNode reached)
	//	{
	//		if (m_startPosition.GetHash() == key)
	//		{
	//			reached = new PathNode() { Position = m_startPosition };
	//			return true;
	//		}

	//		reached = default(PathNode);
	//		return false;
	//	}
	//}

	class FindingSet : PathNodeSet
	{
		public const int MaxOpenNodes = 1024;
		public const int DefaultNodeDistance = 128;
		public const int MinNodeDistance = 2; // needs to be greater than the minimum distance for poping a waypoint
		public const float TurnPenalty = 100f;

		public static double MinPathDistance(ref Vector3D to, ref Vector3D from)
		{
			Vector3D displacement; Vector3D.Subtract(ref to, ref from, out displacement);
			return MinPathDistance(ref displacement);
		}

		public static double MinPathDistance(ref Vector3D displacement)
		{
			double X = Math.Abs(displacement.X), Y = Math.Abs(displacement.Y), Z = Math.Abs(displacement.Z), temp;

			// sort so that X is min and Z is max

			if (Y < X)
			{
				temp = X;
				X = Y;
				Y = temp;
			}
			if (Z < Y)
			{
				temp = Y;
				Y = Z;
				Z = temp;
				if (Y < X)
				{
					temp = X;
					X = Y;
					Y = temp;
				}
			}

			return X * MathHelper.Sqrt3 + (Y - X) * MathHelper.Sqrt2 + Z - Y;
		}

		/// <summary>Nodes that have not been testing for reachable, sorted by estimated distance to target set. Use AddOpenNode to add nodes.</summary>
		public MyBinaryStructHeap<float, PathNode> m_openNodes;
		/// <summary>Nodes that have been reached, indexed by position hash or "Key".</summary>
		public Dictionary<long, PathNode> m_reachedNodes;
		/// <summary>Nodes that have been reached that are not near anything.</summary>
		public List<Vector3D> m_blueSkyNodes;
		public bool Failed { get { return NodeDistance == MinNodeDistance && m_openNodes.Count == 0; } }
		private Logable Log {
			get {
				return new Logable(
					ResourcePool<FindingSet>.InstancesCreated.ToString(),
					m_referencePosition.ToString() + ":" + m_startPosition.ToString(),
					string.Concat(m_blueSkyNodes.Count, ':', m_reachedNodes.Count, ':', m_openNodes.Count));
			}
		}
#if PROFILE
			public int m_unreachableNodes;
#endif

		/// <summary>Affects the distance between path nodes and their children when creating new path nodes. Before the start node is processed, it will be DefaultNodeDistance * 2.</summary>
		public int NodeDistance { get; private set; }

		public override IEnumerable<Vector3D> BlueSkyNodes { get { return m_blueSkyNodes; } }

		public FindingSet()
		{
			m_openNodes = new MyBinaryStructHeap<float, PathNode>(MaxOpenNodes);
			m_reachedNodes = new Dictionary<long, PathNode>();
			m_blueSkyNodes = new List<Vector3D>();
			NodeDistance = DefaultNodeDistance << 1;
#if PROFILE
				m_unreachableNodes = 0;
#endif
		}

		public void Clear()
		{
			m_openNodes.Clear();
			m_reachedNodes.Clear();
			m_blueSkyNodes.Clear();
			NodeDistance = DefaultNodeDistance << 1;
			m_targets = null;
#if PROFILE
				m_unreachableNodes = 0;
#endif
		}

		/// <summary>
		/// Compares this PathNodeSet to another for the purposes of scheduling.
		/// </summary>
		/// <param name="other">The object to compare this to.</param>
		/// <returns>A negative integer, zero, or a positive integer, indicating scheduling priority.</returns>
		public override int CompareTo(PathNodeSet other)
		{
			FindingSet otherFS = (FindingSet)other;

			if (this.Failed)
				return 1;
			else if (otherFS.Failed)
				return -1;

			int value = m_blueSkyNodes.Count - otherFS.m_blueSkyNodes.Count;
			if (value != 0)
				return value;

			value = m_reachedNodes.Count - otherFS.m_reachedNodes.Count;
			if (value != 0)
				return value;

			return 0;
		}

		public override void Setup(ref Vector3D reference, ref Vector3D start, bool canChangeCourse, int maxNodeDistance)
		{
			Clear();
			m_referencePosition = reference;
			m_startPosition = start;
			PathNode firstNode = new PathNode() { Position = start };
			if (!canChangeCourse)
			{
				Vector3D direction; Vector3D.Subtract(ref reference, ref start, out direction);
				direction.Normalize();
				firstNode.DirectionFromParent = direction;
			}
			AddOpenNode(ref firstNode, 0f);
			m_reachedNodes.Add(firstNode.Key, firstNode);
			if (maxNodeDistance < MinNodeDistance)
				maxNodeDistance = MinNodeDistance;
			while (NodeDistance > maxNodeDistance)
				NodeDistance = NodeDistance >> 1;
			Log.DebugLog("Finished setup. reference: " + reference + ", start: " + start + ", NodeDistance: " + NodeDistance, Logger.severity.DEBUG);
		}

		/// <summary>
		/// Add a node to m_openNodes.
		/// </summary>
		/// <param name="node">The newly created node.</param>
		/// <param name="estimatedDistance">Distance from start to node + estimated distance to target.</param>
		public void AddOpenNode(ref PathNode node, float estimatedDistance)
		{
			if (m_openNodes.Count == MaxOpenNodes)
				m_openNodes.RemoveMax();
			m_openNodes.Insert(node, estimatedDistance);
		}

		/// <summary>
		/// Change the distance between reached nodes and their children. When doubling, m_openNodes will be rebuilt.
		/// New children will be created and added to the open list for every reached node.
		/// </summary>
		/// <param name="halve">When true, NodeDistance will be halved. When false, NodeDistance will be doubled.</param>
		public void ChangeNodeDistance(bool halve, bool canChangeCourse)
		{
			if (!halve)
				m_openNodes.Clear();

			if (halve)
			{
				NodeDistance = NodeDistance >> 1;
				Log.DebugLog("Halved node distance, now: " + NodeDistance);
			}
			else
			{
				NodeDistance = NodeDistance << 1;
				Log.DebugLog("Doubled node distance, now: " + NodeDistance);
			}

			foreach (PathNode reachedNode in m_reachedNodes.Values)
			{
				PathNode reachedNode2 = reachedNode;
				if (canChangeCourse)
					CreatePathNode(ref reachedNode2);
				else
					CreatePathNodeLine(ref reachedNode2);
			}
		}

		public void CreatePathNode(PathNode currentNode, bool canChangeCourse)
		{
			if (canChangeCourse)
				CreatePathNode(ref currentNode);
			else
				CreatePathNodeLine(ref currentNode);
		}

		public void CreatePathNode(ref PathNode currentNode, bool canChangeCourse)
		{
			if (canChangeCourse)
				CreatePathNode(ref currentNode);
			else
				CreatePathNodeLine(ref currentNode);
		}

		/// <summary>
		/// Create path nodes around the specified node and add them to open list. Do not use if pathfinder cannot change course.
		/// </summary>
		/// <param name="currentNode">The node that will be the parent to the new nodes.</param>
		private void CreatePathNode(ref PathNode currentNode)
		{
			long currentKey = currentNode.Position.GetHash();
			foreach (Vector3I neighbour in Globals.NeighboursOne)
				CreatePathNode(currentKey, ref currentNode, neighbour, 1f);
			foreach (Vector3I neighbour in Globals.NeighboursTwo)
				CreatePathNode(currentKey, ref currentNode, neighbour, MathHelper.Sqrt2);
			foreach (Vector3I neighbour in Globals.NeighboursThree)
				CreatePathNode(currentKey, ref currentNode, neighbour, MathHelper.Sqrt3);
		}

		/// <summary>
		/// Create a path node from a parent.
		/// </summary>
		/// <param name="parentKey">The key of the parent's postion.</param>
		/// <param name="parent">The parent of the new node.</param>
		/// <param name="neighbour">The cell direction of the new node.</param>
		/// <param name="distMulti">Multiplied by NodeDistance to get the distance the new node will be from its parent.</param>
		private void CreatePathNode(long parentKey, ref PathNode parent, Vector3I neighbour, float distMulti)
		{
			Profiler.StartProfileBlock();

			Vector3D position = parent.Position + neighbour * NodeDistance * distMulti;

			// round position so that is a discrete number of steps from m_referencePosition
			Vector3D finishToPosition; Vector3D.Subtract(ref position, ref m_referencePosition, out finishToPosition);
			VectorExtensions.RoundTo(ref finishToPosition, NodeDistance);
			Vector3D.Add(ref m_referencePosition, ref finishToPosition, out position);

			Log.DebugLog("m_reachedNodes == null", Logger.severity.FATAL, condition: m_reachedNodes == null);

			if (m_reachedNodes.ContainsKey(position.GetHash()))
			{
				Profiler.EndProfileBlock();
				return;
			}

			PathNode result = new PathNode(ref parent, ref position);

			float turn; Vector3.Dot(ref parent.DirectionFromParent, ref result.DirectionFromParent, out turn);

			if (turn < 0f)
			{
				Profiler.EndProfileBlock();
				return;
			}

			double distToDest = double.MaxValue;
			foreach (PathNodeSet target in m_targets)
			{
				double dist = MinPathDistance(ref target.m_startPosition, ref position);
				if (dist < distToDest)
					distToDest = dist;
			}

			float resultKey = result.DistToCur + (float)distToDest;

			if (turn > 0.99f && parent.ParentKey != 0f)
			{
				parentKey = parent.ParentKey;
			}
			else
			{
				if (turn < 0f)
				{
					Profiler.EndProfileBlock();
					return;
				}
				resultKey += TurnPenalty * (1f - turn);
			}

			//Log.DebugLog("DirectionFromParent is incorrect. DirectionFromParent: " + result.DirectionFromParent + ", parent: " + parent.Position + ", current: " + result.Position + ", direction: " +
			//	Vector3.Normalize(result.Position - parent.Position), Logger.severity.ERROR, condition: !Vector3.Normalize(result.Position - parent.Position).Equals(result.DirectionFromParent, 0.01f));
			//Log.DebugLog("Length is incorrect. Length: " + (result.DistToCur - parent.DistToCur) + ", distance: " + Vector3D.Distance(result.Position, parent.Position), Logger.severity.ERROR,
			//	condition: Math.Abs((result.DistToCur - parent.DistToCur) - (Vector3D.Distance(result.Position, parent.Position))) > 0.01f);

			//Log.DebugLog("resultKey <= 0", Logger.severity.ERROR, condition: resultKey <= 0f);
			AddOpenNode(ref result, resultKey);
			Profiler.EndProfileBlock();
		}

		/// <summary>
		/// Create a PathNode between parent and destination. Only for forward search.
		/// </summary>
		/// <param name="parent">The node that will be the parent to the new node.</param>
		private void CreatePathNodeLine(ref PathNode parent)
		{
			Vector3D direction = parent.DirectionFromParent;
			Vector3D disp; Vector3D.Multiply(ref direction, NodeDistance, out disp);
			Vector3D position; Vector3D.Add(ref parent.Position, ref disp, out position);

			PathNode result = new PathNode()
			{
				DirectionFromParent = parent.DirectionFromParent,
				DistToCur = parent.DistToCur + NodeDistance,
				ParentKey = parent.Key,
				Position = position
			};
			// do not bother with key as there will only be one open node
			AddOpenNode(ref result, 0f);
		}

		public override bool HasReached(long key)
		{
			return m_reachedNodes.ContainsKey(key);
		}

		public override bool TryGetReached(long key, out PathNode reached)
		{
			return m_reachedNodes.TryGetValue(key, out reached);
		}

	} // class
}
