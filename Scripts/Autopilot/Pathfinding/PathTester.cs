using System;
using System.Collections.Generic;
using System.Linq;
using Rynchodon.Attached;
using Rynchodon.Utility;
using Rynchodon.Utility.Collections;
using Sandbox.Game.Entities;
using Sandbox.Game.Entities.Cube;
using Sandbox.ModAPI;
using VRage.Game.Entity;
using VRage.Game.ModAPI;
using VRageMath;

namespace Rynchodon.Autopilot.Pathfinding
{
	public class PathTester
	{

		/// <summary>Rejections of this grid</summary>
		private Vector2IMatrix<bool> m_rejections = new Vector2IMatrix<bool>();
		/// <summary>Rejections of the other grid.</summary>
		private Vector2IMatrix<bool> m_rejectTests = new Vector2IMatrix<bool>();

		public MyCubeGrid AutopilotGrid;

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
		public bool ObstructedBy(MyEntity entityTopMost, MyCubeBlock ignoreBlock, ref Vector3 targetDirection, float targetDistance, out MyCubeBlock obstructBlock, out Vector3D hitPosition, bool extraRadius = false)
		{
			Logger.DebugLog("checking: " + entityTopMost.getBestName() + ", targetDirection: " + targetDirection);

			Vector3D currentPosition = AutopilotGrid.GetCentre();

			Vector3 autopilotVelocity = AutopilotGrid.Physics.LinearVelocity;
			Vector3 relativeVelocity;
			if (entityTopMost.Physics == null || entityTopMost.Physics.IsStatic)
			{
				relativeVelocity = autopilotVelocity;
			}
			else
			{
				Vector3 entityVelocity = entityTopMost.Physics.LinearVelocity;
				Vector3.Subtract(ref autopilotVelocity, ref entityVelocity, out relativeVelocity);
			}

			Vector3 rejectionVector; Vector3.Add(ref relativeVelocity, ref targetDirection, out rejectionVector);
			rejectionVector.Normalize();

			return ObstructedBy(entityTopMost, ignoreBlock, ref Vector3D.Zero, ref rejectionVector, targetDistance, out obstructBlock, out hitPosition, extraRadius);
		}

		/// <param name="offset">Added to current position of ship's blocks.</param>
		/// <param name="rejectionVector">Direction of travel of autopilot ship, should be normalized</param>
		public bool ObstructedBy(MyEntity entityTopMost, MyCubeBlock ignoreBlock, ref Vector3D offset, ref Vector3 rejectionVector, float rejectionDistance, out MyCubeBlock obstructBlock, out Vector3D hitPosition, bool extraRadius = true)
		{
			Logger.DebugLog("checking: " + entityTopMost.getBestName() + ", offset: " + offset + ", rejection vector: " + rejectionVector);
			Logger.DebugLog("rejection vector is invalid", Logger.severity.FATAL, condition: !rejectionVector.IsValid());

			MyCubeGrid grid = entityTopMost as MyCubeGrid;
			if (grid != null)
			{
				Profiler.StartProfileBlock("RejectionIntersects");

				Vector3I hitCell;
				if (RejectionIntersects(grid, ignoreBlock, ref rejectionVector, rejectionDistance, ref offset, out hitCell, extraRadius))
				{
					MySlimBlock slim = grid.GetCubeBlock(hitCell);
					obstructBlock = slim == null ? null : slim.FatBlock;
					hitPosition = grid.GridIntegerToWorld(hitCell);
					Profiler.EndProfileBlock();
					Logger.DebugLog("rejection intersects, block: " + (slim != null ? slim.getBestName() : "N/A") + " at " + grid.GridIntegerToWorld(hitCell));
					return true;
				}
				else
					Logger.DebugLog("rejection does not intersect");
				Profiler.EndProfileBlock();
			}

			obstructBlock = null;
			hitPosition = entityTopMost.GetCentre();
			return false;
		}

