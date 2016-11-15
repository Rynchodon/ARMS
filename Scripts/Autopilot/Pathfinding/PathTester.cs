using System;
using System.Collections.Generic;
using System.Linq;
using Rynchodon.Attached;
using Rynchodon.Utility;
using Rynchodon.Utility.Collections;
using Sandbox.Common.ObjectBuilders;
using Sandbox.Game.Entities;
using Sandbox.Game.Entities.Cube;
using Sandbox.Game.Weapons;
using VRage.Game.Components;
using VRage.Game.Entity;
using VRage.Game.ModAPI;
using VRageMath;

namespace Rynchodon.Autopilot.Pathfinding
{
	public class PathTester
	{

		static PathTester()
		{
			Vector2IMatrix<Vector3D>.EqualityComparer = new EqualityComparer_Vector3D();
		}

		private const float StartRayCast = 2.5f;

		private LineSegmentD m_lineSegment = new LineSegmentD();
		private List<Vector2I> m_insideRing = new List<Vector2I>();

		public ShipControllerBlock Controller;
		public MyCubeGrid AutopilotGrid;

		public PathTester(ShipControllerBlock controller)
		{
			this.Controller = controller;
			this.AutopilotGrid = (MyCubeGrid)controller.CubeGrid;
		}

		/// <summary>
		/// Convert a vector to a grid position, given a grid size.
		/// </summary>
		/// <param name="vector">The position in metres</param>
		/// <param name="gridSize">The size of the grid</param>
		private Vector2I ToCell(Vector2 vector, float gridSize)
		{
			return new Vector2I((int)Math.Round(vector.X / gridSize), (int)Math.Round(vector.Y / gridSize));
		}

		/// <summary>
		/// For testing the current path of autopilot, accounts for current velocity.
		/// </summary>
		/// <param name="targetDirection">The direction the autopilot wants to travel in.</param>
		public bool ObstructedBy(MyEntity entity, MyCubeBlock ignoreBlock, ref Vector3 targetDirection, float targetDistance, out MyCubeBlock obstructBlock, out float distance)
		{
			//Logger.DebugLog("checking: " + entityTopMost.getBestName() + ", targetDirection: " + targetDirection);

			Vector3D currentPosition = AutopilotGrid.GetCentre();

			Vector3 autopilotVelocity = AutopilotGrid.Physics.LinearVelocity;
			Vector3 relativeVelocity;
			MyPhysicsComponentBase physics = entity.GetTopMostParent().Physics;
			if (physics == null || physics.IsStatic)
			{
				relativeVelocity = autopilotVelocity;
			}
			else
			{
				Vector3 entityVelocity = physics.LinearVelocity;
				Vector3.Subtract(ref autopilotVelocity, ref entityVelocity, out relativeVelocity);
			}

			Vector3 rejectionVector; Vector3.Add(ref relativeVelocity, ref targetDirection, out rejectionVector);
			targetDistance += rejectionVector.Normalize();

			return ObstructedBy(entity, ignoreBlock, ref Vector3D.Zero, ref rejectionVector, targetDistance, out obstructBlock, out distance);
		}

