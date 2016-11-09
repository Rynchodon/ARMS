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

		/// <summary>
		/// Starts the pathfinding.
		/// </summary>
		private void FindAPath()
		{
			m_path.Clear();
			//PurgeTempGPS();
			m_pathfinding = true;
			CurrentState = State.SearchingForPath;

			FillDestWorld();
			m_logger.debugLog("Started pathfinding, obstruction: " + m_obstructingEntity.Entity.nameWithId() + ", dest: " + m_destination.Entity.nameWithId());

			m_logger.debugLog("m_obstructingEntity == null", Logger.severity.ERROR, condition: m_obstructingEntity.Entity == null);

			PathNode startNode;
			Vector3D relativePosition;
			Vector3D obstructPosition = m_obstructingEntity.GetPosition();

			Vector3D.Subtract(ref m_destWorld, ref obstructPosition, out relativePosition);
			Vector3D backStartPos = new Vector3D(Math.Round(relativePosition.X), Math.Round(relativePosition.Y), Math.Round(relativePosition.Z));

			if (!m_canChangeCourse)
			{
				m_logger.debugLog("No course change permitted, setting up line");

				m_forward.Clear();
				m_backward.Clear();

				Vector3D currentToDest; Vector3D.Subtract(ref m_destWorld, ref m_currentPosition, out currentToDest);
				currentToDest.Normalize();
				Vector3D.Subtract(ref m_currentPosition, ref obstructPosition, out m_forward.m_startPosition);
				PathNode foreFirstNode = new PathNode() { Position = m_forward.m_startPosition, DirectionFromParent = currentToDest };
				m_forward.m_reachedNodes.Add(foreFirstNode.Key, foreFirstNode);
				m_forward.AddOpenNode(ref foreFirstNode, 0f);
				m_path.AddBack(ref m_forward.m_startPosition);

				PathNode backFirstNode = new PathNode() { Position = backStartPos };
				m_backward.m_startPosition = backStartPos;
				m_backward.m_reachedNodes.Add(backFirstNode.Key, backFirstNode);
				return;
			}

			if (m_forward.m_reachedNodes.Count == 0 && m_backward.m_reachedNodes.Count == 0)
			{
				// limit node distance when autopilot is near destination
				float maxNodeDist = Math.Max(NavSet.Settings_Current.Distance * 0.25f, 10f);
				if (m_nodeDistance > maxNodeDist)
				{
					m_logger.debugLog("Limiting node distance to " + maxNodeDist);
					m_nodeDistance = maxNodeDist;
				}
			}

			double diffSquared; Vector3D.DistanceSquared(ref backStartPos, ref m_backward.m_startPosition, out diffSquared);
			float destRadius = NavSet.Settings_Current.DestinationRadius;
			destRadius *= destRadius;

			if (diffSquared > destRadius || m_backward.m_reachedNodes.Count == 0)
			{
				m_logger.debugLog("Rebuilding backward.. Previous start : " + m_backward.m_startPosition + ", new start: " + backStartPos +
					", dest world: " + m_destWorld + ", obstruct: " + obstructPosition + ", relative: " + relativePosition);
				m_backward.Clear();
				m_backward.m_startPosition = backStartPos;
				startNode = new PathNode() { Position = backStartPos };
				m_backward.AddOpenNode(ref startNode, 0f);
				m_backward.m_reachedNodes.Add(startNode.Position.GetHash(), startNode);
			}
			else
			{
				m_logger.debugLog("Reusing backward: " + m_backward.m_reachedNodes.Count + ", " + m_backward.m_openNodes.Count);
				//Logger.DebugNotify("Reusing backward nodes");
			}

			Vector3D currentRelative; Vector3D.Subtract(ref m_currentPosition, ref obstructPosition, out currentRelative);
			// forward start node's children needs to be a discrete number of steps from backward startNode
			Vector3D finishToStart; Vector3D.Subtract(ref currentRelative, ref backStartPos, out finishToStart);

			Vector3D.DistanceSquared(ref currentRelative, ref m_forward.m_startPosition, out diffSquared);
			if (diffSquared > destRadius || m_forward.m_reachedNodes.Count == 0)
			{
				m_logger.debugLog("Rebuilding forward. Previous start: " + m_forward.m_startPosition + ", new start: " + currentRelative);
				m_forward.Clear();
				m_forward.m_startPosition = currentRelative;
				m_path.AddBack(ref currentRelative);

				PathNode parentNode = new PathNode() { Position = currentRelative };
				long parentKey = parentNode.Position.GetHash();
				m_forward.m_reachedNodes.Add(parentKey, parentNode);

				double parentPathDist = MinPathDistance(ref finishToStart);

				Vector3D steps; Vector3D.Divide(ref finishToStart, m_nodeDistance, out steps);
				Vector3D stepsFloor = new Vector3D() { X = Math.Floor(steps.X), Y = Math.Floor(steps.Y), Z = Math.Floor(steps.Z) };
				Vector3D currentStep;
				for (currentStep.X = stepsFloor.X; currentStep.X <= stepsFloor.X + 1; currentStep.X++)
					for (currentStep.Y = stepsFloor.Y; currentStep.Y <= stepsFloor.Y + 1; currentStep.Y++)
						for (currentStep.Z = stepsFloor.Z; currentStep.Z <= stepsFloor.Z + 1; currentStep.Z++)
						{
							Vector3D.Multiply(ref currentStep, m_nodeDistance, out finishToStart);
							Vector3D.Add(ref backStartPos, ref finishToStart, out relativePosition);

							Vector3D dispToDest; Vector3D.Subtract(ref backStartPos, ref relativePosition, out dispToDest);
							double childPathDist = MinPathDistance(ref dispToDest);

							//if (childPathDist > parentPathDist)
							//{
							//	m_logger.debugLog("Skipping child: " + ReportRelativePosition(relativePosition) + ", childPathDist: " + childPathDist + ", parentPathDist: " + parentPathDist);
							//	continue;
							//}

							PathNode childNode = new PathNode(ref parentNode, ref relativePosition);

							float resultKey = childNode.DistToCur + (float)childPathDist;

							//m_logger.debugLog("Initial child: " + ReportRelativePosition(childNode.Position));
							m_forward.AddOpenNode(ref childNode, resultKey);
						}
			}
			else
			{
				m_logger.debugLog("Reusing forward: " + m_forward.m_reachedNodes.Count + ", " + m_forward.m_openNodes.Count);
				//Logger.DebugNotify("Reusing forward nodes");
				m_path.AddBack(ref currentRelative);
			}
		}

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
					FindAPath(true);
				else if (m_forward.m_blueSkyNodes.Count != m_backward.m_blueSkyNodes.Count && NavSet.Settings_Current.Distance > 1000f)
				{
					if (m_forward.m_blueSkyNodes.Count < m_backward.m_blueSkyNodes.Count)
						FindAPath(true);
					else
						FindAPath(false);
				}
				FindAPath(m_forward.m_reachedNodes.Count <= m_backward.m_reachedNodes.Count);
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
		private void FindAPath(bool isForwardSet)
		{
			PathNodeSet pnSet = isForwardSet ? m_forward : m_backward;

			m_logger.debugLog("m_obstructingEntity == null", Logger.severity.ERROR, condition: m_obstructingEntity.Entity == null, secondaryState: SetName(pnSet));

			if (pnSet.m_openNodes.Count == 0)
			{
				OutOfNodes();
				return;
			}

			PathNode currentNode = pnSet.m_openNodes.RemoveMin();
			if (currentNode.DistToCur == 0f)
			{
				m_logger.debugLog("first node: " + ReportRelativePosition(currentNode.Position), secondaryState: SetName(pnSet));
				CreatePathNodes(ref currentNode, isForwardSet);
				return;
			}

			if (pnSet.m_reachedNodes.ContainsKey(currentNode.Position.GetHash()))
			{
				//m_logger.debugLog("Already reached: " + ReportRelativePosition(currentNode.Position), secondaryState: SetName(direction));
				return;
			}

			FillEntitiesLists();
			Vector3 repulsion;
			CalcRepulsion(false, out repulsion);
			m_logger.debugLog("Calculated repulsion for some reason: " + repulsion, Logger.severity.WARNING, condition: repulsion != Vector3.Zero, secondaryState: SetName(pnSet));

			Vector3D obstructPosition = m_obstructingEntity.GetPosition();
			PathNode parent = pnSet.m_reachedNodes[currentNode.ParentKey];
			Vector3D worldParent;
			Vector3D.Add(ref obstructPosition, ref parent.Position, out worldParent);
			Vector3D offset; Vector3D.Subtract(ref worldParent, ref m_currentPosition, out offset);

			if (!CanTravelSegment(ref offset, ref currentNode.DirectionFromParent, currentNode.DistToCur - parent.DistToCur))
			{
#if PROFILE
				pnSet.m_unreachableNodes++;
#endif
				//m_logger.debugLog("Not reachable: " + ReportRelativePosition(currentNode.Position), secondaryState: SetName(pnSet));
				//ShowPosition(currentNode);
				return;
			}
			m_logger.debugLog("Reached node: " + ReportRelativePosition(currentNode.Position) + " from " + ReportRelativePosition(pnSet.m_reachedNodes[currentNode.ParentKey].Position) + ", reached: " + pnSet.m_reachedNodes.Count + ", open: " + pnSet.m_openNodes.Count, secondaryState: SetName(pnSet));
			long cNodePosHash = currentNode.Position.GetHash();
			pnSet.m_reachedNodes.Add(cNodePosHash, currentNode);
			//ShowPosition(currentNode, SetName(pnSet));

			if (!m_canChangeCourse)
			{
				m_logger.debugLog("Running backwards search", Logger.severity.ERROR, condition: !isForwardSet, secondaryState: SetName(pnSet));
				Vector3D.Subtract(ref m_currentPosition, ref m_backward.m_startPosition, out offset);
				if (CanTravelSegment(ref offset, ref currentNode.DirectionFromParent, currentNode.DistToCur - parent.DistToCur))
				{
					m_logger.debugLog("Reached destination from node: " + ReportRelativePosition(currentNode.Position), secondaryState: SetName(pnSet));
					BuildPath(currentNode.Key, m_backward.m_startPosition.GetHash());
				}
				return;
			}

			PathNodeSet otherDirection = isForwardSet ? m_backward : m_forward;
			if (otherDirection.m_reachedNodes.ContainsKey(cNodePosHash))
			{
				m_logger.debugLog("Other direction has same position", secondaryState: SetName(pnSet));
				BuildPath(cNodePosHash, cNodePosHash);
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

				if (CanTravelSegment(ref offset, ref currentNode.Position, ref otherDirection.m_startPosition))
				{
					m_logger.debugLog("Blue sky to opposite start", secondaryState: SetName(pnSet));
					//Logger.DebugNotify("Blue Sky to Opposite", level: Logger.severity.INFO);
					if (isForwardSet)
						BuildPath(currentNode.Key, otherDirection.m_startPosition.GetHash());
					else
						BuildPath(otherDirection.m_startPosition.GetHash(), currentNode.Key);
					return;
				}

				foreach (Vector3D otherBlueSky in otherDirection.m_blueSkyNodes)
				{
					if (CanTravelSegment(ref offset, currentNode.Position, otherBlueSky))
					{
						m_logger.debugLog("Blue sky path", secondaryState: SetName(pnSet));
						//Logger.DebugNotify("Blue Sky Path", level: Logger.severity.INFO);
						BuildPath(pnSet.m_blueSkyNodes == m_forward.m_blueSkyNodes ? currentNode.Position.GetHash() : otherBlueSky.GetHash());
						return;
					}
				}

				pnSet.m_blueSkyNodes.Add(currentNode.Position);
			}
			
			//DebugRectClose(ref otherDirection, currentNode.Position);
			CreatePathNodes(ref currentNode, isForwardSet);
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
					m_logger.debugLog("Obstructed by voxel " + hitVoxel + " at " + hitPosition, Logger.severity.DEBUG, condition: hitVoxel != m_obstructingEntity.Entity);
					Profiler.EndProfileBlock();
					return false;
				}
			}

			//m_logger.debugLog("checking " + m_entitiesPruneAvoid.Count + " entites - voxels");

			if (m_entitiesPruneAvoid.Count != 0)
				for (int i = m_entitiesPruneAvoid.Count - 1; i >= 0; i--)
				{
					MyEntity entity = m_entitiesPruneAvoid[i];
					if (entity is MyVoxelBase)
						// already checked
						continue;
					MyCubeBlock obstructBlock;
					float distance;
					if (m_tester.ObstructedBy(entity, ignoreBlock, ref offset, ref direction, length, out obstructBlock, out distance))
					{
						m_logger.debugLog("Obstructed by " + entity.nameWithId() + "." + obstructBlock, Logger.severity.DEBUG, condition: entity != m_obstructingEntity.Entity);
						Profiler.EndProfileBlock();
						return false;
					}
				}

			//m_logger.debugLog("No obstruction. Start: " + (m_currentPosition + offset) + ", finish: " + (m_currentPosition + offset + (line.To - line.From)));
			Profiler.EndProfileBlock();
			return true;
		}

		private void CreatePathNodes(ref PathNode currentNode, bool isForwardSet)
		{
			if (!m_canChangeCourse)
			{
				m_logger.debugLog("Not forward set", Logger.severity.ERROR, condition: !isForwardSet, secondaryState: SetName(isForwardSet ? m_forward : m_backward));
				CreatePathNodeLine(ref currentNode);
				return;
			}

			long currentKey = currentNode.Position.GetHash();
			foreach (Vector3I neighbour in Globals.NeighboursOne)
				CreatePathNode(currentKey, ref currentNode, neighbour, 1f, isForwardSet);
			foreach (Vector3I neighbour in Globals.NeighboursTwo)
				CreatePathNode(currentKey, ref currentNode, neighbour, MathHelper.Sqrt2, isForwardSet);
			foreach (Vector3I neighbour in Globals.NeighboursThree)
				CreatePathNode(currentKey, ref currentNode, neighbour, MathHelper.Sqrt3, isForwardSet);
		}

		private void CreatePathNode(long parentKey, ref PathNode parent, Vector3I neighbour, float distMulti, bool isForwardSet)
		{
			Profiler.StartProfileBlock();
			PathNodeSet pnSet = isForwardSet ? m_forward : m_backward;

			Vector3D position = parent.Position + neighbour * m_nodeDistance;
			// round position so that is a discrete number of steps from destination
			Vector3D finishToPosition; Vector3D.Subtract(ref position, ref m_backward.m_startPosition, out finishToPosition);
			VectorExtensions.RoundTo(ref finishToPosition, m_nodeDistance);
			Vector3D.Add(ref m_backward.m_startPosition, ref finishToPosition, out position);

			//position = new Vector3D(Math.Round(position.X), Math.Round(position.Y), Math.Round(position.Z));
			long positionHash = position.GetHash();

			if (pnSet.m_reachedNodes.ContainsKey(positionHash))
			{
				Profiler.EndProfileBlock();
				return;
			}

			//Vector3D disp; Vector3D.Subtract(ref position, ref parent.Position, out disp);
			//Vector3 neighbourF = neighbour;
			//Vector3 direction; Vector3.Divide(ref neighbourF, distMulti, out direction);

			PathNode result = new PathNode(ref parent, ref position);

			float turn; Vector3.Dot(ref parent.DirectionFromParent, ref result.DirectionFromParent, out turn);

			if (turn < 0f)
			{
				Profiler.EndProfileBlock();
				return;
			}

			Vector3D obstructionPosition = m_obstructingEntity.GetPosition();
			Vector3D currentWorldPosition; Vector3D.Add(ref position, ref obstructionPosition, out currentWorldPosition);
			Vector3D dest = isForwardSet ? m_destWorld : m_currentPosition;
			Vector3D dispToDest; Vector3D.Subtract(ref dest, ref currentWorldPosition, out dispToDest);
			//double distToDest = Math.Abs(dispToDest.X) + Math.Abs(dispToDest.Y) + Math.Abs(dispToDest.Z);
			//float resultKey = distToCur + (float)distToDest;

			float resultKey = result.DistToCur + (float)MinPathDistance(ref dispToDest);

			if (turn > 0.99f && parent.ParentKey != 0f)
			{
				//m_logger.debugLog("Skipping parent node. Turn: " + turn);
				parentKey = parent.ParentKey;
			}
			else
			{
				if (turn < 0f)
				{
					Profiler.EndProfileBlock();
					return;
				}
				//m_logger.debugLog("Pathfinder node backtracks to parent. Position: " + position + ", parent position: " + parent.Position +
				//	"\ndirection: " + direction + ", parent direction: " + parent.DirectionFromParent, Logger.severity.FATAL, condition: turn < -0.99f, secondaryState: SetName(pnSet));
				resultKey += 300f * (1f - turn);
			}

			//PathNode result = new PathNode()
			//{
			//	ParentKey = parentKey,
			//	DistToCur = distToCur,
			//	Position = position,
			//	DirectionFromParent = direction,
			//};

			m_logger.debugLog("DirectionFromParent is incorrect. DirectionFromParent: " + result.DirectionFromParent + ", parent: " + parent.Position + ", current: " + result.Position + ", direction: " +
				Vector3.Normalize(result.Position - parent.Position), Logger.severity.ERROR, condition: !Vector3.Normalize(result.Position - parent.Position).Equals(result.DirectionFromParent, 0.01f));
			m_logger.debugLog("Length is incorrect. Length: " + (result.DistToCur - parent.DistToCur) + ", distance: " + Vector3D.Distance(result.Position, parent.Position), Logger.severity.ERROR,
				condition: Math.Abs((result.DistToCur - parent.DistToCur) - (Vector3D.Distance(result.Position, parent.Position))) > 0.01f);

			m_logger.debugLog("resultKey <= 0", Logger.severity.ERROR, condition: resultKey <= 0f, secondaryState: SetName(pnSet));
			//m_logger.debugLog("path node: " + resultKey + ", " + result.ParentKey + ", " + result.DistToCur + ", " + result.Position + " => " + (m_obstructingEntity.GetPosition() + result.Position) + ", " + result.DirectionFromParent, secondaryState: SetName(pnSet));
			//m_logger.debugLog("Path node positon: " + ReportRelativePosition(result.Position) + " from " + m_backward.m_startPosition + " + " + finishToPosition + ". original position: " + (parent.Position + neighbour * m_nodeDistance),
			//	secondaryState: SetName(pnSet));
			pnSet.AddOpenNode(ref result, resultKey);
			//ShowPosition(result, resultKey.ToString());
			Profiler.EndProfileBlock();
		}

		/// <summary>
		/// Create a PathNode between parent and destination. Only for forward search.
		/// </summary>
		private void CreatePathNodeLine(ref PathNode parent)
		{
			Vector3D direction = parent.DirectionFromParent;
			Vector3D disp; Vector3D.Multiply(ref direction, m_nodeDistance, out disp);
			Vector3D position; Vector3D.Add(ref parent.Position, ref disp, out position);

			PathNode result = new PathNode()
			{
				DirectionFromParent = parent.DirectionFromParent,
				DistToCur = parent.DistToCur + m_nodeDistance,
				ParentKey = parent.Key,
				Position = position
			};
			// do not bother with key as there will only be one open node
			m_forward.AddOpenNode(ref result, 0f);

			m_logger.debugLog("Next position: " + ReportRelativePosition(result.Position));
		}

		private double MinPathDistance(ref Vector3D displacement)
		{
			//return Math.Abs(displacement.X) + Math.Abs(displacement.Y) + Math.Abs(displacement.Z); // going for more of a current-best approach

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

			m_logger.debugLog("Sorting failed: " + X + ", " + Y + ", " + Z, Logger.severity.ERROR, condition: X > Y || X > Z || Y > Z);

			return X * MathHelper.Sqrt3 + (Y - X) * MathHelper.Sqrt2 + Z - Y;
		}

		private void OutOfNodes()
		{
			if (m_nodeDistance >= 2f)
			{
				m_logger.debugLog("No path found, halving node distance to " + (m_nodeDistance * 0.5f), Logger.severity.INFO);
				//Logger.DebugNotify("Halving node distance", level: Logger.severity.INFO);
				m_nodeDistance *= 0.5f;

				if (m_forward.m_reachedNodes.Count > 1)
				{
					PathNode node;
					IEnumerator<PathNode> enumerator = m_forward.m_reachedNodes.Values.GetEnumerator();
					while (enumerator.MoveNext())
					{
						node = enumerator.Current;
						CreatePathNodes(ref node, true);
					}
					enumerator.Dispose();
				}
				else
					m_forward.Clear();

				if (m_backward.m_reachedNodes.Count > 1)
				{
					PathNode node;
					IEnumerator<PathNode> enumerator = m_backward.m_reachedNodes.Values.GetEnumerator();
					while (enumerator.MoveNext())
					{
						node = enumerator.Current;
						CreatePathNodes(ref node, false);
					}
					enumerator.Dispose();
				}
				else
					m_backward.Clear();

				FindAPath();
				return;
			}

			if (m_forward.m_reachedNodes.Count > 1)
			{
				PathNode closest;
				JustMove(100f, out closest);
				m_logger.debugLog("Building path to arbitrary node: " + ReportRelativePosition(closest.Position));
				BuildPath(closest.Position.GetHash());
				return;
			}

			// with line, failing is "normal"
			m_logger.debugLog("Pathfinding failed", m_canChangeCourse ? Logger.severity.WARNING : Logger.severity.DEBUG);
			Logger.DebugNotify("Pathfinding failed", 10000, m_canChangeCourse ? Logger.severity.WARNING : Logger.severity.DEBUG);

#if PROFILE
			LogStats();
#endif

			PathfindingFailed();
			return;
		}

		private void JustMove(float target, out PathNode closest)
		{
			m_logger.debugLog("target is too low: " + target + ", node dist: " + m_nodeDistance, Logger.severity.ERROR, condition: target < m_nodeDistance);
			closest = default(PathNode);
			closest.DistToCur = float.MaxValue;
			foreach (KeyValuePair<long, PathNode> pair in m_forward.m_reachedNodes)
			{
				float distToCur = pair.Value.DistToCur;
				if (distToCur != 0f && Math.Abs(target - pair.Value.DistToCur) < Math.Abs(target - closest.DistToCur))
					closest = pair.Value;
			}
			Logger.DebugNotify("Build path to arbitrary node", level: Logger.severity.DEBUG);
		}

		private void PathfindingFailed()
		{
			m_pathfinding = false;
			m_waitUntil = Globals.UpdateCount + 600uL;
			CurrentState = State.FailedToFindPath;
		}

		private void BuildPath(long forwardHash, long backwardHash = 0L)
		{
			m_path.Clear();
			PurgeTempGPS();

			PathNode node;
			if (!m_forward.m_reachedNodes.TryGetValue(forwardHash, out node))
			{
				m_logger.alwaysLog("Parent hash " + forwardHash + " not found in forward set, failed to build path", Logger.severity.ERROR);
				if (m_backward.m_reachedNodes.ContainsKey(forwardHash))
					m_logger.alwaysLog("Backward set does contains hash", Logger.severity.DEBUG);
				PathfindingFailed();
				return;
			}
			while (node.DistToCur != 0f)
			{
				//ShowPosition(node, "Path");
				m_path.AddFront(ref node.Position);
				if (!m_forward.m_reachedNodes.TryGetValue(node.ParentKey, out node))
				{
					m_logger.alwaysLog("Child hash " + forwardHash + " not found in forward set, failed to build path", Logger.severity.ERROR);
					if (m_backward.m_reachedNodes.ContainsKey(forwardHash))
						m_logger.alwaysLog("Backward set does contains hash", Logger.severity.DEBUG);
					PathfindingFailed();
					return;
				}
			}
			m_path.AddFront(ref node.Position);

			if (backwardHash != 0L)
			{
				if (!m_backward.m_reachedNodes.TryGetValue(backwardHash, out node))
				{
					m_logger.alwaysLog("Parent hash " + backwardHash + " not found in backward set, failed to build path", Logger.severity.ERROR);
					if (m_forward.m_reachedNodes.ContainsKey(forwardHash))
						m_logger.alwaysLog("Forward set does contains hash", Logger.severity.DEBUG);
					PathfindingFailed();
					return;
				}

				if (forwardHash == backwardHash && node.ParentKey != 0L)
				{
					if (!m_backward.m_reachedNodes.TryGetValue(node.ParentKey, out node))
					{
						m_logger.alwaysLog("First child hash " + backwardHash + " not found in backward set, failed to build path", Logger.severity.ERROR);
						if (m_forward.m_reachedNodes.ContainsKey(forwardHash))
							m_logger.alwaysLog("Forward set does contains hash", Logger.severity.DEBUG);
						PathfindingFailed();
						return;
					}
				}
				while (node.DistToCur != 0f)
				{
					//ShowPosition(node, "Path");
					m_path.AddBack(ref node.Position);
					if (!m_backward.m_reachedNodes.TryGetValue(node.ParentKey, out node))
					{
						m_logger.alwaysLog("Child hash " + backwardHash + " not found in backward set, failed to build path", Logger.severity.ERROR);
						if (m_forward.m_reachedNodes.ContainsKey(forwardHash))
							m_logger.alwaysLog("Forward set does contains hash", Logger.severity.DEBUG);
						PathfindingFailed();
						return;
					}
				}
			}

#if PROFILE
			LogStats();
#endif

			m_pathfinding = false;
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
				m_logger.debugLog("Trying potential target #" + target + ": " + m_path.m_postions[target]);
				if (CanTravelSegment(ref Vector3D.Zero, currentPosition, m_path.m_postions[target]))
				{
					m_logger.debugLog("Next target is position #" + target);
					m_path.m_target = target;
					return true;
				}
			}

			m_logger.debugLog("Failed to set next target", Logger.severity.INFO);
			return false;
		}

		private string ReportRelativePosition(Vector3D position)
		{
			return position + " => " + (position + m_obstructingEntity.GetPosition());
		}

		private void LogStats()
		{
			foreach (Vector3D position in m_path.m_postions)
				m_logger.alwaysLog("Waypoint: " + ReportRelativePosition(position));
#if PROFILE
			m_logger.alwaysLog("Nodes reached: " + (m_forward.m_reachedNodes.Count + m_backward.m_reachedNodes.Count) + ", unreachable: " + (m_forward.m_unreachableNodes + m_backward.m_unreachableNodes) +
				", open: " + (m_forward.m_openNodes.Count + m_backward.m_openNodes.Count) + ", path length: " + m_path.Count);
#else
			m_logger.alwaysLog("Nodes reached: " + (m_forward.m_reachedNodes.Count + m_backward.m_reachedNodes.Count) +
				", open: " + (m_forward.m_openNodes.Count + m_backward.m_openNodes.Count) + ", path length: " + m_path.Count);
#endif
		}

