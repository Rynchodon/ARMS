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
	// see Pathfinder.cs for Summary & Remarks
	// contains the low-level/pathfinding parts of Pathfinder
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

				Log.DebugLog("Changed state from " + value_state + " to " + value);
				value_state = value;
			}
		}

		#region Setup

		/// <summary>
		/// Prepares forwards & backwards sets for pathfinding and starts the process.
		/// </summary>
		private void StartPathfinding()
		{
			m_path.Clear();
#if SHOW_REACHED
			PurgeTempGPS();
#endif
			CurrentState = State.SearchingForPath;

			if (m_forward == null)
			{
				Log.DebugLog("interrupted");
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

		/// <summary>
		/// Prepares the backwards sets for pathfinding.
		/// </summary>
		/// <param name="referencePosition">The position from which all pathfinding nodes for all sets will have a discrete distance.</param>
		/// <param name="maxNodeDistance">The maximum distance between nodes.</param>
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

		/// <summary>
		/// Prepares the forwards set for pathfinding.
		/// </summary>
		/// <param name="referencePosition">The position from which all pathfinding nodes for all sets will have a discrete distance.</param>
		/// <param name="maxNodeDistance">The maximum distance between nodes.</param>
		private void SetupForward(ref Vector3D referencePosition, int maxNodeDistance)
		{
			Log.DebugLog("m_obstructingEntity.Entity == null", Logger.severity.FATAL, condition: m_obstructingEntity.Entity == null);
			Log.DebugLog("m_forward == null", Logger.severity.FATAL, condition: m_forward == null);

			Vector3D startPosition;
			Vector3D obstructPosition = m_obstructingEntity.GetPosition();
			Vector3D.Subtract(ref m_currentPosition, ref obstructPosition, out startPosition);

			m_forward.Setup(ref referencePosition, ref startPosition, m_canChangeCourse, maxNodeDistance);
			m_path.AddFront(ref m_forward.m_startPosition);
		}

		/// <summary>
		/// Remove all sets from m_backwardList.
		/// </summary>
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
		}

		/// <summary>
		/// Fill and clean m_backwardList.
		/// </summary>
		private void CreateBackwards()
		{
			if (m_backwardList != null)
				if (m_backwardList.Length == m_destinations.Length)
				{
					for (int i = 0; i < m_backwardList.Length; i++)
						if (m_backwardList[i] == null)
							m_backwardList[i] = ResourcePool<FindingSet>.Get();
						else
							((FindingSet)m_backwardList[i]).Clear();
					return;
				}
				else
					ClearBackwards();

			m_backwardList = new PathNodeSet[m_destinations.Length];

			for (int i = 0; i < m_backwardList.Length; i++)
				if (m_backwardList[i] == null)
					m_backwardList[i] = ResourcePool<FindingSet>.Get();
		}

		/// <summary>
		/// Converts a destination to a node position.
		/// </summary>
		/// <param name="dest">The destination to get the position from.</param>
		/// <param name="fromObsPos">The node position.</param>
		private void GetFromObsPosition(Destination dest, out Vector3D fromObsPos)
		{
			Vector3D destWorld = dest.WorldPosition();
			Vector3D obstructPosition = m_obstructingEntity.GetPosition();
			Vector3D.Subtract(ref destWorld, ref obstructPosition, out fromObsPos);

			Log.TraceLog("destWorld: " + destWorld + ", obstructPosition: " + obstructPosition + ", fromObsPos: " + fromObsPos);
		}

		#endregion
		
		/// <summary>
		/// Main entry point while pathfinding.
		/// </summary>
		private void ContinuePathfinding()
		{
			if (m_waitUntil > Globals.UpdateCount)
			{
				m_holdPosition = true;
				OnComplete();
				return;
			}

			Log.DebugLog("m_runningLock == null", Logger.severity.FATAL, condition: m_runningLock == null);

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
					Log.DebugLog("m_forward == null", Logger.severity.FATAL, condition: m_forward == null);
					Log.DebugLog("m_backwardList == null", Logger.severity.FATAL, condition: m_backwardList == null);

					PathNodeSet bestSet = null;
					foreach (PathNodeSet backSet in m_backwardList)
						if (bestSet == null || backSet.CompareTo(bestSet) < 0)
							bestSet = backSet;
					Log.DebugLog("bestSet == null", Logger.severity.FATAL, condition: bestSet == null);
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
				if (m_runHalt || m_runInterrupt)
				{
					Log.DebugLog("Exception due to halt/interrupt", Logger.severity.DEBUG);
					return;
				}
				else
				{
					Log.AlwaysLog("Pathfinder crashed", Logger.severity.ERROR);
					CurrentState = State.Crashed;
					throw;
				}
			}
			finally { m_runningLock.ReleaseExclusive(); }

			PostRun();
		}

		/// <summary>
		/// Continues pathfinding.
		/// </summary>
		/// <param name="pnSet">The active set.</param>
		private void ContinuePathfinding(FindingSet pnSet)
		{
			//PathNodeSet pnSet = isForwardSet ? m_forward : m_backward;

			Log.DebugLog(SetName(pnSet) + " m_obstructingEntity == null", Logger.severity.ERROR, condition: m_obstructingEntity.Entity == null);

			if (pnSet.m_openNodes.Count == 0)
			{
				OutOfNodes(pnSet);
				return;
			}

			PathNode currentNode = pnSet.m_openNodes.RemoveMin();
			if (currentNode.DistToCur == 0f)
			{
				Log.DebugLog(SetName(pnSet) + " first node: " + ReportRelativePosition(currentNode.Position));
				pnSet.CreatePathNode(ref currentNode, m_canChangeCourse);
				return;
			}

			if (pnSet.HasReached(currentNode.Position.GetHash()))
			{
				//Log.DebugLog("Already reached: " + ReportRelativePosition(currentNode.Position), secondaryState: SetName(direction));
				return;
			}

			FillEntitiesLists();
			Vector3 repulsion;
			CalcRepulsion(false, out repulsion);
			Log.DebugLog(SetName(pnSet) + " Calculated repulsion for some reason: " + repulsion, Logger.severity.WARNING, condition: repulsion != Vector3.Zero);

			PathNode parent;
			if (!pnSet.m_reachedNodes.TryGetValue(currentNode.ParentKey, out parent))
			{
				Log.DebugLog("Failed to get parent", Logger.severity.ERROR);
				return;
			}
			Vector3D worldParent;
			Vector3D obstructPosition = m_obstructingEntity.GetPosition();
			Vector3D.Add(ref obstructPosition, ref parent.Position, out worldParent);
			PathTester.TestInput input;
			Vector3D.Subtract(ref worldParent, ref m_currentPosition, out input.Offset);
			input.Direction = currentNode.DirectionFromParent;
			input.Length = currentNode.DistToCur - parent.DistToCur;

			float proximity;
			if (!CanTravelSegment(ref input, out proximity))
			{
#if PROFILE
				pnSet.m_unreachableNodes++;
#endif
				//Log.DebugLog("Not reachable: " + ReportRelativePosition(currentNode.Position), secondaryState: SetName(pnSet));
				return;
			}
			ReachedNode(pnSet, ref currentNode, ref input, proximity);
		}

		/// <summary>
		/// Called when a node is reached. Tests against targets and creates new nodes.
		/// </summary>
		/// <param name="pnSet">The active set.</param>
		/// <param name="currentNode">The node that was reached.</param>
		/// <param name="input">Param for CanTravelSegment, Offset and Direction should be correct.</param>
		/// <param name="proximity">Result from CanTravelSegment, how close the ship would come to an entity when traveling to this node from its parent.</param>
		private void ReachedNode(FindingSet pnSet, ref PathNode currentNode, ref PathTester.TestInput input, float proximity)
		{
			// impose a penalty for going near entities, this does not affect this node but will affect its children
			// this really helps prevent pathfinder getting stuck but the penalty might be too high
			float penalty = 10f * (100f - MathHelper.Clamp(proximity, 0f, 100f));
			Log.DebugLog(SetName(pnSet) + " Reached node: " + ReportRelativePosition(currentNode.Position) + " from " + ReportRelativePosition(pnSet.m_reachedNodes[currentNode.ParentKey].Position) +
				", reached: " + pnSet.m_reachedNodes.Count + ", open: " + pnSet.m_openNodes.Count + ", proximity: " + proximity + ", penalty: " + penalty);
			currentNode.DistToCur += penalty;
			long cNodePosHash = currentNode.Key;
			pnSet.m_reachedNodes.Add(cNodePosHash, currentNode);

			if (!m_canChangeCourse)
			{
				// test from current node position to destination
				Log.DebugLog(SetName(pnSet) + " Running backwards search", Logger.severity.ERROR, condition: pnSet != m_forward);
				Vector3D.Subtract(ref m_currentPosition, ref pnSet.m_referencePosition, out input.Offset);
				input.Length = (float)Vector3D.Distance(currentNode.Position, pnSet.m_referencePosition);
				if (CanTravelSegment(ref input, out proximity))
				{
					Log.DebugLog(SetName(pnSet) + " Reached destination from node: " + ReportRelativePosition(currentNode.Position));
					Log.DebugLog("Backwards start is not reference position", Logger.severity.ERROR, condition: m_backwardList[0].m_startPosition != pnSet.m_referencePosition);
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
					Log.DebugLog(SetName(pnSet) + " Opposite set has same position");
					if (pnSet == m_forward)
						BuildPath(cNodePosHash, target, cNodePosHash);
					else
						BuildPath(cNodePosHash, pnSet, cNodePosHash);
					return;
				}

			Vector3D obstructPosition = m_obstructingEntity.GetPosition();
			Vector3D currentNodeWorld; Vector3D.Add(ref obstructPosition, ref currentNode.Position, out currentNodeWorld);
			BoundingSphereD sphere = new BoundingSphereD() { Center = currentNodeWorld, Radius = m_autopilotShipBoundingRadius + 100f };
			m_entitiesRepulse.Clear(); // use repulse list as prune/avoid is needed for CanTravelSegment
			MyGamePruningStructure.GetAllTopMostEntitiesInSphere(ref sphere, m_entitiesRepulse);
			if (m_entitiesRepulse.Count == 0 && BlueSkyReached(pnSet, currentNode, ref currentNodeWorld))
				return;

#if SHOW_REACHED
			ShowPosition(currentNode, SetName(pnSet));
#endif

			if (pnSet.NodeDistance < FindingSet.DefaultNodeDistance && pnSet.m_reachedNodes.Count % 10 == 0 && pnSet.m_openNodes.Count > 100)
			{
				Log.DebugLog("Reached: " + pnSet.m_reachedNodes.Count + ", trying with higher node distance");
				pnSet.ChangeNodeDistance(false, m_canChangeCourse);
				return;
			}

			pnSet.CreatePathNode(ref currentNode, m_canChangeCourse);
		}

		/// <summary>
		/// Called when a blue sky node is reached. Tests if the ship can reach a target blue sky or target start.
		/// </summary>
		/// <param name="pnSet">The active set.</param>
		/// <param name="currentNode">The blue sky node.</param>
		/// <param name="currentNodeWorld">World Position of the current node.</param>
		/// <returns>True iff opposite start or target blue sky is reached, in which case, this method will have invoked BuildPath.</returns>
		private bool BlueSkyReached(FindingSet pnSet, PathNode currentNode, ref Vector3D currentNodeWorld)
		{
			//Logger.DebugNotify(SetName(pnSet) + " Blue Sky");
			Log.DebugLog(SetName(pnSet) + " Blue sky node: " + ReportRelativePosition(currentNode.Position));

			PathTester.TestInput input;
			Vector3D.Subtract(ref currentNodeWorld, ref m_currentPosition, out input.Offset);

			foreach (PathNodeSet target in pnSet.m_targets)
			{
				Vector3D disp; Vector3D.Subtract(ref target.m_startPosition, ref currentNode.Position, out disp);
				input.Direction = disp;
				input.Length = input.Direction.Normalize();

				float proximity;
				if (CanTravelSegment(ref input, out proximity))
				{
					Log.DebugLog(SetName(pnSet) + " Blue sky to opposite start");
					if (pnSet == m_forward)
						BuildPath(currentNode.Key, target, target.m_startPosition.GetHash());
					else
						BuildPath(target.m_startPosition.GetHash(), pnSet, currentNode.Key);
					return true;
				}
				foreach (Vector3D targetBlueSky in target.BlueSkyNodes)
				{
					input.Direction = Vector3D.Subtract(targetBlueSky, currentNode.Position);
					input.Length = input.Direction.Normalize();

					if (CanTravelSegment(ref input, out proximity))
					{
						Log.DebugLog(SetName(pnSet) + " Blue sky path");
						if (pnSet == m_forward)
							BuildPath(currentNode.Key, target, targetBlueSky.GetHash());
						else
							BuildPath(targetBlueSky.GetHash(), pnSet, currentNode.Key);
						return true;
					}
				}
			}
			pnSet.m_blueSkyNodes.Add(currentNode.Position);
#if SHOW_REACHED
				ShowPosition(currentNode, "Blue Sky " + SetName(pnSet));
#endif
			return false;
		}

		/// <summary>
		/// Tests if a line can be traveled from input.Offset to input.Offset + input.Direction * input.Length
		/// </summary>
		/// <param name="input">Param for PathTester</param>
		/// <param name="proximity">How close the ship would come to an obstruction.</param>
		/// <returns>True iff the segment can be traveled by the ship.</returns>
		private bool CanTravelSegment(ref PathTester.TestInput input, out float proximity)
		{
			Profiler.StartProfileBlock();

			Log.DebugLog(input.ToString());
			MyCubeBlock ignoreBlock = NavSet.Settings_Current.DestinationEntity as MyCubeBlock;
			proximity = float.MaxValue;

			if (m_checkVoxel)
			{
				//Log.DebugLog("raycasting voxels");
				PathTester.VoxelTestResult result;
				if (m_tester.RayCastIntersectsVoxel(ref input, out result))
				{
					//Log.DebugLog("Obstructed by voxel " + hitVoxel + " at " + hitPosition, Logger.severity.DEBUG, condition: hitVoxel != m_obstructingEntity.Entity);
					Profiler.EndProfileBlock();
					return false;
				}
				if (result.Proximity < proximity)
					proximity = result.Proximity;
			}

			//Log.TraceLog("checking " + m_entitiesPruneAvoid.Count + " entites - voxels");

			if (m_entitiesPruneAvoid.Count != 0)
				for (int i = m_entitiesPruneAvoid.Count - 1; i >= 0; i--)
				{
					MyEntity entity = m_entitiesPruneAvoid[i];
					if (entity is MyVoxelBase)
						// already checked
						continue;
					//Log.DebugLog("checking: " + entity.nameWithId());
					PathTester.GridTestResult result;
					if (m_tester.ObstructedBy(entity, ignoreBlock, ref input, out result))
					{
						//Log.DebugLog("Obstructed by " + entity.nameWithId() + "." + obstructBlock, Logger.severity.DEBUG, condition: entity != m_obstructingEntity.Entity);
						Profiler.EndProfileBlock();
						return false;
					}
					if (result.Proximity < proximity)
						proximity = result.Proximity;
				}

			//Log.DebugLog("No obstruction. Start: " + (m_currentPosition + offset) + ", finish: " + (m_currentPosition + offset + (line.To - line.From)));
			Profiler.EndProfileBlock();
			return true;
		}

		/// <summary>
		/// Resolves set running out of nodes, either changing NodeDistance, moving the ship, or failing pathfinding.
		/// </summary>
		/// <param name="pnSet">The set that is out of nodes.</param>
		private void OutOfNodes(FindingSet pnSet)
		{
			if (pnSet.NodeDistance > FindingSet.MinNodeDistance)
			{
				pnSet.ChangeNodeDistance(true, m_canChangeCourse);
				return;
			}

			if (pnSet != m_forward)
			{
				Log.DebugLog("Failed set: " + ReportRelativePosition(pnSet.m_startPosition), Logger.severity.DEBUG);
				return;
			}

			if (MoveToArbitrary())
				return;

			// with line, failing is "normal"
			Log.DebugLog("Pathfinding failed", m_canChangeCourse ? Logger.severity.WARNING : Logger.severity.DEBUG);
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
		/// <returns>True iff a path has been built to an arbitrary node.</returns>
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
						Log.DebugLog("Node does not get closer to 100 m from current. node.DistToCur: " + node.DistToCur + ", arbitraryNode.DistToCur: " + arbitraryNode.DistToCur);
						continue;
					}
				}
				else if (node.DistToCur < 100f)
				{
					Log.DebugLog("Node is not far enough: " + node.DistToCur);
					continue;
				}
				else if (FindingSet.MinPathDistance(ref m_forward.m_referencePosition, ref arbitraryNode.Position) < FindingSet.MinPathDistance(ref m_forward.m_referencePosition, ref node.Position))
				{
					Log.DebugLog("Node is further from destination");
					continue;
				}

				arbitraryNode = node;
			}

			Log.DebugLog("Chosen node: " + ReportRelativePosition(arbitraryNode.Position) + ", dist to cur: " + arbitraryNode.DistToCur + ", min path: " + FindingSet.MinPathDistance(ref m_forward.m_referencePosition, ref arbitraryNode.Position));
			if (arbitraryNode.DistToCur > 10f)
			{
				Logger.DebugNotify("Building path to arbitrary node", level: Logger.severity.DEBUG);
				BuildPath(arbitraryNode.Position.GetHash());
				return true;
			}
			return false;
		}

		/// <summary>
		/// Sets the state to failed and sets wait.
		/// </summary>
		private void PathfindingFailed()
		{
			m_waitUntil = Globals.UpdateCount + 600uL;
			CurrentState = State.FailedToFindPath;
		}

		/// <summary>
		/// Upon completion of pathfinding, this method builds the path that autopilot will have to follow.
		/// </summary>
		/// <param name="forwardHash">Position hash of the forward node that was reached.</param>
		/// <param name="backward">The backward set that was reached.</param>
		/// <param name="backwardHash">Position hash of the backward node that was reached.</param>
		private void BuildPath(long forwardHash, PathNodeSet backward = null, long backwardHash = 0L)
		{
			m_path.Clear();
#if SHOW_PATH
			PurgeTempGPS();
#endif

			Log.DebugLog("Obstruction: " + m_obstructingEntity.Entity.nameWithId() + ", match position: " + m_obstructingEntity.MatchPosition + ", position: " + m_obstructingEntity.GetPosition() + ", actual position: " + m_obstructingEntity.Entity.PositionComp.GetPosition());

			PathNode node;
			if (!m_forward.TryGetReached(forwardHash, out node))
			{
				Log.AlwaysLog("Parent hash " + forwardHash + " not found in forward set, failed to build path", Logger.severity.ERROR);
				if (backward.HasReached(forwardHash))
					Log.AlwaysLog("Backward set does contains hash", Logger.severity.DEBUG);
				PathfindingFailed();
				return;
			}
			while (node.DistToCur != 0f)
			{
#if SHOW_PATH
				ShowPosition(node, "Path");
#endif
				m_path.AddFront(ref node.Position);
				Log.DebugLog("Forward: " + ReportRelativePosition(node.Position));
				if (!m_forward.TryGetReached(node.ParentKey, out node))
				{
					Log.AlwaysLog("Child hash " + forwardHash + " not found in forward set, failed to build path", Logger.severity.ERROR);
					if (backward.HasReached(forwardHash))
						Log.AlwaysLog("Backward set does contains hash", Logger.severity.DEBUG);
					PathfindingFailed();
					return;
				}
			}
			m_path.AddFront(ref node.Position);
			Log.DebugLog("Forward: " + ReportRelativePosition(node.Position));

			if (backwardHash != 0L)
			{
				if (!backward.TryGetReached(backwardHash, out node))
				{
					Log.AlwaysLog("Parent hash " + backwardHash + " not found in backward set, failed to build path", Logger.severity.ERROR);
					if (m_forward.HasReached(forwardHash))
						Log.AlwaysLog("Forward set does contains hash", Logger.severity.DEBUG);
					PathfindingFailed();
					return;
				}

				if (forwardHash == backwardHash && node.ParentKey != 0L)
				{
					if (!backward.TryGetReached(node.ParentKey, out node))
					{
						Log.AlwaysLog("First child hash " + backwardHash + " not found in backward set, failed to build path", Logger.severity.ERROR);
						if (m_forward.HasReached(forwardHash))
							Log.AlwaysLog("Forward set does contains hash", Logger.severity.DEBUG);
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
					Log.DebugLog("Backward: " + ReportRelativePosition(node.Position));
					if (!backward.TryGetReached(node.ParentKey, out node))
					{
						Log.AlwaysLog("Child hash " + backwardHash + " not found in backward set, failed to build path", Logger.severity.ERROR);
						if (m_forward.HasReached(forwardHash))
							Log.AlwaysLog("Forward set does contains hash", Logger.severity.DEBUG);
						PathfindingFailed();
						return;
					}
				}
				m_path.AddBack(ref node.Position);
				Log.DebugLog("Backward: " + ReportRelativePosition(node.Position));
			}

			//#if PROFILE
			//			LogStats();
			//#endif

			foreach (Vector3D position in m_path.m_positions)
				Log.DebugLog("Path: " + ReportRelativePosition(position));

			if (backward != null)
				for (int i = 0; i < m_backwardList.Length; i++)
					if (m_backwardList[i] == backward)
					{
						m_pickedDestination = m_destinations[i];
						Log.DebugLog("Picked destination: " + m_pickedDestination);
						break;
					}

			CurrentState = State.FollowingPath;
			Log.DebugLog("Built path", Logger.severity.INFO);
			//Logger.DebugNotify("Finished Pathfinding", level: Logger.severity.INFO);
			SetNextPathTarget();
		}

		/// <summary>
		/// Sets the next reachable waypoint in m_path as Pathfinder's target.
		/// </summary>
		/// <returns>True iff any waypoint can be reached.</returns>
		private bool SetNextPathTarget()
		{
			Log.DebugLog("Path is empty", Logger.severity.ERROR, condition: m_path.Count == 0);
			Log.DebugLog("Path is complete", Logger.severity.ERROR, condition: m_path.Count == 1);

			Vector3D obstructionPosition = m_obstructingEntity.GetPosition();
			Vector3D currentPosition; Vector3D.Subtract(ref m_currentPosition, ref obstructionPosition, out currentPosition);

			int target;
			for (target = m_path.m_target == 0 ? m_path.Count - 1 : m_path.m_target - 1; target > 0; target--)
			{
				Log.DebugLog("Trying potential target #" + target + ": " + m_path.m_positions[target]);
				PathTester.TestInput input;
				input.Offset = Vector3D.Zero;
				input.Direction = Vector3D.Subtract(m_path.m_positions[target], currentPosition);
				input.Length = input.Direction.Normalize();

				float proximity;
				if (CanTravelSegment(ref input, out proximity))
				{
					Log.DebugLog("Next target is position #" + target);
					m_path.m_target = target;
					return true;
				}
			}

			Log.DebugLog("Failed to set next target", Logger.severity.INFO);
			return false;
		}

		#region Debug & Profile

		/// <summary>
		/// Constructs a string containing the supplied position and its world translation.
		/// </summary>
		/// <param name="position">The relative position of a node.</param>
		/// <returns>A string containing the supplied position and its world translation.</returns>
		private string ReportRelativePosition(Vector3D position)
		{
			return position + " => " + (position + m_obstructingEntity.GetPosition());
		}

//		private void LogStats()
//		{
//			foreach (Vector3D position in m_path.m_postions)
//				Log.AlwaysLog("Waypoint: " + ReportRelativePosition(position));
//#if PROFILE
//			Log.AlwaysLog("Nodes reached: " + (m_forward.m_reachedNodes.Count + m_backward.m_reachedNodes.Count) + ", unreachable: " + (m_forward.m_unreachableNodes + m_backward.m_unreachableNodes) +
//				", open: " + (m_forward.m_openNodes.Count + m_backward.m_openNodes.Count) + ", path length: " + m_path.Count);
//#else
//			Log.AlwaysLog("Nodes reached: " + (m_forward.m_reachedNodes.Count + m_backward.m_reachedNodes.Count) +
//				", open: " + (m_forward.m_openNodes.Count + m_backward.m_openNodes.Count) + ", path length: " + m_path.Count);
//#endif
//		}

#if DEBUG
		private Queue<IMyGps> m_shownPositions = new Queue<IMyGps>(100);
		private List<IMyGps> m_allGpsList = new List<IMyGps>();
#endif

		/// <summary>
		/// Show the position of a node on the HUD using GPS marker.
		/// </summary>
		/// <param name="currentNode">The node which will have its position shown.</param>
		/// <param name="name">The name to give the GPS marker.</param>
		[System.Diagnostics.Conditional("DEBUG")]
		private void ShowPosition(PathNode currentNode, string name = null)
		{
			IMyGps gps = MyAPIGateway.Session.GPS.Create(name ?? currentNode.Position.ToString(), string.Empty, currentNode.Position + m_obstructingEntity.GetPosition(), true, true);
			gps.DiscardAt = TimeSpan.MinValue;
			//Log.DebugLog("Showing " + gps.Coords);
			MyAPIGateway.Utilities.TryInvokeOnGameThread(() => MyAPIGateway.Session.GPS.AddLocalGps(gps));
#if DEBUG
			m_shownPositions.Enqueue(gps);
			if (m_shownPositions.Count == 100)
			{
				IMyGps remove = m_shownPositions.Dequeue();
				//Log.DebugLog("Removing " + remove.Coords);
				MyAPIGateway.Utilities.TryInvokeOnGameThread(() => MyAPIGateway.Session.GPS.RemoveLocalGps(remove));
			}
#endif
		}

		/// <summary>
		/// Remove ALL temporary GPS markers.
		/// </summary>
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
		//				Log.DebugLog(ReportRelativePosition(position) + " @ " + positionSteps + " steps is less than " + m_nodeDistance + " m from: " + ReportRelativePosition(otherNode.Position) + " @ " + otherSteps + " steps", Logger.severity.WARNING);
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
