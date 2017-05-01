#if DEBUG
//#define TRACE
#endif

using System;
using System.Collections.Generic;
using System.Linq;
using Rynchodon.Attached;
using Rynchodon.Utility;
using Rynchodon.Utility.Collections;
using Sandbox.Common.ObjectBuilders;
using Sandbox.Game.Entities;
using Sandbox.Game.Weapons;
using VRage.Game.Components;
using VRage.Game.Entity;
using VRage.Game.ModAPI;
using VRageMath;

namespace Rynchodon.Autopilot.Pathfinding
{
	/// <summary>
	/// Performs the comutations necessary to determine whether or not a path is clear.
	/// </summary>
	public class PathTester
	{

		/// <summary>
		/// Standard inputs for PathTester's public functions.
		/// </summary>
		public struct TestInput : IEquatable<TestInput>
		{
			/// <summary>Difference between the current position of the ship and the start of the path segment.</summary>
			public Vector3D Offset;
			/// <summary>The direction of travel</summary>
			public Vector3 Direction;
			/// <summary>Really distance, but Length is more visually distinct from Direction.</summary>
			public float Length;

			public bool Equals(TestInput other)
			{
				return Offset == other.Offset && Direction == other.Direction && Length == other.Length;
			}

			public override string ToString()
			{
				return "{Offset: " + Offset + " Direction: " + Direction + " Length: " + Length + "}";
			}
		}

		/// <summary>
		/// Results for entities that are not voxels.
		/// </summary>
		public struct GridTestResult
		{
			public static readonly GridTestResult Default = new GridTestResult() { m_proximity = float.MaxValue };

			private float m_proximity;

			/// <summary>Approximation of the distance in the target direction that can be travelled.</summary>
			public float Distance;
			/// <summary>Approximation of how close the ship would get to an obstruction. In some cases, it will be less than zero. When setting, value is only changed if it is greater than the supplied value</summary>
			public float Proximity
			{
				get { return m_proximity; }
				set
				{
					if (value < m_proximity)
						m_proximity = value;
				}
			}
			/// <summary>Either null or the block that is obstructing the path.</summary>
			public MyCubeBlock ObstructingBlock;
		}

		/// <summary>
		/// Results for voxel tests.
		/// </summary>
		public struct VoxelTestResult
		{
			public static readonly VoxelTestResult Default = new VoxelTestResult() { m_proximity = float.MaxValue };

			private float m_proximity;

			/// <summary>Approximation of the distance in the target direction that can be travelled.</summary>
			public float Distance;
			/// <summary>Approximation of how close the ship would get to an obstruction. In some cases, it will be less than zero. When setting, value is only changed if it is greater than the supplied value</summary>
			public float Proximity
			{
				get { return m_proximity; }
				set
				{
					if (value < m_proximity)
						m_proximity = value;
				}
			}
			/// <summary>The voxel that is obstructing the path.</summary>
			public MyVoxelBase ObstructingVoxel;
		}

