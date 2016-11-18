// points are only shown with Debug build
//#define SHOW_PATH // show the path found with GPS
//#define SHOW_REACHED // show points reached with GPS

using System;
using System.Collections.Generic;
using Rynchodon.Utility;
using Sandbox.Game.Entities;
using Sandbox.ModAPI;
using VRage.Game.Entity;
using VRage.Game.ModAPI;
using VRageMath;

namespace Rynchodon.Autopilot.Pathfinding
{
	public partial class Pathfinder
	{

		public enum State : byte
		{
			None, Crashed, Unobstructed, SearchingForPath, FollowingPath, FailedToFindPath
		}

		private Path m_path = new Path(false);
		private FindingSet m_forward;
		/// <summary>Indecies allign with m_destinations.</summary>
		private PathNodeSet[] m_backwardList;
#if PROFILE
		private DateTime m_timeStartPathfinding;
#endif

		private State value_state;
		public State CurrentState
		{
			get { return value_state; }
			set
			{
				if (m_navSetChange)
					value = State.None;
				if (value_state == value)
					return;

				if (value == State.SearchingForPath)
				{
					ResourcePool.Get(out m_forward);
				}
				else if (value_state == State.SearchingForPath)
				{
					m_waitUntil = 0uL;
					m_forward.Clear();
					ResourcePool.Return(m_forward);
					m_forward = null;
					ClearBackwards();
				}

				value_state = value;
			}
		}

		#region Setup

		private void StartPathfinding()
		{
			m_path.Clear();
#if SHOW_REACHED
			PurgeTempGPS();
#endif
			CurrentState = State.SearchingForPath;

			if (m_forward == null)
			{
				m_logger.debugLog("interrupted");
				return;
			}

			int maxNodeDistance = (int)NavSet.Settings_Current.Distance >> 2;

			Vector3D referencePosition;
			SetupBackward(out referencePosition, maxNodeDistance);
			SetupForward(ref referencePosition, maxNodeDistance);

			IEnumerable<PathNodeSet> backTargets = m_forward.ToEnumerable();

			foreach (PathNodeSet pns in m_backwardList)
				pns.m_targets = backTargets;
			m_forward.m_targets = m_backwardList;
		}

		private void SetupBackward(out Vector3D referencePosition, int maxNodeDistance)
		{
			referencePosition = default(Vector3D);
			CreateBackwards();

			int index = 0;
			foreach (Destination dest in m_destinations)
			{
				PathNodeSet pns = m_backwardList[index];

				Vector3D startPosition;
				GetFromObsPosition(dest, out startPosition);

				if (index == 0)
					referencePosition = startPosition;

				pns.Setup(ref referencePosition, ref startPosition, m_canChangeCourse, maxNodeDistance);
				index++;
			}

			if (referencePosition == default(Vector3D))
				throw new Exception("no backwards set! destination count: " + m_destinations.Length);
		}

		private void SetupForward(ref Vector3D referencePosition, int maxNodeDistance)
		{
			m_logger.debugLog("m_obstructingEntity.Entity == null", Logger.severity.FATAL, condition: m_obstructingEntity.Entity == null);
			m_logger.debugLog("m_forward == null", Logger.severity.FATAL, condition: m_forward == null);

			Vector3D startPosition;
			Vector3D obstructPosition = m_obstructingEntity.GetPosition();
			Vector3D.Subtract(ref m_currentPosition, ref obstructPosition, out startPosition);

			m_forward.Setup(ref referencePosition, ref startPosition, m_canChangeCourse, maxNodeDistance);
			m_path.AddFront(ref m_forward.m_startPosition);
		}

		private void ClearBackwards()
		{
			if (m_backwardList == null)
				return;

			for (int i = 0; i < m_backwardList.Length; i++)
			{
				FindingSet back = (FindingSet)m_backwardList[i];
				if (back != null)
				{
					back.Clear();
					ResourcePool.Return(back);
					m_backwardList[i] = null;
				}
			}

			//if (m_backwardList.Length == 1)
			//{
			//	FindingSet back = (FindingSet)m_backwardList[0];
			//	if (back == null)
			//		return;
			//	back.Clear();
			//	ResourcePool.Return(back);
			//	m_backwardList[0] = null;
			//}
			//else
			//	for (int i = 0; i < m_backwardList.Length; i++)
			//		m_backwardList[i] = null;
		}

