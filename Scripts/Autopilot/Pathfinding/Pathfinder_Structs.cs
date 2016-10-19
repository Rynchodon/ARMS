#define PROFILE

using System;
using System.Collections.Generic;
using Rynchodon.Utility;
using Rynchodon.Utility.Collections;
using VRage.Collections;
using VRage.Game.Entity;
using VRageMath;

namespace Rynchodon.Autopilot.Pathfinding
{
	public partial class Pathfinder
	{

		private struct Obstruction
		{
			public MyEntity Entity;
			public bool MatchPosition;

			public Vector3 LinearVelocity
			{
				get
				{
					return MatchPosition && Entity.Physics != null ? Entity.Physics.LinearVelocity : Vector3.Zero;
				}
			}

			public Vector3D GetPosition()
			{
				return MatchPosition ? Entity.PositionComp.GetPosition() : Vector3D.Zero;
			}

			public Vector3D GetCentre()
			{
				return MatchPosition ? Entity.GetCentre() : Vector3D.Zero;
			}
		}

		private struct PathNode
		{
			public long ParentKey;
			public float DistToCur;
			public Vector3D Position;
			public Vector3 DirectionFromParent;

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

		private struct PathNodeSet
		{
			public Vector3D m_startPosition;
			public MyBinaryStructHeap<float, PathNode> m_openNodes;
			public Dictionary<long, PathNode> m_reachedNodes;
			/// <summary>Nodes that are not near anything.</summary>
			public List<Vector3D> m_blueSkyNodes;
#if PROFILE
			public int m_unreachableNodes;
#endif

			public PathNodeSet(bool nothing)
			{
				m_startPosition = default(Vector3D);
				m_openNodes = new MyBinaryStructHeap<float, PathNode>();
				m_reachedNodes = new Dictionary<long, PathNode>();
				m_blueSkyNodes = new List<Vector3D>();
#if PROFILE
				m_unreachableNodes = 0;
#endif
			}

			public void Clear()
			{
				m_startPosition = default(Vector3D);
				m_openNodes.Clear();
				m_reachedNodes.Clear();
				m_blueSkyNodes.Clear();
#if PROFILE
				m_unreachableNodes = 0;
#endif
			}
		}

		private struct Path
		{
			/// <summary>Head will be at the last reached position and tail will be at the final destination.</summary>
			public Deque<Vector3D> m_postions;
			/// <summary>The index of the position autopilot is trying to reach.</summary>
			public int m_target;

			public int Count { get { return m_postions.Count; } }

			public bool HasReached { get { return m_postions.Count != 0; } }

			public bool HasTarget
			{
				get
				{
					Logger.DebugLog("Target should have been set", Logger.severity.ERROR, condition: m_target == 0 && m_postions.Count > 1 );
					return m_target != 0;
				}
			}

			public bool IsFinished { get { return m_postions.Count == 1; } }

			public Path(bool nothing)
			{
				m_postions = new Deque<Vector3D>();
				m_target = 0;
			}

			public void AddFront(ref Vector3D position)
			{
				m_postions.AddHead(ref position);
				Logger.DebugLog("Added front, count: " + m_postions.Count);
			}

			public void AddBack(ref Vector3D position)
			{
				m_postions.AddTail(ref position);
				Logger.DebugLog("Added back, count: " + m_postions.Count);
			}

			public void Clear()
			{
				m_postions.Clear();
				m_target = 0;
				Logger.DebugLog("Cleared");
			}

			public void GetReached(out Vector3D position)
			{
				m_postions.PeekHead(out position);
			}

			public Vector3D GetReached()
			{
				return m_postions.PeekHead();
			}

			public void GetTarget(out Vector3D position)
			{
				Logger.DebugLog("m_target has not been set", Logger.severity.ERROR, condition: m_target == 0);
				position = m_postions[m_target];
			}

			public Vector3D GetTarget()
			{
				Logger.DebugLog("m_target has not been set", Logger.severity.ERROR, condition: m_target == 0);
				return m_postions[m_target];
			}

			public void ReachedTarget()
			{
				Logger.DebugLog("m_target has not been set", Logger.severity.ERROR, condition: m_target == 0);
				for (int i = 0; i < m_target; i++)
					m_postions.RemoveHead();
				Logger.DebugLog("remove " + (m_target) + ", positions");
				m_target = 0;
			}

		}

	}
}