		static PathTester()
		{
			Vector2IMatrix<Vector3D>.EqualityComparer = EqualityComparer_Vector3D.Instance;
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
		/// Adjusts input for the autopilot's velocity and entity's velocity.
		/// </summary>
		/// <param name="input">Original TestInput with Direction as the desired direction of travel and Length as the distance to the destination.</param>
		/// <param name="adjusted">Offset will be zero, Direction and Length will be modified from input for the velocity of autopilot and entity.</param>
		/// <param name="entity">The potential obstruction, if null, assumes a static entity</param>
		public void AdjustForCurrentVelocity(ref TestInput input, out TestInput adjusted, MyEntity entity, bool destination)
		{
			Vector3 autopilotVelocity = AutopilotGrid.Physics.LinearVelocity;
			Vector3 relativeVelocity;

			if (entity == null)
			{
				relativeVelocity = autopilotVelocity;
			}
			else
			{
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
			}

			Vector3.Add(ref relativeVelocity, ref input.Direction, out adjusted.Direction);
			adjusted.Offset = Vector3D.Zero;
			if (adjusted.Direction == Vector3.Zero)
				adjusted.Length = 0f;
			else
			{
				adjusted.Length = 20f + adjusted.Direction.Normalize() * Pathfinder.SpeedFactor;
				if (destination && input.Length < adjusted.Length)
					adjusted.Length = input.Length;
			}
		}

		/// <summary>
		/// Tests if a section of space can be travelled without hitting the specified entity.
		/// </summary>
		/// <param name="entity">The potential obstruction</param>
		/// <param name="ignoreBlock">Null or the block autopilot is trying to connect with.</param>
		/// <param name="input"><see cref="TestInput"/></param>
		/// <param name="result"><see cref="GridTestResult"/></param>
		/// <returns>True if the specified entity obstructs the path.</returns>
		public bool ObstructedBy(MyEntity entity, MyCubeBlock ignoreBlock, ref TestInput input, out GridTestResult result)
		{
			//Logger.DebugLog("checking: " + entity.getBestName() + ", offset: " + offset + ", rejection vector: " + rejectionVector + ", rejection distance: " + rejectionDistance);
#if DEBUG
			if (!input.Direction.IsValid() || Math.Abs(1f - input.Direction.LengthSquared()) > 0.01f)
				throw new Exception("rejection vector is invalid. entity: " + entity.nameWithId() + ", input: " + input);
#endif

			result = GridTestResult.Default;
			result.Distance = input.Length;
			MyCubeGrid grid = entity as MyCubeGrid;
			if (grid != null)
			{
				// check for dangerous tools on grid
				CubeGridCache cache = CubeGridCache.GetFor(grid);
				if (cache == null)
					return false;
				Profiler.StartProfileBlock("Checking Tools");
				foreach (MyShipDrill drill in cache.BlocksOfType(typeof(MyObjectBuilder_Drill)))
					if (drill.IsShooting)
						if (SphereTest(drill, ref input, ref result))
						{
							Profiler.EndProfileBlock();
							return true;
						}
				foreach (MyShipGrinder grinder in cache.BlocksOfType(typeof(MyObjectBuilder_ShipGrinder)))
					if (grinder.IsShooting)
						if (SphereTest(grinder, ref input, ref result))
						{
							Profiler.EndProfileBlock();
							return true;
						}
				Profiler.EndProfileBlock();

				if (ExtensionsRelations.canConsiderFriendly(Controller.CubeBlock, grid) && EndangerGrid(grid, ref input, ref result))
				{
					Logger.DebugLog("Movement would endanger: " + grid.getBestName());
					return true;
				}

				Profiler.StartProfileBlock("RejectionIntersects");
				if (RejectionIntersects(grid, ignoreBlock, ref input, ref result))
				{
					Profiler.EndProfileBlock();
					return true;
				}
				Profiler.EndProfileBlock();
			}
			else
				return SphereTest(entity, ref input, ref result);

			return false;
		}

		/// <summary>
		/// Tests a grid for obstructing the ship via vector rejection.
		/// </summary>
		/// <param name="oGrid">The grid that may obstruct this one.</param>
		/// <param name="ignoreBlock">Block to ignore, or null</param>
		/// <param name="input"><see cref="TestInput"/></param>
		/// <param name="result"><see cref="GridTestResult"/></param>
		/// <returns>True if oGrid is blocking the ship.</returns>
		private bool RejectionIntersects(MyCubeGrid oGrid, MyCubeBlock ignoreBlock, ref TestInput input, ref GridTestResult result)
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
				return false;
			}

			bool checkBlock = ignoreBlock != null && oGrid == ignoreBlock.CubeGrid;

			Vector3 v; input.Direction.CalculatePerpendicularVector(out v);
			Vector3 w; Vector3.Cross(ref input.Direction, ref v, out w);
			Matrix to3D = new Matrix(v.X, v.Y, v.Z, 0f,
				w.X, w.Y, w.Z, 0f,
				input.Direction.X, input.Direction.Y, input.Direction.Z, 0f,
				0f, 0f, 0f, 1f);
			Matrix to2D; Matrix.Invert(ref to3D, out to2D);