		private void CreateBackwards()
		{
			if (m_backwardList != null)
				if (m_backwardList.Length == m_destinations.Length)
				{
					for (int i = 0; i < m_backwardList.Length; i++)
						if (m_backwardList[i] == null)
							m_backwardList[i] = new FindingSet();
						else
							((FindingSet)m_backwardList[i]).Clear();
					return;
				}
				else
					ClearBackwards();

			m_backwardList = new PathNodeSet[m_destinations.Length];

			for (int i = 0; i < m_backwardList.Length; i++)
				if (m_backwardList[i] == null)
					m_backwardList[i] = new FindingSet();

			//if (m_backwardList != null && m_backwardList.Length == 1)
			//{
			//	FindingSet back = (FindingSet)m_backwardList[0];
			//	if (back != null)
			//	{
			//		back.Clear();
			//		if (m_destinations.Length == 1)
			//			return;
			//		ResourcePool.Return(back);
			//		m_backwardList[0] = null;
			//	}
			//}

			//if (m_backwardList == null || m_backwardList.Length != m_destinations.Length)
			//	m_backwardList = new PathNodeSet[m_destinations.Length];

			//if (m_backwardList.Length == 1)
			//{
			//	if (m_backwardList[0] == null)
			//		m_backwardList[0] = ResourcePool<FindingSet>.Get();
			//}
			//else
			//	for (int i = 0; i < m_backwardList.Length; i++)
			//		if (m_backwardList[i] == null)
			//			m_backwardList[i] = new RootNode();
		}

		private void GetFromObsPosition(Destination dest, out Vector3D fromObsPos)
		{
			Vector3D destWorld = dest.WorldPosition();
			Vector3D obstructPosition = m_obstructingEntity.GetPosition();
			Vector3D.Subtract(ref destWorld, ref obstructPosition, out fromObsPos);

			m_logger.debugLog("destWorld: " + destWorld + ", obstructPosition: " + obstructPosition + ", fromObsPos: " + fromObsPos);
		}

		#endregion
		
		private void ContinuePathfinding()
		{
			if (m_waitUntil > Globals.UpdateCount)
			{
				m_holdPosition = true;
				OnComplete();
				return;
			}

			if (!m_runningLock.TryAcquireExclusive())
				return;
			try
			{
				if (m_runHalt || m_runInterrupt)
					return;
				FillDestWorld();

				if (!m_canChangeCourse)
					ContinuePathfinding(m_forward);
				else
				{
					PathNodeSet bestSet = null;
					foreach (PathNodeSet backSet in m_backwardList)
						if (bestSet == null || backSet.CompareTo(bestSet) < 0)
							bestSet = backSet;
					FindingSet fs = (FindingSet)bestSet;
					if (fs.Failed)
					{
						if (!MoveToArbitrary())
							ContinuePathfinding(m_forward);
					}
					else
					{
						if (m_forward.CompareTo(bestSet) < 0)
							fs = m_forward;
						ContinuePathfinding(fs);
					}
				}
				OnComplete();
			}
			catch
			{
				CurrentState = State.Crashed;
				throw;
			}
			finally { m_runningLock.ReleaseExclusive(); }

			PostRun();
		}

