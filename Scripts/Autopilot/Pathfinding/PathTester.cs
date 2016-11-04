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

		private const float StartRayCast = 2.5f;

		/// <summary>Rejections of this grid</summary>
		private Vector2IMatrix<bool> m_rejections = new Vector2IMatrix<bool>();
		/// <summary>Rejections of the other grid.</summary>
		private Vector2IMatrix<bool> m_rejectTests = new Vector2IMatrix<bool>();
		private LineSegmentD m_lineSegment = new LineSegmentD();

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
		public bool ObstructedBy(MyEntity entity, MyCubeBlock ignoreBlock, ref Vector3 targetDirection, float targetDistance, out MyCubeBlock obstructBlock)
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
			rejectionVector.Normalize();

			return ObstructedBy(entity, ignoreBlock, ref Vector3D.Zero, ref rejectionVector, targetDistance, out obstructBlock);
		}

		/// <param name="offset">Added to current position of ship's blocks.</param>
		/// <param name="rejectionVector">Direction of travel of autopilot ship, should be normalized</param>
		public bool ObstructedBy(MyEntity entity, MyCubeBlock ignoreBlock, ref Vector3D offset, ref Vector3 rejectionVector, float rejectionDistance, out MyCubeBlock obstructBlock)
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
					return true;
				}
				Profiler.StartProfileBlock("Checking Tools");
				foreach (MyShipDrill drill in cache.BlocksOfType(typeof(MyObjectBuilder_Drill)))
					if (drill.IsShooting)
						if (SphereTest(drill, ref offset, ref rejectionVector, rejectionDistance, out obstructBlock))
							return true;
				foreach (MyShipGrinder grinder in cache.BlocksOfType(typeof(MyObjectBuilder_ShipGrinder)))
					if (grinder.IsShooting)
						if (SphereTest(grinder, ref offset, ref rejectionVector, rejectionDistance, out obstructBlock))
							return true;
				Profiler.EndProfileBlock();

				if (ExtensionsRelations.canConsiderFriendly(Controller.CubeBlock, grid) && EndangerGrid(grid, ref offset, ref rejectionVector, rejectionDistance))
				{
					Logger.DebugLog("Movement would endanger: " + grid.getBestName());
					obstructBlock = null;
					return true;
				}

				Profiler.StartProfileBlock("RejectionIntersects");
				Vector3I hitCell;
				if (RejectionIntersects(grid, ignoreBlock, ref rejectionVector, rejectionDistance, ref offset, out hitCell))
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
				return SphereTest(entity, ref offset, ref rejectionVector, rejectionDistance, out obstructBlock);

			obstructBlock = null;
			return false;
		}

		/// <param name="oGrid">The grid that may obstruct this one.</param>
		/// <param name="ignoreBlock">Block to ignore, or null</param>
		/// <param name="rejectionVector">Direction of travel of autopilot ship, should be normalized</param>
		/// <param name="offset">Difference between the current position of the autopilot ship and where it will be.</param>
		private bool RejectionIntersects(MyCubeGrid oGrid, MyCubeBlock ignoreBlock, ref Vector3 rejectionVector, float rejectionDistance, ref Vector3D offset, out Vector3I oGridCell)
		{
			Logger.DebugLog("Rejection vector is not normalized, length squared: " + rejectionVector.LengthSquared(), Logger.severity.FATAL, condition: Math.Abs(rejectionVector.LengthSquared() - 1f) > 0.001f);
			//Logger.DebugLog("Testing for rejection intersection: " + oGrid.nameWithId() + ", starting from: " + (AutopilotGrid.GetCentre() + offset) + ", rejection vector: " + rejectionVector + ", distance: " + rejectionDistance +
			//	", final: " + (AutopilotGrid.GetCentre() + offset + rejectionVector * rejectionDistance));
			Logger.DebugLog("rejction distance < 0: " + rejectionDistance, Logger.severity.ERROR, condition: rejectionDistance < 0f);

			IEnumerable<CubeGridCache> myCaches = AttachedGrid.AttachedGrids(AutopilotGrid, AttachedGrid.AttachmentKind.Physics, true).Select(CubeGridCache.GetFor);
			Vector3D currentPosition = AutopilotGrid.GetCentre();

			CubeGridCache oCache = CubeGridCache.GetFor(oGrid);
			if (oCache == null)
			{
				Logger.DebugLog("Failed to get cache for other grid", Logger.severity.DEBUG);
				oGridCell = Vector3I.Zero;
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

			m_rejections.Clear();
			MatrixD worldMatrix = AutopilotGrid.WorldMatrix;
			float gridSize = AutopilotGrid.GridSize;
			float minProjection = float.MaxValue, maxProjection = float.MinValue; // the permitted range when rejecting the other grids cells
			foreach (CubeGridCache cache in myCaches)
			{
				if (cache == null)
				{
					Logger.DebugLog("Missing a cache", Logger.severity.DEBUG);
					oGridCell = Vector3I.Zero;
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
					Logger.DebugLog("Math fail: rejection: " + rejection + ", planar components: " + planarComponents + "\nto3D: " + to3D, Logger.severity.WARNING, condition: planarComponents.Z > 0.001f || planarComponents.Z < -0.001f);
					Vector2 pc2 = new Vector2(planarComponents.X, planarComponents.Y);
					m_rejections[ToCell(pc2, roundTo)] = true;
					//Logger.DebugLog("My rejection: " + rejection + ", planar: " + ToCell(pc2, roundTo));
				}
			}

			minProjection += StartRayCast; // allow autopilot to move away from a touching object

			//Logger.DebugLog("projection min: " + minProjection + ", max: " + maxProjection + ", max for other: " + (maxProjection + rejectionDistance));
			maxProjection += rejectionDistance;

			//Logger.DebugLog("checking other grid cells");

			m_rejectTests.Clear();
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

				float projectionDistance; Vector3 rejection;
				VectorExtensions.RejectNormalized(ref relativeF, ref rejectionVector, out projectionDistance, out rejection);
				if (projectionDistance < minProjection || projectionDistance > maxProjection)
					continue;

				Vector3 planarComponents; Vector3.Transform(ref rejection, ref to2D, out planarComponents);
				Logger.DebugLog("Math fail: rejection: " + rejection + ", planar components: " + planarComponents + "\nto3D: " + to3D, Logger.severity.WARNING, condition: planarComponents.Z > 0.001f || planarComponents.Z < -0.001f);
				Vector2 pc2 = new Vector2(planarComponents.X, planarComponents.Y);
				Vector2I cell2D = ToCell(pc2, roundTo);

				if (!m_rejectTests.Add(cell2D, true))
				{
					//Logger.DebugLog("Already tested: " + cell2D);
					continue;
				}

				//Logger.DebugLog("Rejection: " + rejection + ", planar: " + cell2D);
				//Logger.DebugLog("testing range. x: " + (cell2D.X - steps) + " - " + (cell2D.X + steps));

				Vector2I test;
				for (test.X = cell2D.X - steps; test.X <= cell2D.X + steps; test.X++)
					for (test.Y = cell2D.Y - steps; test.Y <= cell2D.Y + steps; test.Y++)
						if (m_rejections.Contains(test))
						{
							if (checkBlock)
							{
								IMySlimBlock slim = oGrid.GetCubeBlock(cell);
								if (slim.FatBlock == ignoreBlock)
									continue;
							}
							oGridCell = cell;
							Logger.DebugLog("Hit, projectionDistance: " + projectionDistance + ", min: " + minProjection + ", max: " + maxProjection);
							return true;
						}
			}

			oGridCell = Vector3I.Zero;
			return false;
		}

		/// <summary>
		/// Sphere test for simple entities, like floating objects, and for avoiding dangerous ships tools.
		/// </summary>
		/// <param name="entity">The entity to test.</param>
		/// <param name="offset">Difference between current postiong and where the ship will be</param>
		/// <param name="rejectionVector">Direction of movement vector</param>
		/// <param name="rejectionDistance">Distance of movement</param>
		private bool SphereTest(MyEntity entity, ref Vector3D offset, ref Vector3 rejectionVector, float rejectionDistance, out MyCubeBlock obstructBlock)
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
				obsRadius *= 16f;

			m_lineSegment.From = currentPosition;
			m_lineSegment.To = finalPosition;
			double distSq = m_lineSegment.DistanceSquared(ref offObsPos);

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
			return dot >= 0d || dot / toObstruction.Length() >= -0.5d;
		}

		/// <summary>
		/// Tests if autopilot's ship would endanger a grid with a ship tool.
		/// </summary>
		private bool EndangerGrid(MyCubeGrid grid, ref Vector3D offset, ref Vector3 rejectionVector, float rejectionDistance)
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
					return true;
				}
				foreach (MyShipDrill drill in cache.BlocksOfType(typeof(MyObjectBuilder_Drill)))
					if (drill.IsShooting && ToolObstructed(drill, ref gridPosition, gridRadius, ref rejectDisp) && IsRejectionTowards(ref obstructPositon, ref currentPosition, ref rejectD))
						return true;
				foreach (MyShipGrinder grinder in cache.BlocksOfType(typeof(MyObjectBuilder_ShipGrinder)))
					if (grinder.IsShooting && ToolObstructed(grinder, ref gridPosition, gridRadius, ref rejectDisp) && IsRejectionTowards(ref obstructPositon, ref currentPosition, ref rejectD))
						return true;
			}

			return false;
		}

		private bool ToolObstructed(MyCubeBlock drill, ref Vector3D gridPosition, double gridRadius, ref Vector3D rejectDisp)
		{
			m_lineSegment.From = drill.PositionComp.GetPosition();
			m_lineSegment.To = m_lineSegment.From + rejectDisp;
			return m_lineSegment.DistanceLessEqual(ref gridPosition, gridRadius + drill.PositionComp.LocalVolume.Radius * 4f);
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
			if (!CapsuleDExtensions.IntersectsVoxel(ref capsule, out hitVoxel, out hitPosition, true))
			{
				Profiler.EndProfileBlock();
				return false;
			}
			capsule.Radius = AutopilotGrid.GridSize * 2f;

			IEnumerable<CubeGridCache> myCaches = AttachedGrid.AttachedGrids(AutopilotGrid, AttachedGrid.AttachmentKind.Physics, true).Select(CubeGridCache.GetFor);

			Vector3 v; rayDirection.CalculatePerpendicularVector(out v);
			Vector3 w; Vector3.Cross(ref rayDirection, ref v, out w);
			Matrix to3D = new Matrix(v.X, v.Y, v.Z, 0f,
				w.X, w.Y, w.Z, 0f,
				rayDirection.X, rayDirection.Y, rayDirection.Z, 0f,
				0f, 0f, 0f, 1f);
			Matrix to2D; Matrix.Invert(ref to3D, out to2D);

			m_rejections.Clear();
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
					if (!m_rejections.Add(ToCell(pc2, gridSize), true))
						continue;

					Vector3D end = new Vector3D() { X = offsetWorld.X + rayDirectionLength.X, Y = offsetWorld.Y + rayDirectionLength.Y, Z = offsetWorld.Z + rayDirectionLength.Z };
					capsule.P0 = offsetWorld;
					capsule.P1 = end;
					if (CapsuleDExtensions.IntersectsVoxel(ref capsule, out hitVoxel, out hitPosition, true))
					{
						Profiler.EndProfileBlock();
						return true;
					}
				}
			}

			hitVoxel = null;
			hitPosition = Vector3.Invalid;
			Profiler.EndProfileBlock();
			return false;
		}

	}
}