			float roundTo;
			int minDistanceSquared;
			if (AutopilotGrid.GridSizeEnum == oGrid.GridSizeEnum)
			{
				roundTo = AutopilotGrid.GridSize;
				minDistanceSquared = 1;
			}
			else
			{
				roundTo = Math.Min(AutopilotGrid.GridSize, oGrid.GridSize);
				minDistanceSquared = (int)Math.Ceiling(Math.Max(AutopilotGrid.GridSize, oGrid.GridSize) / roundTo);
				minDistanceSquared *= minDistanceSquared;
			}
			int maxDistanceSquared = minDistanceSquared * 100;

			Profiler.StartProfileBlock("RejectionIntersects:Build ship");

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
					Profiler.EndProfileBlock();
					return false;
				}
				foreach (Vector3I cell in cache.OccupiedCells())
				{
					Vector3 local = cell * gridSize;

					MyCubeBlock block = (MyCubeBlock)cache.CubeGrid.GetCubeBlock(cell)?.FatBlock;
					if (block != null && block.Subparts != null && block.Subparts.Count != 0 && !CellOccupiedByBlock(cell, block))
						continue;

					Vector3D world; Vector3D.Transform(ref local, ref worldMatrix, out world);
					Vector3D relative; Vector3D.Subtract(ref world, ref currentPosition, out relative);
					Vector3 relativeF = relative;

					float projectionDistance; Vector3 rejection;
					VectorExtensions.RejectNormalized(ref relativeF, ref input.Direction, out projectionDistance, out rejection);
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
			Profiler.EndProfileBlock();

			minProjection += StartRayCast; // allow autopilot to move away from a touching object

			//Logger.DebugLog("projection min: " + minProjection + ", max: " + maxProjection + ", max for other: " + (maxProjection + rejectionDistance));
			maxProjection += input.Length;

			//Logger.DebugLog("checking other grid cells");

			Profiler.StartProfileBlock("RejectionIntersects:other grid");

			Vector2IMatrix<bool> otherGridRejections;
			ResourcePool.Get(out otherGridRejections);

			worldMatrix = oGrid.WorldMatrix;
			gridSize = oGrid.GridSize;
			foreach (Vector3I cell in oCache.OccupiedCells())
			{
				//Logger.DebugLog("cell: " + cell);

				Vector3 local = cell * gridSize;
				Vector3D world; Vector3D.Transform(ref local, ref worldMatrix, out world);
				Vector3D offsetWorld; Vector3D.Subtract(ref world, ref input.Offset, out offsetWorld);
				Vector3D relative; Vector3D.Subtract(ref offsetWorld, ref currentPosition, out relative);
				Vector3 relativeF = relative;

				Vector3 rejection;
				VectorExtensions.RejectNormalized(ref relativeF, ref input.Direction, out result.Distance, out rejection);
				if (result.Distance < minProjection || result.Distance > maxProjection)
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

				ExpandingRings.Ring ring = default(ExpandingRings.Ring);
				for (int ringIndex = 0; ring.DistanceSquared <= maxDistanceSquared; ringIndex++)
				{
					ring = ExpandingRings.GetRing(ringIndex);
					for (int squareIndex = 0; squareIndex < ring.Squares.Length; squareIndex ++)
						if (apShipRejections.Contains(cell2D + ring.Squares[squareIndex]))
							if (ring.DistanceSquared <= minDistanceSquared)
							{
								IMySlimBlock slim = oGrid.GetCubeBlock(cell);
								if (slim != null)
								{
									if (checkBlock && slim.FatBlock == ignoreBlock)
										continue;

									MyCubeBlock fat = (MyCubeBlock)slim.FatBlock;
									if (fat != null && fat.Subparts != null && fat.Subparts.Count != 0 && !CellOccupiedByBlock(cell, fat))
										continue;

									result.ObstructingBlock = fat;
								}
								else
									result.ObstructingBlock = null;

								result.Proximity = 0f;
								Logger.DebugLog("Hit, projectionDistance: " + result.Distance + ", min: " + minProjection + ", max: " + maxProjection + ", ring: " + ringIndex + ", ring dist sq: " + ring.DistanceSquared + ", min dist sq: " + minDistanceSquared + ", max dist sq: " + maxDistanceSquared);
								Profiler.EndProfileBlock();
								apShipRejections.Clear();
								otherGridRejections.Clear();
								ResourcePool.Return(apShipRejections);
								ResourcePool.Return(otherGridRejections);
								return true;
							}
							else
							{
								maxDistanceSquared = ring.DistanceSquared;
								goto NextCell;
							}
				}

				NextCell:;
			}
			Profiler.EndProfileBlock();

			apShipRejections.Clear();
			otherGridRejections.Clear();
			ResourcePool.Return(apShipRejections);
			ResourcePool.Return(otherGridRejections);
			result.Proximity = (float)Math.Sqrt(maxDistanceSquared);
			return false;
		}