		/// <summary>
		/// Continues pathfinding.
		/// </summary>
		private void ContinuePathfinding(FindingSet pnSet)
		{
			//PathNodeSet pnSet = isForwardSet ? m_forward : m_backward;

			m_logger.debugLog("m_obstructingEntity == null", Logger.severity.ERROR, condition: m_obstructingEntity.Entity == null, secondaryState: SetName(pnSet));

			if (pnSet.m_openNodes.Count == 0)
			{
				OutOfNodes(pnSet);
				return;
			}

			PathNode currentNode = pnSet.m_openNodes.RemoveMin();
			if (currentNode.DistToCur == 0f)
			{
				m_logger.debugLog("first node: " + ReportRelativePosition(currentNode.Position), secondaryState: SetName(pnSet));
				pnSet.CreatePathNode(ref currentNode, m_canChangeCourse);
				return;
			}

			if (pnSet.HasReached(currentNode.Position.GetHash()))
			{
				//m_logger.debugLog("Already reached: " + ReportRelativePosition(currentNode.Position), secondaryState: SetName(direction));
				return;
			}

			FillEntitiesLists();
			Vector3 repulsion;
			CalcRepulsion(false, out repulsion);
			m_logger.debugLog("Calculated repulsion for some reason: " + repulsion, Logger.severity.WARNING, condition: repulsion != Vector3.Zero, secondaryState: SetName(pnSet));

			Vector3D obstructPosition = m_obstructingEntity.GetPosition();
			PathNode parent;
			if (!pnSet.m_reachedNodes.TryGetValue(currentNode.ParentKey, out parent))
			{
				m_logger.debugLog("Failed to get parent", Logger.severity.WARNING);
				return;
			}
			Vector3D worldParent;
			Vector3D.Add(ref obstructPosition, ref parent.Position, out worldParent);
			Vector3D offset; Vector3D.Subtract(ref worldParent, ref m_currentPosition, out offset);

			if (!CanTravelSegment(ref offset, ref currentNode.DirectionFromParent, currentNode.DistToCur - parent.DistToCur))
			{
#if PROFILE
				pnSet.m_unreachableNodes++;
#endif
				//m_logger.debugLog("Not reachable: " + ReportRelativePosition(currentNode.Position), secondaryState: SetName(pnSet));
				return;
			}
			m_logger.debugLog("Reached node: " + ReportRelativePosition(currentNode.Position) + " from " + ReportRelativePosition(pnSet.m_reachedNodes[currentNode.ParentKey].Position) + ", reached: " + pnSet.m_reachedNodes.Count + ", open: " + pnSet.m_openNodes.Count, secondaryState: SetName(pnSet));
			long cNodePosHash = currentNode.Key;
			pnSet.m_reachedNodes.Add(cNodePosHash, currentNode);

			if (!m_canChangeCourse)
			{
				m_logger.debugLog("Running backwards search", Logger.severity.ERROR, condition: pnSet != m_forward, secondaryState: SetName(pnSet));
				Vector3D.Subtract(ref m_currentPosition, ref pnSet.m_referencePosition, out offset);
				if (CanTravelSegment(ref offset, ref currentNode.DirectionFromParent, currentNode.DistToCur - parent.DistToCur))
				{
					m_logger.debugLog("Reached destination from node: " + ReportRelativePosition(currentNode.Position), secondaryState: SetName(pnSet));
					m_logger.debugLog("Backwards start is not reference position", Logger.severity.ERROR, condition: m_backwardList[0].m_startPosition != pnSet.m_referencePosition);
					BuildPath(cNodePosHash, m_backwardList[0], pnSet.m_referencePosition.GetHash());
					return;
				}
#if SHOW_REACHED
				ShowPosition(currentNode, SetName(pnSet));
#endif
				return;
			}

			foreach (PathNodeSet target in pnSet.m_targets)
				if (target.HasReached(cNodePosHash))
				{
					m_logger.debugLog("Opposite set has same position", secondaryState: SetName(pnSet));
					if (pnSet == m_forward)
						BuildPath(cNodePosHash, target, cNodePosHash);
					else
						BuildPath(cNodePosHash, pnSet, cNodePosHash);
					return;
				}

			// blue sky test

			Vector3D currentNodeWorld; Vector3D.Add(ref obstructPosition, ref currentNode.Position, out currentNodeWorld);
			BoundingSphereD sphere = new BoundingSphereD() { Center = currentNodeWorld, Radius = m_autopilotShipBoundingRadius + 100f };
			m_entitiesRepulse.Clear(); // use repulse list as prune/avoid is needed for CanTravelSegment
			MyGamePruningStructure.GetAllTopMostEntitiesInSphere(ref sphere, m_entitiesRepulse);
			if (m_entitiesRepulse.Count == 0)
			{
				//Logger.DebugNotify(SetName(pnSet) + " Blue Sky");
				m_logger.debugLog("Blue sky node: " + ReportRelativePosition(currentNode.Position), secondaryState: SetName(pnSet));

				Vector3D.Subtract(ref currentNodeWorld, ref m_currentPosition, out offset);

				foreach (PathNodeSet target in pnSet.m_targets)
				{
					if (CanTravelSegment(ref offset, ref currentNode.Position, ref target.m_startPosition))
					{
						m_logger.debugLog("Blue sky to opposite start", secondaryState: SetName(pnSet));
						if (pnSet == m_forward)
							BuildPath(cNodePosHash, target, target.m_startPosition.GetHash());
						else
							BuildPath(target.m_startPosition.GetHash(), pnSet, cNodePosHash);
						return;
					}
					foreach (Vector3D targetBlueSky in target.BlueSkyNodes)
						if (CanTravelSegment(ref offset, currentNode.Position, targetBlueSky))
						{
							m_logger.debugLog("Blue sky path", secondaryState: SetName(pnSet));
							if (pnSet == m_forward)
								BuildPath(cNodePosHash, target, targetBlueSky.GetHash());
							else
								BuildPath(targetBlueSky.GetHash(), pnSet, cNodePosHash);
							return;
						}
				}
				pnSet.m_blueSkyNodes.Add(currentNode.Position);
#if SHOW_REACHED
				ShowPosition(currentNode, "Blue Sky " + SetName(pnSet));
#endif
				return;
			}

#if SHOW_REACHED
			ShowPosition(currentNode, SetName(pnSet));
#endif

			if (pnSet.NodeDistance < FindingSet.DefaultNodeDistance && pnSet.m_reachedNodes.Count % 10 == 0 && pnSet.m_openNodes.Count > 100)
			{
				m_logger.debugLog("Reached: " + pnSet.m_reachedNodes.Count + ", trying with higher node distance");
				pnSet.ChangeNodeDistance(false, m_canChangeCourse);
				return;
			}

			pnSet.CreatePathNode(ref currentNode, m_canChangeCourse);
		}