		/// <param name="oGrid">The grid that may obstruct this one.</param>
		/// <param name="ignoreBlock">Block to ignore, or null</param>
		/// <param name="rejectionVector">Direction of travel of autopilot ship, should be normalized</param>
		/// <param name="offset">Difference between the current position of the autopilot ship and where it will be.</param>
		private bool RejectionIntersects(MyCubeGrid oGrid, MyCubeBlock ignoreBlock, ref Vector3 rejectionVector, float rejectionDistance, ref Vector3D offset, out Vector3I oGridCell, bool extraRadius)
		{
			Logger.DebugLog("Rejection vector is not normalized, length squared: " + rejectionVector.LengthSquared(), Logger.severity.FATAL, condition: Math.Abs(rejectionVector.LengthSquared() - 1f) > 0.001f);
			Logger.DebugLog("Testing for rejection intersection: " + oGrid.nameWithId() + ", starting from: " + (AutopilotGrid.GetCentre() + offset) + ", rejection vector: " + rejectionVector + ", distance: " + rejectionDistance +
				", final: " + (AutopilotGrid.GetCentre() + offset + rejectionVector * rejectionDistance));
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
					Vector3D offsetWorld; Vector3D.Add(ref world, ref offset, out offsetWorld);
					Vector3D relative; Vector3D.Subtract(ref offsetWorld, ref currentPosition, out relative);
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
				Vector3D relative; Vector3D.Subtract(ref world, ref currentPosition, out relative);
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

		/// <param name="rayDirectionLength">Not normalized, should reflect the distance autopilot needs to travel.</param>
		public bool RayCastIntersectsVoxel(ref Vector3D offset, ref Vector3 rayDirectionLength, out IHitInfo hit)
		{
			Profiler.StartProfileBlock();
			IEnumerable<CubeGridCache> myCaches = AttachedGrid.AttachedGrids(AutopilotGrid, AttachedGrid.AttachmentKind.Physics, true).Select(CubeGridCache.GetFor);
			Vector3D currentPosition = AutopilotGrid.GetCentre();

			Vector3 rayDirection; Vector3.Normalize(ref rayDirectionLength, out rayDirection);
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
					hit = default(IHitInfo);
					Profiler.EndProfileBlock();
					return false;
				}
				foreach (Vector3I cell in cache.OccupiedCells())
				{
					Vector3 local = cell * gridSize;
					Vector3D world; Vector3D.Transform(ref local, ref worldMatrix, out world);
					Vector3D offsetWorld; Vector3D.Add(ref world, ref offset, out offsetWorld);
					Vector3D relative; Vector3D.Subtract(ref offsetWorld, ref currentPosition, out relative);
					Vector3 relativeF = relative;
					Vector3 rejection; Vector3.Reject(ref relativeF, ref rayDirection, out rejection);
					Vector3 planarComponents; Vector3.Transform(ref rejection, ref to2D, out planarComponents);
					Logger.DebugLog("Math fail: rejection: " + rejection + ", planar components: " + planarComponents + "\nto3D: " + to3D, Logger.severity.FATAL, condition: planarComponents.Z > 0.001f || planarComponents.Z < -0.001f);
					Vector2 pc2 = new Vector2(planarComponents.X, planarComponents.Y);
					if (!m_rejections.Add(ToCell(pc2, gridSize), true))
						continue;

					Vector3D end = new Vector3D() { X = offsetWorld.X + rayDirectionLength.X,  Y = offsetWorld.Y + rayDirectionLength.Y, Z = offsetWorld.Z + rayDirectionLength.Z};
					if (RayCast.RayCastVoxels(ref offsetWorld, ref end, out hit))
					{
						Profiler.EndProfileBlock(); 
						return true;
					}
				}
			}

			hit = default(IHitInfo);
			Profiler.EndProfileBlock(); 
			return false;
		}

	}
}