		/// <summary>
		/// Determine if a block's AABB, or any of its subparts' AABB, contain the given cell.
		/// </summary>
		/// <param name="cell">Cell in grid space.</param>
		/// <param name="block">The block to test AABB of.</param>
		/// <returns>True iff a block's AABB, or any of its subparts' AABB, contain the given cell.</returns>
		/// <remarks>
		/// If subparts are rotated this test will be very coarse.
		/// This test allows autopilot to pass through open doors, above retracted pistons, etc.
		/// </remarks>
		private static bool CellOccupiedByBlock(Vector3I cell, MyCubeBlock block)
		{
			Vector3 position = cell * block.CubeGrid.GridSize;
			BoundingBox gsAABB;
			block.CombinedAABB(out gsAABB);

			return gsAABB.Contains(position) != ContainmentType.Disjoint;
		}

		/// <summary>
		/// Sphere test for simple entities, like floating objects, and for avoiding dangerous ships tools.
		/// </summary>
		/// <param name="entity">The entity to test.</param>
		/// <param name="input"><see cref="TestInput"/></param>
		/// <param name="result"><see cref="GridTestResult"/></param>
		/// <returns>True if the entity is obstructing the ship.</returns>
		private bool SphereTest(MyEntity entity, ref TestInput input, ref GridTestResult result)
		{
			Vector3D currentPosition = AutopilotGrid.GetCentre();
			Vector3D rejectD = input.Direction;
			Vector3D disp; Vector3D.Multiply(ref rejectD, input.Length, out disp);
			Vector3D finalPosition; Vector3D.Add(ref currentPosition, ref disp, out finalPosition);
			float shipRadius = AutopilotGrid.PositionComp.LocalVolume.Radius;

			Vector3D obsPos = entity.GetCentre();
			Vector3D offObsPos; Vector3D.Subtract(ref obsPos, ref input.Offset, out offObsPos);
			float obsRadius = entity.PositionComp.LocalVolume.Radius;

			if (entity is MyCubeBlock)
				// it is an active tool so increase the radius
				obsRadius += 10f;

			m_lineSegment.From = currentPosition;
			m_lineSegment.To = finalPosition;
			Vector3D closest;
			result.Distance = (float)m_lineSegment.ClosestPoint(ref offObsPos, out closest);
			double distance; Vector3D.Distance(ref offObsPos, ref closest, out distance);
			result.Proximity = (float)distance - (shipRadius + obsRadius);

			if (result.Proximity <= 0f && IsRejectionTowards(ref obsPos, ref currentPosition, ref rejectD))
			{
				Logger.DebugLog("Rejection " + input.Direction + " hit " + entity.nameWithId());
				result.ObstructingBlock = entity as MyCubeBlock; // it may not be a block
				return true;
			}
			return false;
		}

		/// <summary>
		/// Tests if the vector from currentPosition to obstructPosition is in the same direction as rejectionDirection.
		/// </summary>
		/// <param name="obstructPosition">Position of the obstruction</param>
		/// <param name="currentPosition">Current position of the ship</param>
		/// <param name="rejectionDirection">Direction of travel.</param>
		/// <returns>True if the vector from currentPosition to obstructPosition is in the same direction as rejectionDirection.</returns>
		private bool IsRejectionTowards(ref Vector3D obstructPosition, ref Vector3D currentPosition, ref Vector3D rejectionDirection)
		{
			Vector3D toObstruction; Vector3D.Subtract(ref obstructPosition, ref currentPosition, out toObstruction);
			double dot = toObstruction.Dot(ref rejectionDirection);
			return dot >= 0d;
		}