		/// <param name="offset">Added to current position of ship's blocks.</param>
		/// <param name="rejectionVector">Direction of travel of autopilot ship, should be normalized</param>
		public bool ObstructedBy(MyEntity entity, MyCubeBlock ignoreBlock, ref Vector3D offset, ref Vector3 rejectionVector, float rejectionDistance, out MyCubeBlock obstructBlock, out float distance)
		{
			//Logger.DebugLog("checking: " + entityTopMost.getBestName() + ", offset: " + offset + ", rejection vector: " + rejectionVector);
			Logger.DebugLog("rejection vector is invalid", Logger.severity.FATAL, condition: !rejectionVector.IsValid());

			MyCubeGrid grid = entity as MyCubeGrid;
			if (grid != null)
			{
				// check for dangerous tools on grid
				CubeGridCache cache = CubeGridCache.GetFor(grid);
				if (cache == null)
				{
					obstructBlock = null;
					distance = 0f;
					return true;
				}
				Profiler.StartProfileBlock("Checking Tools");
				foreach (MyShipDrill drill in cache.BlocksOfType(typeof(MyObjectBuilder_Drill)))
					if (drill.IsShooting)
						if (SphereTest(drill, ref offset, ref rejectionVector, rejectionDistance, out obstructBlock, out distance))
						{
							Profiler.EndProfileBlock();
							return true;
						}
				foreach (MyShipGrinder grinder in cache.BlocksOfType(typeof(MyObjectBuilder_ShipGrinder)))
					if (grinder.IsShooting)
						if (SphereTest(grinder, ref offset, ref rejectionVector, rejectionDistance, out obstructBlock, out distance))
						{
							Profiler.EndProfileBlock();
							return true;
						}
				Profiler.EndProfileBlock();

				if (ExtensionsRelations.canConsiderFriendly(Controller.CubeBlock, grid) && EndangerGrid(grid, ref offset, ref rejectionVector, rejectionDistance, out distance))
				{
					Logger.DebugLog("Movement would endanger: " + grid.getBestName());
					obstructBlock = null;
					return true;
				}

				Profiler.StartProfileBlock("RejectionIntersects");
				Vector3I hitCell;
				if (RejectionIntersects(grid, ignoreBlock, ref rejectionVector, rejectionDistance, ref offset, out hitCell, out distance))
				{
					MySlimBlock slim = grid.GetCubeBlock(hitCell);
					obstructBlock = slim == null ? null : slim.FatBlock;
					Profiler.EndProfileBlock();
					//Logger.DebugLog("rejection intersects, block: " + (slim != null ? slim.getBestName() : "N/A") + " at " + grid.GridIntegerToWorld(hitCell));
					return true;
				}
				//else
				//	Logger.DebugLog("rejection does not intersect");
				Profiler.EndProfileBlock();
			}
			else
				return SphereTest(entity, ref offset, ref rejectionVector, rejectionDistance, out obstructBlock, out distance);

			obstructBlock = null;
			distance = 0f;
			return false;
		}