		private bool CanTravelSegment(ref Vector3D offset, Vector3D start, Vector3D end)
		{
			Vector3D directD; Vector3D.Subtract(ref end, ref start, out directD);
			Vector3 direct = directD;
			float length = direct.Normalize();
			return CanTravelSegment(ref offset, ref direct, length);
		}

		private bool CanTravelSegment(ref Vector3D offset, ref Vector3D start, ref Vector3D end)
		{
			Vector3D directD; Vector3D.Subtract(ref end, ref start, out directD);
			Vector3 direct = directD;
			float length = direct.Normalize();
			return CanTravelSegment(ref offset, ref direct, length);
		}

		/// <param name="offset">Difference between start of segment and current position.</param>
		/// <param name="line">Relative from and relative to</param>
		private bool CanTravelSegment(ref Vector3D offset, ref Vector3 direction, float length)
		{
			Profiler.StartProfileBlock();

			MyCubeBlock ignoreBlock = NavSet.Settings_Current.DestinationEntity as MyCubeBlock;

			m_logger.debugLog("offset: " + offset + ", direction: " + direction + ", length: " + length);

			if (m_checkVoxel)
			{
				//m_logger.debugLog("raycasting voxels");

				Vector3 adjustment; Vector3.Multiply(ref direction, VoxelAdd, out adjustment);
				Vector3 disp; Vector3.Multiply(ref direction, length, out disp);
				Vector3 rayTest; Vector3.Add(ref disp, ref adjustment, out rayTest);
				MyVoxelBase hitVoxel;
				Vector3D hitPosition;
				if (m_tester.RayCastIntersectsVoxel(ref offset, ref rayTest, out hitVoxel, out hitPosition))
				{
					//m_logger.debugLog("Obstructed by voxel " + hitVoxel + " at " + hitPosition, Logger.severity.DEBUG, condition: hitVoxel != m_obstructingEntity.Entity);
					Profiler.EndProfileBlock();
					return false;
				}
			}

			//m_logger.traceLog("checking " + m_entitiesPruneAvoid.Count + " entites - voxels");

			if (m_entitiesPruneAvoid.Count != 0)
				for (int i = m_entitiesPruneAvoid.Count - 1; i >= 0; i--)
				{
					MyEntity entity = m_entitiesPruneAvoid[i];
					if (entity is MyVoxelBase)
						// already checked
						continue;
					MyCubeBlock obstructBlock;
					float distance;
					//m_logger.debugLog("checking: " + entity.nameWithId());
					if (m_tester.ObstructedBy(entity, ignoreBlock, ref offset, ref direction, length, out obstructBlock, out distance))
					{
						//m_logger.debugLog("Obstructed by " + entity.nameWithId() + "." + obstructBlock, Logger.severity.DEBUG, condition: entity != m_obstructingEntity.Entity);
						Profiler.EndProfileBlock();
						return false;
					}
				}

			//m_logger.debugLog("No obstruction. Start: " + (m_currentPosition + offset) + ", finish: " + (m_currentPosition + offset + (line.To - line.From)));
			Profiler.EndProfileBlock();
			return true;
		}