#if LOG_ENABLED
		private Queue<IMyGps> m_shownPositions = new Queue<IMyGps>(100);
		private List<IMyGps> m_allGpsList = new List<IMyGps>();
#endif

		[System.Diagnostics.Conditional("LOG_ENABLED")]
		private void ShowPosition(PathNode currentNode, string name = null)
		{
			IMyGps gps = MyAPIGateway.Session.GPS.Create(name ?? currentNode.Position.ToString(), string.Empty, currentNode.Position + m_obstructingEntity.GetPosition(), true, true);
			gps.DiscardAt = MyAPIGateway.Session.ElapsedPlayTime;
			//m_logger.debugLog("Showing " + gps.Coords);
			MyAPIGateway.Utilities.TryInvokeOnGameThread(() => MyAPIGateway.Session.GPS.AddLocalGps(gps));
#if LOG_ENABLED
			m_shownPositions.Enqueue(gps);
			if (m_shownPositions.Count == 100)
			{
				IMyGps remove = m_shownPositions.Dequeue();
				//m_logger.debugLog("Removing " + remove.Coords);
				MyAPIGateway.Utilities.TryInvokeOnGameThread(() => MyAPIGateway.Session.GPS.RemoveLocalGps(remove));
			}
#endif
		}

		[System.Diagnostics.Conditional("LOG_ENABLED")]
		private void PurgeTempGPS()
		{
#if LOG_ENABLED
			m_shownPositions.Clear();
			MyAPIGateway.Utilities.TryInvokeOnGameThread(() => {
				foreach (IMyGps gps in MyAPIGateway.Session.GPS.GetGpsList(MyAPIGateway.Session.Player.IdentityId))
					if (gps.DiscardAt.HasValue && gps.DiscardAt.Value < MyAPIGateway.Session.ElapsedPlayTime)
						MyAPIGateway.Session.GPS.RemoveLocalGps(gps);
			});
#endif
		}

		private string SetName(PathNodeSet pnSet)
		{
			return pnSet.m_openNodes == m_forward.m_openNodes ? "Forward" : "Backward";
		}

		[System.Diagnostics.Conditional("LOG_ENABLED")]
		private void DebugRectClose(ref PathNodeSet pnSet, Vector3D position)
		{
			foreach (PathNode otherNode in pnSet.m_reachedNodes.Values)
				if (otherNode.Position != m_forward.m_startPosition)
				{
					double distRect = Vector3D.RectangularDistance(position, otherNode.Position);
					if (distRect < m_nodeDistance)
					{
						Vector3D positionSteps; StepCount(ref position, out positionSteps);
						Vector3D otherNodePosition = otherNode.Position;
						Vector3D otherSteps; StepCount(ref otherNodePosition, out otherSteps);
						m_logger.debugLog(ReportRelativePosition(position) + " @ " + positionSteps + " steps is less than " + m_nodeDistance + " m from: " + ReportRelativePosition(otherNode.Position) + " @ " + otherSteps + " steps", Logger.severity.WARNING);
					}
				}
		}

		private void StepCount(ref Vector3D position, out Vector3D steps)
		{
			Vector3D finishToPosition; Vector3D.Subtract(ref position, ref m_backward.m_startPosition, out finishToPosition);
			Vector3D.Divide(ref finishToPosition, m_nodeDistance, out steps);
		}

	}
}