		/// <param name="oGrid">The grid that may obstruct this one.</param>
		/// <param name="ignoreBlock">Block to ignore, or null</param>
		/// <param name="rejectionVector">Direction of travel of autopilot ship, should be normalized</param>
		/// <param name="offset">Difference between the current position of the autopilot ship and where it will be.</param>
		private bool RejectionIntersects(MyCubeGrid oGrid, MyCubeBlock ignoreBlock, ref Vector3 rejectionVector, float rejectionDistance, ref Vector3D offset, out Vector3I oGridCell, out float distance)
		{
			//Logger.DebugLog("Rejection vector is not normalized, length squared: " + rejectionVector.LengthSquared(), Logger.severity.FATAL, condition: Math.Abs(rejectionVector.LengthSquared() - 1f) > 0.001f);
			//Logger.DebugLog("Testing for rejection intersection: " + oGrid.nameWithId() + ", starting from: " + (AutopilotGrid.GetCentre() + offset) + ", rejection vector: " + rejectionVector + ", distance: " + rejectionDistance +
			//	", final: " + (AutopilotGrid.GetCentre() + offset + rejectionVector * rejectionDistance));
			//Logger.DebugLog("rejction distance < 0: " + rejectionDistance, Logger.severity.ERROR, condition: rejectionDistance < 0f);

			IEnumerable<CubeGridCache> myCaches = AttachedGrid.AttachedGrids(AutopilotGrid, AttachedGrid.AttachmentKind.Physics, true).Select(CubeGridCache.GetFor);
			Vector3D currentPosition = AutopilotGrid.GetCentre();

			CubeGridCache oCache = CubeGridCache.GetFor(oGrid);
			if (oCache == null)
			{
				Logger.DebugLog("Failed to get cache for other grid", Logger.severity.DEBUG);
				oGridCell = Vector3I.Zero;
				distance = 0f;
				return false;
			}

			bool checkBlock = ignoreBlock != null && oGrid == ignoreBlock.CubeGrid;

			Vector3 v; rejectionVector.CalculatePerpendicularVector(out v);
			Vector3 w; Vector3.Cross(ref rejectionVector, ref v, out w);
			Matrix to3D = new Matrix(v.X, v.Y, v.Z, 0f,
				w.X, w.Y, w.Z, 0f,
				rejectionVector.X, rejectionVector.Y, rejectionVector.Z, 0f,
				0f, 0f, 0f, 1f);
			Matrix to2D; Matrix.Invert(ref to3D, out to2D);

			float roundTo;
			int steps;
			if (AutopilotGrid.GridSizeEnum == oGrid.GridSizeEnum)
			{
				roundTo = AutopilotGrid.GridSize;
				steps = 1;
			}
			else
			{
				roundTo = Math.Min(AutopilotGrid.GridSize, oGrid.GridSize);
				steps = (int)Math.Ceiling(Math.Max(AutopilotGrid.GridSize, oGrid.GridSize) / roundTo);
			}
			//if (extraRadius)
			//	steps *= 2;

			//Logger.DebugLog("round to: " + roundTo + ", steps: " + steps);
			//Logger.DebugLog("building m_rejections");

			Vector2IMatrix<bool> apShipRejections;
			ResourcePool.Get(out apShipRejections);

			MatrixD worldMatrix = AutopilotGrid.WorldMatrix;
			float gridSize = AutopilotGrid.GridSize;
			float minProjection = float.MaxValue, maxProjection = float.MinValue; // the permitted range when rejecting the other grids cells
			foreach (CubeGridCache cache in myCaches)
			{
				if (cache == null)
				{
					Logger.DebugLog("Missing a cache", Logger.severity.DEBUG);
					oGridCell = Vector3I.Zero;
					distance = 0f;
					return false;
				}
				foreach (Vector3I cell in cache.OccupiedCells())
				{
					Vector3 local = cell * gridSize;
					Vector3D world; Vector3D.Transform(ref local, ref worldMatrix, out world);
					Vector3D relative; Vector3D.Subtract(ref world, ref currentPosition, out relative);
					Vector3 relativeF = relative;

					float projectionDistance; Vector3 rejection;
					VectorExtensions.RejectNormalized(ref relativeF, ref rejectionVector, out projectionDistance, out rejection);
					if (projectionDistance < minProjection)
						minProjection = projectionDistance;
					else if (projectionDistance > maxProjection)
						maxProjection = projectionDistance;

					Vector3 planarComponents; Vector3.Transform(ref rejection, ref to2D, out planarComponents);
					//Logger.DebugLog("Math fail: rejection: " + rejection + ", planar components: " + planarComponents + "\nto3D: " + to3D, Logger.severity.WARNING, condition: planarComponents.Z > 0.001f || planarComponents.Z < -0.001f);
					Vector2 pc2 = new Vector2(planarComponents.X, planarComponents.Y);
					apShipRejections[ToCell(pc2, roundTo)] = true;
					//Logger.DebugLog("My rejection: " + rejection + ", planar: " + ToCell(pc2, roundTo));
				}
			}

			minProjection += StartRayCast; // allow autopilot to move away from a touching object

			//Logger.DebugLog("projection min: " + minProjection + ", max: " + maxProjection + ", max for other: " + (maxProjection + rejectionDistance));
			maxProjection += rejectionDistance;

			//Logger.DebugLog("checking other grid cells");

			Vector2IMatrix<bool> otherGridRejections;
			ResourcePool.Get(out otherGridRejections);

			worldMatrix = oGrid.WorldMatrix;
			gridSize = oGrid.GridSize;
			foreach (Vector3I cell in oCache.OccupiedCells())
			{
				//Logger.DebugLog("cell: " + cell);

				Vector3 local = cell * gridSize;
				Vector3D world; Vector3D.Transform(ref local, ref worldMatrix, out world);
				Vector3D offsetWorld; Vector3D.Subtract(ref world, ref offset, out offsetWorld);
				Vector3D relative; Vector3D.Subtract(ref offsetWorld, ref currentPosition, out relative);
				Vector3 relativeF = relative;

				Vector3 rejection;
				VectorExtensions.RejectNormalized(ref relativeF, ref rejectionVector, out distance, out rejection);
				if (distance < minProjection || distance > maxProjection)
					continue;

				Vector3 planarComponents; Vector3.Transform(ref rejection, ref to2D, out planarComponents);
				//Logger.DebugLog("Math fail: rejection: " + rejection + ", planar components: " + planarComponents + "\nto3D: " + to3D, Logger.severity.WARNING, condition: planarComponents.Z > 0.001f || planarComponents.Z < -0.001f);
				Vector2 pc2 = new Vector2(planarComponents.X, planarComponents.Y);
				Vector2I cell2D = ToCell(pc2, roundTo);

				if (!otherGridRejections.Add(cell2D, true))
				{
					//Logger.DebugLog("Already tested: " + cell2D);
					continue;
				}

				//Logger.DebugLog("Rejection: " + rejection + ", planar: " + cell2D);
				//Logger.DebugLog("testing range. x: " + (cell2D.X - steps) + " - " + (cell2D.X + steps));

				Vector2I test;
				for (test.X = cell2D.X - steps; test.X <= cell2D.X + steps; test.X++)
					for (test.Y = cell2D.Y - steps; test.Y <= cell2D.Y + steps; test.Y++)
						if (apShipRejections.Contains(test))
						{
							if (checkBlock)
							{
								IMySlimBlock slim = oGrid.GetCubeBlock(cell);
								if (slim.FatBlock == ignoreBlock)
									continue;
							}
							oGridCell = cell;
							//Logger.DebugLog("Hit, projectionDistance: " + projectionDistance + ", min: " + minProjection + ", max: " + maxProjection);
							apShipRejections.Clear();
							otherGridRejections.Clear();
							ResourcePool.Return(apShipRejections);
							ResourcePool.Return(otherGridRejections);
							return true;
						}
			}

			oGridCell = Vector3I.Zero;
			apShipRejections.Clear();
			otherGridRejections.Clear();
			ResourcePool.Return(apShipRejections);
			ResourcePool.Return(otherGridRejections);
			distance = 0f;
			return false;
		}