		private void OutOfNodes(FindingSet pnSet)
		{
			if (pnSet.NodeDistance > FindingSet.MinNodeDistance)
			{
				pnSet.ChangeNodeDistance(true, m_canChangeCourse);
				return;
			}

			if (pnSet != m_forward)
			{
				m_logger.debugLog("Failed set: " + ReportRelativePosition(pnSet.m_startPosition), Logger.severity.DEBUG);
				return;
			}

			if (MoveToArbitrary())
				return;

			// with line, failing is "normal"
			m_logger.debugLog("Pathfinding failed", m_canChangeCourse ? Logger.severity.WARNING : Logger.severity.DEBUG);
			Logger.DebugNotify("Pathfinding failed", 10000, m_canChangeCourse ? Logger.severity.WARNING : Logger.severity.DEBUG);

//#if PROFILE
//			LogStats();
//#endif

			PathfindingFailed();
			return;
		}

		/// <summary>
		/// Move to arbitrary node, this can resolve circular obstructions.
		/// </summary>
		private bool MoveToArbitrary()
		{
			if (m_forward.m_openNodes.Count != 0 && m_forward.m_reachedNodes.Count * m_forward.NodeDistance < 1000)
				// try to get more forward nodes before picking one
				return false;

			PathNode arbitraryNode = default(PathNode);
			foreach (KeyValuePair<long, PathNode> pair in m_forward.m_reachedNodes)
			{
				PathNode node = pair.Value;

				if (arbitraryNode.DistToCur < 100f)
				{
					if (node.DistToCur < arbitraryNode.DistToCur)
					{
						m_logger.debugLog("Node does not get closer to 100 m from current. node.DistToCur: " + node.DistToCur + ", arbitraryNode.DistToCur: " + arbitraryNode.DistToCur);
						continue;
					}
				}
				else if (node.DistToCur < 100f)
				{
					m_logger.debugLog("Node is not far enough: " + node.DistToCur);
					continue;
				}
				else if (FindingSet.MinPathDistance(ref m_forward.m_referencePosition, ref arbitraryNode.Position) < FindingSet.MinPathDistance(ref m_forward.m_referencePosition, ref node.Position))
				{
					m_logger.debugLog("Node is further from destination");
					continue;
				}

				arbitraryNode = node;
			}

			m_logger.debugLog("Chosen node: " + ReportRelativePosition(arbitraryNode.Position) + ", dist to cur: " + arbitraryNode.DistToCur + ", min path: " + FindingSet.MinPathDistance(ref m_forward.m_referencePosition, ref arbitraryNode.Position));
			if (arbitraryNode.DistToCur > 10f)
			{
				Logger.DebugNotify("Building path to arbitrary node", level: Logger.severity.DEBUG);
				BuildPath(arbitraryNode.Position.GetHash());
				return true;
			}
			return false;
		}

		private void PathfindingFailed()
		{
			m_waitUntil = Globals.UpdateCount + 600uL;
			CurrentState = State.FailedToFindPath;
		}

