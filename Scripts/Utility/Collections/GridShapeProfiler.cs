using System;
using System.Collections.Generic;
using Sandbox.Game.Entities;
using Sandbox.ModAPI;
using VRage;
using VRage.Collections;
using VRage.Game.Entity;
using VRage.Game.ModAPI;
using VRage.ModAPI;
using VRageMath;

namespace Rynchodon
{

	/// <summary>
	/// Keeps a HashSet of all the occupied cells in a grid.
	/// </summary>
	public class GridCellCache
	{

		private static Dictionary<IMyCubeGrid, GridCellCache> CellCache = new Dictionary<IMyCubeGrid, GridCellCache>();
		private static FastResourceLock lock_cellCache = new FastResourceLock();

		static GridCellCache()
		{
			MyAPIGateway.Entities.OnCloseAll += Entities_OnCloseAll;
		}

		private static void Entities_OnCloseAll()
		{
			MyAPIGateway.Entities.OnCloseAll -= Entities_OnCloseAll;
			CellCache = null;
			lock_cellCache = null;
		}

		public static GridCellCache GetCellCache(IMyCubeGrid grid)
		{
			GridCellCache cache;
			using (lock_cellCache.AcquireExclusiveUsing())
				if (!CellCache.TryGetValue(grid, out cache))
				{
					cache = new GridCellCache(grid);
					CellCache.Add(grid, cache);
				}
			return cache;
		}

		private readonly Logger m_logger;
		private readonly IMyCubeGrid m_grid;

		private HashSet<Vector3I> CellPositions = new HashSet<Vector3I>();
		private List<MyCubeBlock> LargeDoors = new List<MyCubeBlock>();
		private FastResourceLock lock_cellPositions = new FastResourceLock();

		/// <summary>
		/// Not totally accurate as it does not check door extension.
		/// </summary>
		public int CellCount
		{
			get { return CellPositions.Count + LargeDoors.Count; }
		}

		private GridCellCache(IMyCubeGrid grid)
		{
			m_logger = new Logger(() => grid.DisplayName);
			m_grid = grid;

			List<IMySlimBlock> dummy = new List<IMySlimBlock>();
			MainLock.UsingShared(() => {
				using (lock_cellPositions.AcquireExclusiveUsing())
					grid.GetBlocks(dummy, slim => {
						Add(slim);
						return false;
					});

				grid.OnBlockAdded += grid_OnBlockAdded;
				grid.OnBlockRemoved += grid_OnBlockRemoved;
				grid.OnClosing += grid_OnClosing;
			});

			m_logger.debugLog("Initialized");
		}

		public void ForEach(Action<Vector3I> action)
		{
			using (lock_cellPositions.AcquireSharedUsing())
			{
				foreach (Vector3I cell in CellPositions)
					action(cell);

				foreach (MyCubeBlock door in LargeDoors)
				{
					if (door.Closed)
						continue;

					Dictionary<string, MyEntitySubpart> subparts = door.Subparts;
					foreach (var part in subparts)
						action(m_grid.WorldToGridInteger(part.Value.PositionComp.GetPosition()));
				}
			}
		}

		public void ForEach(Func<Vector3I, bool> function)
		{
			using (lock_cellPositions.AcquireSharedUsing())
			{
				foreach (Vector3I cell in CellPositions)
					if (function(cell))
						return;

				foreach (MyCubeBlock door in LargeDoors)
				{
					if (door.Closed)
						continue;

					Dictionary<string, MyEntitySubpart> subparts = door.Subparts;
					foreach (var part in subparts)
						if (function(m_grid.WorldToGridInteger(part.Value.PositionComp.GetPosition())))
							return;
				}
			}
		}

		public IEnumerable<Vector3I> EachCell()
		{
			using (lock_cellPositions.AcquireSharedUsing())
			{
				foreach (Vector3I cell in CellPositions)
					yield return cell;

				foreach (MyCubeBlock door in LargeDoors)
				{
					if (door.Closed)
						continue;

					foreach (var part in door.Subparts)
						yield return m_grid.WorldToGridInteger(part.Value.PositionComp.GetPosition());
				}
			}
		}

		/// <summary>
		/// Gets the closest occupied cell.
		/// </summary>
		public void GetClosestOccupiedCell(ref Vector3I startCell, ref Vector3I previousCell, out Vector3I closestCell)
		{
			closestCell = previousCell;
			int closestDistance;
			using (lock_cellPositions.AcquireSharedUsing())
				// bias against switching
				closestDistance = CellPositions.Contains(previousCell) ? previousCell.DistanceSquared(startCell) - 2 : int.MaxValue;

			foreach (Vector3I cell in EachCell())
			{
				int dist = cell.DistanceSquared(startCell);
				if (dist < closestDistance)
				{
					closestCell = cell;
					closestDistance = dist;
				}
			}
		}