		/// <summary>
		/// Sphere test for simple entities, like floating objects, and for avoiding dangerous ships tools.
		/// </summary>
		/// <param name="entity">The entity to test.</param>
		/// <param name="offset">Difference between current postiong and where the ship will be</param>
		/// <param name="rejectionVector">Direction of movement vector</param>
		/// <param name="rejectionDistance">Distance of movement</param>
		private bool SphereTest(MyEntity entity, ref Vector3D offset, ref Vector3 rejectionVector, float rejectionDistance, out MyCubeBlock obstructBlock, out float distance)
		{
			Vector3D currentPosition = AutopilotGrid.GetCentre();
			Vector3D rejectD = rejectionVector;
			Vector3D disp; Vector3D.Multiply(ref rejectD, rejectionDistance, out disp);
			Vector3D finalPosition; Vector3D.Add(ref currentPosition, ref disp, out finalPosition);
			double shipRadius = AutopilotGrid.PositionComp.LocalVolume.Radius;

			Vector3D obsPos = entity.GetCentre();
			Vector3D offObsPos; Vector3D.Subtract(ref obsPos, ref offset, out offObsPos);
			double obsRadius = entity.PositionComp.LocalVolume.Radius;

			if (entity is MyCubeBlock)
				obsRadius += 10f;

			m_lineSegment.From = currentPosition;
			m_lineSegment.To = finalPosition;
			Vector3D closest;
			distance = (float)m_lineSegment.ClosestPoint(ref offObsPos, out closest);
			double distSq; Vector3D.DistanceSquared(ref offObsPos, ref closest, out distSq);

			double minDist = shipRadius + obsRadius;
			double minDistSq = minDist * minDist;

			if (distSq < minDistSq && IsRejectionTowards(ref obsPos, ref currentPosition, ref rejectD))
			{
				Logger.DebugLog("Rejection " + rejectionVector + " hit " + entity.nameWithId());
				obstructBlock = (MyCubeBlock)entity;
				return true;
			}
			obstructBlock = null;
			return false;
		}

		private bool IsRejectionTowards(ref Vector3D obstructPosition, ref Vector3D currentPosition, ref Vector3D rejectionDirection)
		{
			Vector3D toObstruction; Vector3D.Subtract(ref obstructPosition, ref currentPosition, out toObstruction);
			double dot = toObstruction.Dot(ref rejectionDirection);
			return dot >= 0d;// || dot / toObstruction.Length() >= -0.5d;
		}

