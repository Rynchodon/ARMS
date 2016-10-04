using System;
using System.Collections.Generic;
using System.Linq;
using Rynchodon.Attached;
using Rynchodon.Utility;
using Rynchodon.Utility.Collections;
using Sandbox.Game.Entities;
using Sandbox.ModAPI;
using VRage.Game.Entity;
using VRage.Game.ModAPI;
using VRageMath;

namespace Rynchodon.Autopilot.Pathfinder
{
	public class PathTester
	{

		/// <summary>Rejections of this grid</summary>
		private Vector2IMatrix<bool> m_rejections = new Vector2IMatrix<bool>();
		/// <summary>Rejections of the other grid.</summary>
		private Vector2IMatrix<bool> m_rejectTests = new Vector2IMatrix<bool>();

		public MyCubeGrid AutopilotGrid;

		private Vector2I Round(Vector2 vector, float value)
		{
			return new Vector2I((int)Math.Round(vector.X / value), (int)Math.Round(vector.Y / value));
		}

		/// <summary>
		/// For testing the current path of autopilot, accounts for current velocity.
		/// </summary>
		/// <param name="targetDirection">The direction the autopilot wants to travel in.</param>
		public bool ObstructedBy(MyEntity entityTopMost, MyCubeBlock ignoreBlock, ref Vector3 targetDirection, out object partHit, out Vector3D hitPosition)
		{
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

			Vector3 rejectionVector;
			Vector3.Add(ref relativeVelocity, ref targetDirection, out rejectionVector);
			rejectionVector.Normalize();

			return ObstructedBy(entityTopMost, ignoreBlock, ref currentPosition, ref rejectionVector, out partHit, out hitPosition);
		}

		/// <param name="offset">Added to current position of ship's blocks.</param>
		/// <param name="rejectionVector">Direction of travel of autopilot ship, should be normalized</param>
		public bool ObstructedBy(MyEntity entityTopMost, MyCubeBlock ignoreBlock, ref Vector3D offset, ref Vector3 rejectionVector, out object partHit, out Vector3D hitPosition)
		{
			MyCubeGrid grid = entityTopMost as MyCubeGrid;
			if (grid != null)
			{
				Profiler.StartProfileBlock("RejectionIntersects");

				Vector3I hitCell;
				if (RejectionIntersects(grid, ignoreBlock, ref rejectionVector, ref offset, out hitCell))
				{
					Logger.DebugLog("rejection intersects");
					partHit = grid.GetCubeBlock(hitCell);
					hitPosition = grid.GridIntegerToWorld(hitCell);
					Profiler.EndProfileBlock();
					return true;
				}
				else
					Logger.DebugLog("rejection does not intersect");
				Profiler.EndProfileBlock();
			}

			partHit = null;
			hitPosition = entityTopMost.GetCentre();
			return false;
		}

		/// <param name="oGrid">The grid that may obstruct this one.</param>
		/// <param name="ignoreBlock">Block to ignore, or null</param>
		/// <param name="rejectionVector">Direction of travel of autopilot ship, should be normalized</param>
		/// <param name="offset">Difference between the current position of the autopilot ship and where it will be.</param>
		private bool RejectionIntersects(MyCubeGrid oGrid, MyCubeBlock ignoreBlock, ref Vector3 rejectionVector, ref Vector3D offset, out Vector3I oGridCell)
		{
			Logger.DebugLog("Rejection vector is not normalized, length squared: " + rejectionVector.LengthSquared(), Logger.severity.FATAL, condition: Math.Abs(rejectionVector.LengthSquared() - 1f) > 0.001f);
			Logger.DebugLog("Testing for rejection intersection: " + oGrid.nameWithId() + ", rejection vector: " + rejectionVector);

			// TODO: need a cyclinder test, currently method is only testing rejection

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
				steps = (int)Math.Ceiling(Math.Max(AutopilotGrid.GridSize, oGrid.GridSize) / roundTo) + 1;
			}

			//Logger.DebugLog("building m_rejections");

			m_rejections.Clear();
			MatrixD worldMatrix = AutopilotGrid.WorldMatrix;
			float gridSize = AutopilotGrid.GridSize;
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
					Vector3 rejection; Vector3.Reject(ref relativeF, ref rejectionVector, out rejection);
					Vector3 planarComponents; Vector3.Transform(ref rejection, ref to2D, out planarComponents);
					Logger.DebugLog("Math fail: rejection: " + rejection + ", planar components: " + planarComponents + "\nto3D: " + to3D, Logger.severity.FATAL, condition: planarComponents.Z > 0.0001f || planarComponents.Z < -0.0001f);
					Vector2 pc2 = new Vector2(planarComponents.X, planarComponents.Y);
					m_rejections[Round(pc2, roundTo)] = true;
				}
			}

			//Logger.DebugLog("checking other grid cells");

			m_rejectTests.Clear();
			worldMatrix = oGrid.WorldMatrix;
			gridSize = oGrid.GridSize;
			foreach (Vector3I cell in oCache.OccupiedCells())
			{
				Vector3 local = cell * gridSize;
				Vector3D world; Vector3D.Transform(ref local, ref worldMatrix, out world);
				Vector3D relative; Vector3D.Subtract(ref world, ref currentPosition, out relative);
				Vector3 relativeF = relative;
				Vector3 rejection; Vector3.Reject(ref relativeF, ref rejectionVector, out rejection);
				Vector3 planarComponents; Vector3.Transform(ref rejection, ref to2D, out planarComponents);
				Logger.DebugLog("Math fail: rejection: " + rejection + ", planar components: " + planarComponents + "\nto3D: " + to3D, Logger.severity.FATAL, condition: planarComponents.Z > 0.0001f || planarComponents.Z < -0.0001f);
				Vector2 pc2 = new Vector2(planarComponents.X, planarComponents.Y);
				Vector2I rounded = Round(pc2, roundTo);

				if (!m_rejectTests.Add(rounded, true))
					continue;

				//Logger.DebugLog("testing range. x: " + rounded.X + " - " + pc2.X);

				Vector2I test;
				for (test.X = rounded.X - steps; test.X <= pc2.X + steps; test.X++)
					for (test.Y = rounded.Y - steps; test.Y <= pc2.Y + steps; test.Y++)
						if (m_rejections.Contains(test))
						{
							if (checkBlock)
							{
								IMySlimBlock slim = oGrid.GetCubeBlock(cell);
								if (slim.FatBlock == ignoreBlock)
									continue;
							}
							oGridCell = cell;
							return true;
						}
			}
			oGridCell = Vector3I.Zero;
			return false;
		}

		/// <param name="rayDirectionLength">Not normalized, should reflect the distance autopilot needs to travel.</param>
		public bool RayCastIntersectsVoxel(ref Vector3D offset, ref Vector3 rayDirectionLength, out IHitInfo hit)
		{
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
					Logger.DebugLog("Math fail: rejection: " + rejection + ", planar components: " + planarComponents + "\nto3D: " + to3D, Logger.severity.FATAL, condition: planarComponents.Z > 0.0001f || planarComponents.Z < -0.0001f);
					Vector2 pc2 = new Vector2(planarComponents.X, planarComponents.Y);
					if (!m_rejections.Add(Round(pc2, gridSize), true))
						continue;

					if (MyAPIGateway.Physics.CastRay(offsetWorld, offsetWorld + rayDirectionLength, out hit, RayCast.FilterLayerVoxel))
						return true;
				}
			}

			hit = default(IHitInfo);
			return false;
		}

	}
}