		/// <summary>
		/// Gets the closest occupied cell.
		/// </summary>
		public void GetClosestOccupiedCell(ref Vector3D startWorld, ref Vector3I previousCell, out Vector3I closestCell)
		{
			Vector3I startPoint = m_grid.WorldToGridInteger(startWorld);
			GetClosestOccupiedCell(ref startPoint, ref previousCell, out closestCell);
		}

		private void grid_OnClosing(IMyEntity obj)
		{
			IMyCubeGrid grid = obj as IMyCubeGrid;
			grid.OnBlockAdded -= grid_OnBlockAdded;
			grid.OnBlockRemoved -= grid_OnBlockRemoved;
			grid.OnClosing -= grid_OnClosing;
			if (lock_cellCache == null)
				return;
			using (lock_cellPositions.AcquireExclusiveUsing())
			{
				CellPositions = null;
				LargeDoors = null;
			}
			lock_cellPositions = null;
			using (lock_cellCache.AcquireExclusiveUsing())
				CellCache.Remove(grid);
		}

		private void grid_OnBlockAdded(IMySlimBlock slim)
		{
			using (lock_cellPositions.AcquireExclusiveUsing())
				Add(slim);
		}

		private void grid_OnBlockRemoved(IMySlimBlock slim)
		{
			using (lock_cellPositions.AcquireExclusiveUsing())
			{
				if (slim.FatBlock is IMyDoor && ((MyCubeBlock)slim.FatBlock).Subparts.Count != 0)
				{
					LargeDoors.Remove((MyCubeBlock)slim.FatBlock);
					return;
				}

				// some cells may not be occupied
				slim.ForEachCell(cell => {
					CellPositions.Remove(cell);
					return false;
				});
			}
		}

		private void Add(IMySlimBlock slim)
		{
			bool checkLocal;
			if (slim.FatBlock != null)
			{
				if (slim.FatBlock is IMyDoor && ((MyCubeBlock)slim.FatBlock).Subparts.Count != 0)
				{
					LargeDoors.Add((MyCubeBlock)slim.FatBlock);
					return;
				}

				checkLocal = slim.FatBlock is IMyMotorStator || slim.FatBlock is IMyPistonBase;
			}
			else
				checkLocal = false;

			slim.ForEachCell(cell => {
				if (checkLocal)
				{
					// for piston base and stator, cell may not actually be inside local AABB
					// if this is done for doors, they would always be treated as open
					// other blocks have not been tested
					Vector3 positionBlock = Vector3.Transform(slim.CubeGrid.GridIntegerToWorld(cell), slim.FatBlock.WorldMatrixNormalizedInv);

					if (slim.FatBlock.LocalAABB.Contains(positionBlock) == ContainmentType.Disjoint)
						return false;
				}

				CellPositions.Add(cell);
				return false;
			});
		}

	}

	/// <summary>
	/// <para>Creates a List of every occupied cell for a grid. This List is used to create projections of the grid.</para>
	/// <para>This class only ever uses local positions.</para>
	/// </summary>
	/// TODO: Contains() based comparison of rejected cells
	public class GridShapeProfiler
	{

		///// <summary>Added to required distance when not landing</summary>
		//private const float NotLandingBuffer = 5f;

		private Logger m_logger = new Logger();
		private IMyCubeGrid m_grid;
		private GridCellCache m_cellCache;
		private Vector3 m_centreRejection;
		private Vector3 m_directNorm;
		private readonly MyUniqueList<Vector3> m_rejectionCells = new MyUniqueList<Vector3>();
		private readonly FastResourceLock m_lock_rejcectionCells = new FastResourceLock();
		private bool m_landing;

		public Capsule Path { get; private set; }

		private Vector3 Centre { get { return m_grid.LocalAABB.Center; } }

		public GridShapeProfiler() { }

		public void Init(IMyCubeGrid grid, RelativePosition3F destination, Vector3 navBlockLocalPosition, bool landing)
		{
			if (grid != m_grid)
			{
				this.m_grid = grid;
				this.m_logger = new Logger(() => m_grid.DisplayName);
				this.m_cellCache = GridCellCache.GetCellCache(grid);
			}

			m_directNorm = Vector3.Normalize(destination.ToLocal() - navBlockLocalPosition);
			m_landing = landing;
			Vector3 centreDestination = destination.ToLocal() + Centre - navBlockLocalPosition;
			rejectAll();
			createCapsule(centreDestination, navBlockLocalPosition);
		}