		private void BuildPath(long forwardHash, PathNodeSet backward = null, long backwardHash = 0L)
		{
			m_path.Clear();
#if SHOW_PATH
			PurgeTempGPS();
#endif

			m_logger.debugLog("Obstruction: " + m_obstructingEntity.Entity.nameWithId() + ", match position: " + m_obstructingEntity.MatchPosition + ", position: " + m_obstructingEntity.GetPosition() + ", actual position: " + m_obstructingEntity.Entity.PositionComp.GetPosition());

			PathNode node;
			if (!m_forward.TryGetReached(forwardHash, out node))
			{
				m_logger.alwaysLog("Parent hash " + forwardHash + " not found in forward set, failed to build path", Logger.severity.ERROR);
				if (backward.HasReached(forwardHash))
					m_logger.alwaysLog("Backward set does contains hash", Logger.severity.DEBUG);
				PathfindingFailed();
				return;
			}
			while (node.DistToCur != 0f)
			{
#if SHOW_PATH
				ShowPosition(node, "Path");
#endif
				m_path.AddFront(ref node.Position);
				m_logger.debugLog("Forward: " + ReportRelativePosition(node.Position));
				if (!m_forward.TryGetReached(node.ParentKey, out node))
				{
					m_logger.alwaysLog("Child hash " + forwardHash + " not found in forward set, failed to build path", Logger.severity.ERROR);
					if (backward.HasReached(forwardHash))
						m_logger.alwaysLog("Backward set does contains hash", Logger.severity.DEBUG);
					PathfindingFailed();
					return;
				}
			}
			m_path.AddFront(ref node.Position);
			m_logger.debugLog("Forward: " + ReportRelativePosition(node.Position));

			if (backwardHash != 0L)
			{
				if (!backward.TryGetReached(backwardHash, out node))
				{
					m_logger.alwaysLog("Parent hash " + backwardHash + " not found in backward set, failed to build path", Logger.severity.ERROR);
					if (m_forward.HasReached(forwardHash))
						m_logger.alwaysLog("Forward set does contains hash", Logger.severity.DEBUG);
					PathfindingFailed();
					return;
				}

				if (forwardHash == backwardHash && node.ParentKey != 0L)
				{
					if (!backward.TryGetReached(node.ParentKey, out node))
					{
						m_logger.alwaysLog("First child hash " + backwardHash + " not found in backward set, failed to build path", Logger.severity.ERROR);
						if (m_forward.HasReached(forwardHash))
							m_logger.alwaysLog("Forward set does contains hash", Logger.severity.DEBUG);
						PathfindingFailed();
						return;
					}
				}
				while (node.DistToCur != 0f)
				{
#if SHOW_PATH
					ShowPosition(node, "Path");
#endif
					m_path.AddBack(ref node.Position);
					m_logger.debugLog("Backward: " + ReportRelativePosition(node.Position));
					if (!backward.TryGetReached(node.ParentKey, out node))
					{
						m_logger.alwaysLog("Child hash " + backwardHash + " not found in backward set, failed to build path", Logger.severity.ERROR);
						if (m_forward.HasReached(forwardHash))
							m_logger.alwaysLog("Forward set does contains hash", Logger.severity.DEBUG);
						PathfindingFailed();
						return;
					}
				}
				m_path.AddBack(ref node.Position);
				m_logger.debugLog("Backward: " + ReportRelativePosition(node.Position));
			}

			//#if PROFILE
			//			LogStats();
			//#endif

			foreach (Vector3D position in m_path.m_positions)
				m_logger.debugLog("Path: " + ReportRelativePosition(position));

			if (backward != null)
				for (int i = 0; i < m_backwardList.Length; i++)
					if (m_backwardList[i] == backward)
					{
						m_pickedDestination = m_destinations[i];
						m_logger.debugLog("Picked destination: " + m_pickedDestination);
						break;
					}

			CurrentState = State.FollowingPath;
			m_logger.debugLog("Built path", Logger.severity.INFO);
			//Logger.DebugNotify("Finished Pathfinding", level: Logger.severity.INFO);
			SetNextPathTarget();
		}

		private bool SetNextPathTarget()
		{
			m_logger.debugLog("Path is empty", Logger.severity.ERROR, condition: m_path.Count == 0);
			m_logger.debugLog("Path is complete", Logger.severity.ERROR, condition: m_path.Count == 1);

			Vector3D obstructionPosition = m_obstructingEntity.GetPosition();
			Vector3D currentPosition; Vector3D.Subtract(ref m_currentPosition, ref obstructionPosition, out currentPosition);

			int target;
			for (target = m_path.m_target == 0 ? m_path.Count - 1 : m_path.m_target - 1; target > 0; target--)
			{
				m_logger.debugLog("Trying potential target #" + target + ": " + m_path.m_positions[target]);
				if (CanTravelSegment(ref Vector3D.Zero, currentPosition, m_path.m_positions[target]))
				{
					m_logger.debugLog("Next target is position #" + target);
					m_path.m_target = target;
					return true;
				}
			}

			m_logger.debugLog("Failed to set next target", Logger.severity.INFO);
			return false;
		}

		#region Debug & Profile