		/// <summary>
		/// Tests if autopilot's ship would endanger a grid with a ship tool.
		/// </summary>
		/// <param name="grid">The grid that is potentially endangered.</param>
		/// <param name="input"><see cref="TestInput"/></param>
		/// <param name="result"><see cref="GridTestResult"/></param>
		/// True if the ship would endanger grid.
		private bool EndangerGrid(MyCubeGrid grid, ref TestInput input, ref GridTestResult result)
		{
			Vector3D currentPosition = AutopilotGrid.GetCentre();
			Vector3D obstructPositon = grid.GetCentre();
			Vector3D rejectD = input.Direction;

			Vector3D gridPosition = grid.GetCentre() - input.Offset;
			float gridRadius = grid.PositionComp.LocalVolume.Radius;

			Vector3 rejectDispF; Vector3.Multiply(ref input.Direction, input.Length, out rejectDispF);
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
					if (drill.IsShooting && ToolObstructed(drill, ref gridPosition, gridRadius, ref rejectDisp, ref result) && IsRejectionTowards(ref obstructPositon, ref currentPosition, ref rejectD))
						return true;
				foreach (MyShipGrinder grinder in cache.BlocksOfType(typeof(MyObjectBuilder_ShipGrinder)))
					if (grinder.IsShooting && ToolObstructed(grinder, ref gridPosition, gridRadius, ref rejectDisp, ref result) && IsRejectionTowards(ref obstructPositon, ref currentPosition, ref rejectD))
						return true;
			}