		/// <summary>
		/// Rejection test for intersection with the profiled grid.
		/// </summary>
		/// <param name="grid">Grid whose cells will be rejected and compared to the profiled grid's rejections.</param>
		/// <returns>True iff there is a collision</returns>
		public bool rejectionIntersects(IMyCubeGrid grid, IMyCubeBlock ignore, out MyEntity entity, out Vector3? pointOfObstruction)
		{
			m_logger.debugLog("m_grid == null", Logger.severity.FATAL, condition: m_grid == null);

			//m_logger.debugLog("testing grid: " + grid.getBestName(), "rejectionIntersects()");

			GridCellCache gridCache = GridCellCache.GetCellCache(grid);
			MatrixD toLocal = m_grid.WorldMatrixNormalizedInv;
			Line pathLine = Path.get_Line();

			float minDist = m_grid.GridSize + grid.GridSize;
			//if (!m_landing)
			//	minDist += NotLandingBuffer;
			float pathRadius = Path.Radius + minDist;
			float minDistSquared = minDist * minDist;

			MyEntity entity_in = null;
			Vector3? pointOfObstruction_in = null;
			using (m_lock_rejcectionCells.AcquireSharedUsing())
				gridCache.ForEach(cell => {
					Vector3 world = grid.GridIntegerToWorld(cell);
					//m_logger.debugLog("checking position: " + world, "rejectionIntersects()");
					if (pathLine.PointInCylinder(pathRadius, world))
					{
						//m_logger.debugLog("point in cylinder: " + world, "rejectionIntersects()");
						Vector3 local = Vector3.Transform(world, toLocal);
						if (rejectionIntersects(local, minDistSquared))
						{
							entity_in = grid.GetCubeBlock(cell).FatBlock as MyEntity ?? grid as MyEntity;
							if (ignore != null && entity_in == ignore)
								return false;

							pointOfObstruction_in = pathLine.ClosestPoint(world);
							return true;
						}
					}
					return false;
				});

			if (pointOfObstruction_in.HasValue)
			{
				entity = entity_in;
				pointOfObstruction = pointOfObstruction_in;
				return true;
			}

			entity = null;
			pointOfObstruction = null;
			return false;
		}

		/// <summary>
		/// Rejection test for intersection with the profiled grid.
		/// </summary>
		/// <param name="localMetresPosition">The local position in metres.</param>
		/// <returns>true if the rejection collides with one or more of the grid's rejections</returns>
		private bool rejectionIntersects(Vector3 localMetresPosition, float minDistSquared)
		{
			Vector3 TestRejection = RejectMetres(localMetresPosition);
			foreach (Vector3 ProfileRejection in m_rejectionCells)
			{
				//m_logger.debugLog("distance between: " + Vector3.DistanceSquared(TestRejection, ProfileRejection), "rejectionIntersects()");
				if (Vector3.DistanceSquared(TestRejection, ProfileRejection) < minDistSquared)
					return true;
			}
			return false;
		}

		private Vector3 RejectMetres(Vector3 metresPosition)
		{ return Vector3.Reject(metresPosition, m_directNorm); }

		/// <summary>
		/// Perform a vector rejection of every occupied cell from DirectionNorm and store the results in rejectionCells.
		/// </summary>
		private void rejectAll()
		{
			using (m_lock_rejcectionCells.AcquireExclusiveUsing())
			{
				m_rejectionCells.Clear();

				m_centreRejection = RejectMetres(Centre);
				m_cellCache.ForEach(cell => {
					Vector3 rejection = RejectMetres(cell * m_grid.GridSize);
					m_rejectionCells.Add(rejection);
				});
			}
		}

		/// <param name="centreDestination">where the centre of the grid will end up (local)</param>
		private void createCapsule(Vector3 centreDestination, Vector3 localPosition)
		{
			float longestDistanceSquared = 0;
			using (m_lock_rejcectionCells.AcquireSharedUsing())
				foreach (Vector3 rejection in m_rejectionCells)
				{
					float distanceSquared = (rejection - m_centreRejection).LengthSquared();
					if (distanceSquared > longestDistanceSquared)
						longestDistanceSquared = distanceSquared;
				}
			Vector3 P0 = RelativePosition3F.FromLocal(m_grid, Centre).ToWorld();

			//Vector3D P1;
			//if (m_landing)
			//{
			Vector3 P1 = RelativePosition3F.FromLocal(m_grid, centreDestination).ToWorld();
			//}
			//else
			//{
			//	//// extend capsule past destination by distance between remote and front of grid
			//	//Ray navTowardsDest = new Ray(localPosition, m_directNorm);
			//	//float tMin, tMax;
			//	//m_grid.LocalVolume.IntersectRaySphere(navTowardsDest, out tMin, out tMax);
			//	//P1 = RelativeVector3F.createFromLocal(centreDestination + tMax * m_directNorm, m_grid).getWorldAbsolute();

			//	// extend capsule by length of grid
			//	P1 = RelativePosition3F.FromLocal(m_grid, centreDestination + m_directNorm * m_grid.GetLongestDim()).ToWorld();
			//}

			float CapsuleRadius = (float)Math.Sqrt(longestDistanceSquared) + 3f * m_grid.GridSize;// +(m_landing ? 0f : NotLandingBuffer);
			Path = new Capsule(P0, P1, CapsuleRadius);

			m_logger.debugLog("Path capsule created from " + P0 + " to " + P1 + ", radius: " + CapsuleRadius);
		}

	}
}