		private string ReportRelativePosition(Vector3D position)
		{
			return position + " => " + (position + m_obstructingEntity.GetPosition());
		}

//		private void LogStats()
//		{
//			foreach (Vector3D position in m_path.m_postions)
//				m_logger.alwaysLog("Waypoint: " + ReportRelativePosition(position));
//#if PROFILE
//			m_logger.alwaysLog("Nodes reached: " + (m_forward.m_reachedNodes.Count + m_backward.m_reachedNodes.Count) + ", unreachable: " + (m_forward.m_unreachableNodes + m_backward.m_unreachableNodes) +
//				", open: " + (m_forward.m_openNodes.Count + m_backward.m_openNodes.Count) + ", path length: " + m_path.Count);
//#else
//			m_logger.alwaysLog("Nodes reached: " + (m_forward.m_reachedNodes.Count + m_backward.m_reachedNodes.Count) +
//				", open: " + (m_forward.m_openNodes.Count + m_backward.m_openNodes.Count) + ", path length: " + m_path.Count);
//#endif
//		}

#if DEBUG
		private Queue<IMyGps> m_shownPositions = new Queue<IMyGps>(100);
		private List<IMyGps> m_allGpsList = new List<IMyGps>();
#endif

		[System.Diagnostics.Conditional("DEBUG")]
		private void ShowPosition(PathNode currentNode, string name = null)
		{
			IMyGps gps = MyAPIGateway.Session.GPS.Create(name ?? currentNode.Position.ToString(), string.Empty, currentNode.Position + m_obstructingEntity.GetPosition(), true, true);
			gps.DiscardAt = TimeSpan.MinValue;
			//m_logger.debugLog("Showing " + gps.Coords);
			MyAPIGateway.Utilities.TryInvokeOnGameThread(() => MyAPIGateway.Session.GPS.AddLocalGps(gps));
#if DEBUG
			m_shownPositions.Enqueue(gps);
			if (m_shownPositions.Count == 100)
			{
				IMyGps remove = m_shownPositions.Dequeue();
				//m_logger.debugLog("Removing " + remove.Coords);
				MyAPIGateway.Utilities.TryInvokeOnGameThread(() => MyAPIGateway.Session.GPS.RemoveLocalGps(remove));
			}
#endif
		}

		[System.Diagnostics.Conditional("DEBUG")]
		private void PurgeTempGPS()
		{
#if DEBUG
			m_shownPositions.Clear();
			MyAPIGateway.Utilities.TryInvokeOnGameThread(() => {
				foreach (IMyGps gps in MyAPIGateway.Session.GPS.GetGpsList(MyAPIGateway.Session.Player.IdentityId))
					if (gps.DiscardAt.HasValue)
						MyAPIGateway.Session.GPS.RemoveLocalGps(gps);
			});
#endif
		}

		private string SetName(PathNodeSet pnSet)
		{
			return pnSet == m_forward ? "Forward" : "Backward";
		}

		//[System.Diagnostics.Conditional("DEBUG")]
		//private void DebugRectClose(ref PathNodeSet pnSet, Vector3D position)
		//{
		//	foreach (PathNode otherNode in pnSet.m_reachedNodes.Values)
		//		if (otherNode.Position != m_forward.m_startPosition)
		//		{
		//			double distRect = Vector3D.RectangularDistance(position, otherNode.Position);
		//			if (distRect < m_nodeDistance)
		//			{
		//				Vector3D positionSteps; StepCount(ref position, out positionSteps);
		//				Vector3D otherNodePosition = otherNode.Position;
		//				Vector3D otherSteps; StepCount(ref otherNodePosition, out otherSteps);
		//				m_logger.debugLog(ReportRelativePosition(position) + " @ " + positionSteps + " steps is less than " + m_nodeDistance + " m from: " + ReportRelativePosition(otherNode.Position) + " @ " + otherSteps + " steps", Logger.severity.WARNING);
		//			}
		//		}
		//}

		//private void StepCount(ref Vector3D position, out Vector3D steps)
		//{
		//	Vector3D finishToPosition; Vector3D.Subtract(ref position, ref m_backward.m_startPosition, out finishToPosition);
		//	Vector3D.Divide(ref finishToPosition, m_nodeDistance, out steps);
		//}

		#endregion

	}
}