		/// <summary>
		/// Tests if autopilot's ship would endanger a grid with a ship tool.
		/// </summary>
		private bool EndangerGrid(MyCubeGrid grid, ref Vector3D offset, ref Vector3 rejectionVector, float rejectionDistance, out float distance)
		{
			Vector3D currentPosition = AutopilotGrid.GetCentre();
			Vector3D obstructPositon = grid.GetCentre();
			Vector3D rejectD = rejectionVector;

			Vector3D gridPosition = grid.GetCentre() - offset;
			double gridRadius = grid.PositionComp.LocalVolume.Radius;

			Vector3 rejectDispF; Vector3.Multiply(ref rejectionVector, rejectionDistance, out rejectDispF);
			Vector3D rejectDisp = rejectDispF;

			IEnumerable<CubeGridCache> myCaches = AttachedGrid.AttachedGrids(AutopilotGrid, AttachedGrid.AttachmentKind.Terminal, true).Select(CubeGridCache.GetFor);
			foreach (CubeGridCache cache in myCaches)
			{
				if (cache == null)
				{
					Logger.DebugLog("Missing a cache", Logger.severity.DEBUG);
					distance = 0f;
					return true;
				}
				foreach (MyShipDrill drill in cache.BlocksOfType(typeof(MyObjectBuilder_Drill)))
					if (drill.IsShooting && ToolObstructed(drill, ref gridPosition, gridRadius, ref rejectDisp, out distance) && IsRejectionTowards(ref obstructPositon, ref currentPosition, ref rejectD))
						return true;
				foreach (MyShipGrinder grinder in cache.BlocksOfType(typeof(MyObjectBuilder_ShipGrinder)))
					if (grinder.IsShooting && ToolObstructed(grinder, ref gridPosition, gridRadius, ref rejectDisp, out distance) && IsRejectionTowards(ref obstructPositon, ref currentPosition, ref rejectD))
						return true;
			}

			distance = 0f;
			return false;
		}

		private bool ToolObstructed(MyCubeBlock drill, ref Vector3D gridPosition, double gridRadius, ref Vector3D rejectDisp, out float distance)
		{
			m_lineSegment.From = drill.PositionComp.GetPosition();
			m_lineSegment.To = m_lineSegment.From + rejectDisp;
			Vector3D closest;
			distance = (float)m_lineSegment.ClosestPoint(ref gridPosition, out closest);
			double distCloseSq; Vector3D.DistanceSquared(ref gridPosition, ref closest, out distCloseSq);
			double required = gridRadius + drill.PositionComp.LocalVolume.Radius + 5f;
			return distCloseSq <= required * required;
		}