			return false;
		}

		/// <summary>
		/// Tests if a specified tool will endager a grid at gridPosition.
		/// </summary>
		/// <param name="tool">The hazardous tool.</param>
		/// <param name="gridPosition">The positon of the grid.</param>
		/// <param name="gridRadius">The radius around gridPosition which the tool must not intersect.</param>
		/// <param name="rejectDisp">The displacement of travel.</param>
		/// <param name="result"><see cref="GridTestResult"/></param>
		/// <returns>True if tool would endanger a grid at gridPosition.</returns>
		private bool ToolObstructed(MyCubeBlock tool, ref Vector3D gridPosition, float gridRadius, ref Vector3D rejectDisp, ref GridTestResult result)
		{
			m_lineSegment.From = tool.PositionComp.GetPosition();
			m_lineSegment.To = m_lineSegment.From + rejectDisp;
			Vector3D closest;
			result.Distance = (float)m_lineSegment.ClosestPoint(ref gridPosition, out closest);
			double distance; Vector3D.Distance(ref gridPosition, ref closest, out distance);
			result.Proximity = (float)distance - (gridRadius + tool.PositionComp.LocalVolume.Radius + 5f);
			return result.Proximity <= 0f;
		}

		/// <summary>
		/// Tests if the ship is obstructed by any voxel.
		/// </summary>
		/// <param name="input"><see cref="TestInput"/></param>
		/// <param name="result"><see cref="VoxelTestResult"/></param>
		/// <returns>True iff a voxel is obstructing the ship.</returns>
		public bool RayCastIntersectsVoxel(ref TestInput input, out VoxelTestResult result)
		{
			Profiler.StartProfileBlock();

			Logger.DebugLog("direction vector is invalid: " + input.Direction, Logger.severity.FATAL, condition: !input.Direction.IsValid() || Math.Abs(1f - input.Direction.LengthSquared()) > 0.01f);
			Logger.TraceLog(input.ToString());

			if (input.Length < 1f)
			{
				// need to skip as Proximity doesn't work with short capsules
				// should be safe, as the ship got here somehow
				Logger.TraceLog("Input length is small, no voxel test necessary");
				result = VoxelTestResult.Default;
				result.Distance = input.Length;
				result.Proximity = 1f;
				return false;
			}

			Vector3D currentPosition = AutopilotGrid.GetCentre();
			Vector3 startOffset; Vector3.Multiply(ref input.Direction, StartRayCast, out startOffset);
			Vector3D startOffsetD = startOffset;
			Vector3D totalOffset; Vector3D.Add(ref input.Offset, ref startOffsetD, out totalOffset);

			CapsuleD capsule;
			Vector3D.Add(ref currentPosition, ref totalOffset, out capsule.P0);
			Vector3D capsuleDisp;
			{
				capsuleDisp.X = input.Direction.X * input.Length;
				capsuleDisp.Y = input.Direction.Y * input.Length;
				capsuleDisp.Z = input.Direction.Z * input.Length;
			}
			Vector3D.Add(ref capsule.P0, ref capsuleDisp, out capsule.P1);
			capsule.Radius = AutopilotGrid.PositionComp.LocalVolume.Radius;
			//Logger.DebugLog("current position: " + currentPosition + ", offset: " + offset + ", line: " + rayDirectionLength + ", start: " + capsule.P0 + ", end: " + capsule.P1);

			result = VoxelTestResult.Default;
			Vector3D hitPosition;
			float proximity = (float)CapsuleDExtensions.ProximityToVoxel(ref capsule, out result.ObstructingVoxel, out hitPosition, true, input.Length);
			result.Proximity = 10f * proximity; // lie because we have not done a proper test but could have a very nice result
			if (proximity > 1f)
			{
				Logger.TraceLog("Large capsule DOES NOT intersect voxel: " + capsule.String() + ", proximity: " + proximity + "/" + result.Proximity);
				result.Distance = input.Length;
				Profiler.EndProfileBlock();
				return false;
			}
			Logger.TraceLog("Large capsule DOES intersect voxel: " + capsule.String() + ", proximity: " + proximity + "/" + result.Proximity);

			IEnumerable<CubeGridCache> myCaches = AttachedGrid.AttachedGrids(AutopilotGrid, AttachedGrid.AttachmentKind.Physics, true).Select(CubeGridCache.GetFor);

			Vector3 v; input.Direction.CalculatePerpendicularVector(out v);
			Vector3 w; Vector3.Cross(ref input.Direction, ref v, out w);
			Matrix to3D = new Matrix(v.X, v.Y, v.Z, 0f,
				w.X, w.Y, w.Z, 0f,
				input.Direction.X, input.Direction.Y, input.Direction.Z, 0f,
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
					Vector3 rejection; Vector3.Reject(ref relativeF, ref input.Direction, out rejection);
					Vector3 planarComponents; Vector3.Transform(ref rejection, ref to2D, out planarComponents);
					Logger.DebugLog("Math fail: rejection: " + rejection + ", planar components: " + planarComponents + "\nto3D: " + to3D, Logger.severity.FATAL, condition: planarComponents.Z > 0.001f || planarComponents.Z < -0.001f);
					Vector2 pc2 = new Vector2(planarComponents.X, planarComponents.Y);
					apShipRejections.Add(ToCell(pc2, gridSize), offsetWorld);
				}
			}

			Vector2IMatrix<bool> testedRejections;
			ResourcePool.Get(out testedRejections);

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
				Vector3D.Add(ref capsule.P0, ref capsuleDisp, out capsule.P1);
				capsule.Radius = (1f + (float)Math.Sqrt(biggestRingSq)) * gridSize;
				result.Proximity = (float)CapsuleDExtensions.ProximityToVoxel(ref capsule, out result.ObstructingVoxel, out hitPosition, true, input.Length);
				if (result.Proximity <= 1f)
				{
					Logger.TraceLog("Block capsule does hit voxel: " + capsule.String() + ", proxmity: " + result.Proximity);
					double distance;  Vector3D.Distance(ref capsule.P0, ref hitPosition, out distance);
					result.Distance = (float)distance;
					apShipRejections.Clear();
					testedRejections.Clear();
					ResourcePool.Return(apShipRejections);
					ResourcePool.Return(testedRejections);
					Profiler.EndProfileBlock();
					return true;
				}
			}

			Logger.TraceLog("Ship's path is clear from voxels, proximity: " + result.Proximity);
			apShipRejections.Clear();
			testedRejections.Clear();
			ResourcePool.Return(apShipRejections);
			ResourcePool.Return(testedRejections);
			Profiler.EndProfileBlock();
			return false;
		}

	}
}
