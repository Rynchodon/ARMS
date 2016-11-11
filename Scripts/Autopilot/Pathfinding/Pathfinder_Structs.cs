using System;
using Rynchodon.Utility.Collections;
using VRage.Game.Entity;
using VRageMath;

namespace Rynchodon.Autopilot.Pathfinding
{
	public partial class Pathfinder
	{

		/// <summary>
		/// To keep autopilot from matching speed with a door while using the line command.
		/// </summary>
		private struct Obstruction : IEquatable<Obstruction>
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

			public bool Equals(Obstruction other)
			{
				return Entity == other.Entity && MatchPosition == other.MatchPosition;
			}
		}

		private struct Path
		{
			/// <summary>Head will be at the last reached position and tail will be at the final destination.</summary>
			public Deque<Vector3D> m_positions;
			/// <summary>The index of the position autopilot is trying to reach.</summary>
			public int m_target;

			public int Count { get { return m_positions.Count; } }

			public bool HasReached { get { return m_positions.Count != 0; } }

			public bool HasTarget { get { return m_target != 0; } }

			public bool IsFinished { get { return m_positions.Count == 1; } }

			public Path(bool nothing)
			{
				m_positions = new Deque<Vector3D>();
				m_target = 0;
			}

			public void AddFront(ref Vector3D position)
			{
				m_positions.AddHead(ref position);
			}

			public void AddBack(ref Vector3D position)
			{
				m_positions.AddTail(ref position);
			}

			public void Clear()
			{
				m_positions.Clear();
				m_target = 0;
			}

			public void GetReached(out Vector3D position)
			{
				m_positions.PeekHead(out position);
			}

			public Vector3D GetReached()
			{
				return m_positions.PeekHead();
			}

			public void GetTarget(out Vector3D position)
			{
				Logger.DebugLog("m_target has not been set", Logger.severity.ERROR, condition: m_target == 0);
				position = m_positions[m_target];
			}

			public Vector3D GetTarget()
			{
				Logger.DebugLog("m_target has not been set", Logger.severity.ERROR, condition: m_target == 0);
				return m_positions[m_target];
			}

			public void ReachedTarget()
			{
				Logger.DebugLog("m_target has not been set", Logger.severity.ERROR, condition: m_target == 0);
				for (int i = 0; i < m_target; i++)
					m_positions.RemoveHead();
				m_target = 0;
			}

		}

	}
}