		/// <param name="rayDirectionLength">Not normalized, should reflect the distance autopilot needs to travel.</param>
		public bool RayCastIntersectsVoxel(ref Vector3D offset, ref Vector3 rayDirectionLength, out MyVoxelBase hitVoxel, out Vector3D hitPosition)
		{
			Profiler.StartProfileBlock();

			Vector3D currentPosition = AutopilotGrid.GetCentre();
			Vector3 rayDirection; Vector3.Normalize(ref rayDirectionLength, out rayDirection);
			Vector3 startOffset; Vector3.Multiply(ref rayDirection, StartRayCast, out startOffset);
			Vector3D startOffsetD = startOffset;
			Vector3D totalOffset; Vector3D.Add(ref offset, ref startOffsetD, out totalOffset);

			CapsuleD capsule;
			Vector3D.Add(ref currentPosition, ref totalOffset, out capsule.P0);
			Vector3D rayDD = rayDirectionLength;
			Vector3D.Add(ref capsule.P0, ref rayDD, out capsule.P1);
			capsule.Radius = AutopilotGrid.PositionComp.LocalVolume.Radius;
			//Logger.DebugLog("current position: " + currentPosition + ", offset: " + offset + ", line: " + rayDirectionLength + ", start: " + capsule.P0 + ", end: " + capsule.P1);
			if (!CapsuleDExtensions.IntersectsVoxel(ref capsule, out hitVoxel, out hitPosition, true))
			{
				Profiler.EndProfileBlock();
				return false;
			}

			IEnumerable<CubeGridCache> myCaches = AttachedGrid.AttachedGrids(AutopilotGrid, AttachedGrid.AttachmentKind.Physics, true).Select(CubeGridCache.GetFor);

			Vector3 v; rayDirection.CalculatePerpendicularVector(out v);
			Vector3 w; Vector3.Cross(ref rayDirection, ref v, out w);
			Matrix to3D = new Matrix(v.X, v.Y, v.Z, 0f,
				w.X, w.Y, w.Z, 0f,
				rayDirection.X, rayDirection.Y, rayDirection.Z, 0f,
				0f, 0f, 0f, 1f);
			Matrix to2D; Matrix.Invert(ref to3D, out to2D);

			Vector2IMatrix<Vector3D> apShipRejections;
			ResourcePool.Get(out apShipRejections);

			MatrixD worldMatrix = AutopilotGrid.WorldMatrix;
			float gridSize = AutopilotGrid.GridSize;
			foreach (CubeGridCache cache in myCaches)
			{
				if (cache == null)
				{
					Logger.DebugLog("Missing a cache", Logger.severity.DEBUG);
					hitVoxel = null;
					hitPosition = Vector3.Invalid;
					Profiler.EndProfileBlock();
					return false;
				}
				foreach (Vector3I cell in cache.OccupiedCells())
				{
					Vector3 local = cell * gridSize;
					Vector3D world; Vector3D.Transform(ref local, ref worldMatrix, out world);
					Vector3D offsetWorld; Vector3D.Add(ref world, ref totalOffset, out offsetWorld);
					Vector3D relative; Vector3D.Subtract(ref offsetWorld, ref currentPosition, out relative);
					Vector3 relativeF = relative;
					Vector3 rejection; Vector3.Reject(ref relativeF, ref rayDirection, out rejection);
					Vector3 planarComponents; Vector3.Transform(ref rejection, ref to2D, out planarComponents);
					Logger.DebugLog("Math fail: rejection: " + rejection + ", planar components: " + planarComponents + "\nto3D: " + to3D, Logger.severity.FATAL, condition: planarComponents.Z > 0.001f || planarComponents.Z < -0.001f);
					Vector2 pc2 = new Vector2(planarComponents.X, planarComponents.Y);
					apShipRejections.Add(ToCell(pc2, gridSize), offsetWorld);
				}
			}

			Vector2IMatrix<bool> testedRejections;
			ResourcePool.Get(out testedRejections);

			//int tests = 0;
			const int allowedEmpty = 2;
			foreach (KeyValuePair<Vector2I, Vector3D> cell in apShipRejections.MiddleOut())
			{
				//Logger.DebugLog("Cell was not set: " + cell, Logger.severity.FATAL, condition: cell.Value == Vector3D.Zero);

				if (!testedRejections.Add(cell.Key, true))
					continue;

				int ringIndex = 0;
				m_insideRing.Clear();

				int biggestRingSq = 0;
				while (true)
				{
					int consecutiveEmpty = 0;
					ExpandingRings.Ring ring = ExpandingRings.GetRing(ringIndex++);
					foreach (Vector2I ringOffset in ring.Squares)
						if (apShipRejections.Contains(ringOffset + cell.Key))
						{
							consecutiveEmpty = 0;
						}
						else
						{
							consecutiveEmpty++;
							if (consecutiveEmpty > allowedEmpty)
								goto GotRing;
						}
					m_insideRing.AddArray(ring.Squares);
					biggestRingSq = ring.DistanceSquared;
				}

				GotRing:
				foreach (Vector2I ringOffset in m_insideRing)
					testedRejections.Add(ringOffset + cell.Key, true);

				capsule.P0 = cell.Value;
				capsule.P1 = new Vector3D(capsule.P0.X + rayDirectionLength.X, capsule.P0.Y + rayDirectionLength.Y, capsule.P0.Z + rayDirectionLength.Z);
				capsule.Radius = (1f + (float)Math.Sqrt(biggestRingSq)) * gridSize;
				//tests++;
				if (CapsuleDExtensions.IntersectsVoxel(ref capsule, out hitVoxel, out hitPosition, true))
				{
					Profiler.EndProfileBlock();
					apShipRejections.Clear();
					testedRejections.Clear();
					ResourcePool.Return(apShipRejections);
					ResourcePool.Return(testedRejections);
					return true;
				}
			}

			//Logger.DebugLog("Cells: " + apShipRejections.Count + ", tests: " + tests);

			hitVoxel = null;
			hitPosition = Vector3.Invalid;
			Profiler.EndProfileBlock();
			apShipRejections.Clear();
			testedRejections.Clear();
			ResourcePool.Return(apShipRejections);
			ResourcePool.Return(testedRejections);
			return false;
		}

	}
}
