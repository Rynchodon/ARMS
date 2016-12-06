#if DEBUG
#define TRACE
#define TEST_IN_SPACE
#define DEBUG_DRAW
#endif

using System;
using System.Collections.Generic;
using Rynchodon.Threading;
using Rynchodon.Utility.Vectors;
using Sandbox.Game.Entities;
using Sandbox.ModAPI;
using VRage.Game;
using VRage.Game.ModAPI;
using VRageMath;

namespace Rynchodon.Autopilot.Movement
{
	/// <summary>
	/// Applies air resistance to a grid.
	/// </summary>
	class AeroProfiler_old
	{

		/// <summary>
		/// Affects the ammount of air moved from a cell with high air pressure to a cell with low air pressure.
		/// CellEqualization * neighbour count must be less than one or the simulation becomes unstable.
		/// </summary>
		private const float CellEqualization = 1f / 32f;
		/// <summary>
		/// Extra cells around the grid where air flow will be considered
		/// </summary>
		private const int GridPadding = 4;

		private static ThreadManager Thread = new ThreadManager(2, true, typeof(AeroProfiler_old).Name);

		private readonly Logger m_logger;
		private readonly IMyCubeGrid m_grid;
		private readonly FastResourceLock m_lock = new FastResourceLock();

		private Dictionary<Vector3I, float> m_cellAirPressure, m_previousCellAirPressure;

		private Vector3I m_minCell, m_maxCell;
		private Vector3 m_drag;

		public AeroProfiler_old(IMyCubeGrid grid)
		{
			this.m_logger = new Logger(grid);
			this.m_grid = grid;
		}

		public void Update1()
		{
			if (m_drag != Vector3.Zero)
				m_grid.Physics.AddForce(VRage.Game.Components.MyPhysicsForceType.APPLY_WORLD_FORCE, m_drag, null, null);
			//Thread.EnqueueAction(Run);
			Calculate();
		}

		private void Run()
		{
			if (m_lock.TryAcquireExclusive())
			{
				try { Calculate(); }
				finally { m_lock.ReleaseExclusive(); }
			}
		}

		private void Calculate()
		{
			if (m_grid.IsStatic || m_grid.Physics == null)
			{
				ZeroAero();
				return;
			}

#if TEST_IN_SPACE
			const float airDensity = 1f;
#else
			Vector3D position = m_grid.GetCentre();
			MyPlanet closestPlanet = MyPlanetExtensions.GetClosestPlanet(position);
			if (closestPlanet == null)
			{
				ZeroAero();
				return;
			}

			float airDensity = MyPlanetExtensions.GetClosestPlanet(position).GetAirDensity(position);

			if (airDensity <= 0f)
			{
				ZeroAero();
				return;
			}
#endif

			m_minCell = m_grid.Min - GridPadding;
			m_maxCell = m_grid.Max + GridPadding;

			if (m_cellAirPressure == null)
			{
				m_cellAirPressure = new Dictionary<Vector3I, float>();
				m_previousCellAirPressure = new Dictionary<Vector3I, float>();
			}

			AddAirPressure();
			EqualizeAirPressure();



			DebugDraw_Pressure();
		}

		private void ZeroAero()
		{
			m_drag = Vector3.Zero;
			m_cellAirPressure = null;
			m_previousCellAirPressure = null;
		}

		private bool IsGridCell(ref Vector3I cell)
		{
			return
				m_minCell.X <= cell.X && cell.X <= m_maxCell.X &&
				m_minCell.Y <= cell.Y && cell.Y <= m_maxCell.Y &&
				m_minCell.Z <= cell.Z && cell.Z <= m_maxCell.Z;
		}

		/// <summary>
		/// Add air pressure on the leading surfaces of the grid.
		/// </summary>
		private void AddAirPressure()
		{
			Vector3 localVelocity = ((DirectionWorld)m_grid.Physics.LinearVelocity).ToGrid(m_grid);
			float pressureMulti = 1f / (m_grid.GridSize * Globals.UpdatesPerSecond);

			foreach (Vector3I direction in Base6Directions.IntDirections)
			{
				float airPressureChange = -Vector3.Dot(direction, localVelocity);
				if (airPressureChange < 1f)
					continue;
				airPressureChange *= pressureMulti;

				foreach (Vector3I hitCell in m_grid.FirstBlocks(direction))
				{
					Vector3I prevCell = hitCell - direction;
					float cellAirPressure;
					if (!m_cellAirPressure.TryGetValue(prevCell, out cellAirPressure))
						cellAirPressure = 0f;
					cellAirPressure += airPressureChange;
					m_cellAirPressure[prevCell] = cellAirPressure;
				}
			}
		}

		/// <summary>
		/// Equalize air pressure between each cell and its neighbours.
		/// </summary>
		private void EqualizeAirPressure()
		{
			const float min = 0.1f, nMin = -min;

			Globals.Swap(ref m_cellAirPressure, ref m_previousCellAirPressure);
			m_cellAirPressure.Clear();

			foreach (KeyValuePair<Vector3I, float> cell in m_previousCellAirPressure)
			{
				float cellAirPressure = cell.Value;

				foreach (Vector3I offset in Globals.NeighboursOne)
				{
					Vector3I neighbourCell = cell.Key + offset;
					if (m_grid.CubeExists(neighbourCell))
						continue;

					float neighbourAirPressure;
					bool neighbourMissing;
					if (neighbourMissing = !m_previousCellAirPressure.TryGetValue(neighbourCell, out neighbourAirPressure))
						neighbourAirPressure = 0f;

					float change = (neighbourAirPressure - cell.Value) * CellEqualization;
					cellAirPressure += change;
					if (neighbourMissing && (change > min || change < nMin) && IsGridCell(ref neighbourCell))
						m_cellAirPressure[neighbourCell] = 0f;
					//m_logger.traceLog("For Cell: " + cell.Key + ", AD: " + cell.Value + ", Neighbour AD: " + neighbourAirPressure + ", change: " + change);
				}

				if (cellAirPressure > min || cellAirPressure < nMin)
				{
					//m_logger.traceLog("For Cell: " + cell.Key + ", AD: " + cell.Value + " => " + cellAirPressure);
					m_cellAirPressure[cell.Key] = cellAirPressure;
				}
				else
				{
					//m_logger.traceLog("For Cell: " + cell.Key + ", AD near zero: " + cellAirPressure);
				}
			}

			//m_logger.traceLog("Cell count: " + m_cellAirPressure.Count);
		}

		/// <summary>
		/// Move air around the grid based on the grid's velocity.
		/// </summary>
		private void MoveAir()
		{
			//Globals.Swap(ref m_cellAirPressure, ref m_previousCellAirPressure);
			//m_cellAirPressure.Clear();

			Vector3 localVelocity = ((DirectionWorld)m_grid.Physics.LinearVelocity).ToGrid(m_grid);
			float pressureMulti = 1f / (m_grid.GridSize * Globals.UpdatesPerSecond);

			// move air along the grid
			foreach (Vector3I direction in Base6Directions.IntDirections)
			{
				float airPressureChange = -Vector3.Dot(direction, localVelocity);
				if (airPressureChange < 1f)
					continue;
				airPressureChange *= pressureMulti;

				foreach (KeyValuePair<Vector3I, float> cell in m_previousCellAirPressure)
				{
					Vector3I nextCell = cell.Key + direction;
					Vector3I previousCell = cell.Key - direction;

					float cellAirPressure;
					//if (!m_cellAirPressure.TryGetValue(cell.Key, out cellAirPressure))
					cellAirPressure = cell.Value;
					float newCellAirPressure = cellAirPressure;

					if (!m_grid.CubeExists(nextCell))
					{
						//float nextCellAirPressure;
						//if (!m_previousCellAirPressure.TryGetValue(nextCell, out nextCellAirPressure))
						//	nextCellAirPressure = 0f;

						//newCellAirPressure += (nextCellAirPressure - cellAirPressure) * 0.25f;
						//m_logger.debugLog("cell: " + cell.Key + ", pressure: " + cell.Value + ", next cell pressure: " + nextCellAirPressure + ", new pressure: " + newCellAirPressure);
						newCellAirPressure = 0f;
					}
					if (!m_grid.CubeExists(previousCell))
					{
						float previousCellAirPressure;
						if (!m_previousCellAirPressure.TryGetValue(previousCell, out previousCellAirPressure))
							previousCellAirPressure = 0f;

						newCellAirPressure += previousCellAirPressure;
						m_logger.debugLog("cell: " + cell.Key + ", pressure: " + cell.Value + ", previous cell pressure: " + previousCellAirPressure + ", new pressure: " + newCellAirPressure);
					}

					//m_logger.debugLog("cell: "+cell.Key+", pressure: "+cell.Value+", next pressure: "+
					m_cellAirPressure[cell.Key] = newCellAirPressure;

					//float cellAirPressure;
					//if (!m_cellAirPressure.TryGetValue(cell.Key, out cellAirPressure))
					//	cellAirPressure = cell.Value;

					//float nextCellAirPressure;
					//if (!m_cellAirPressure.TryGetValue(nextCell, out nextCellAirPressure) && !m_previousCellAirPressure.TryGetValue(nextCell, out nextCellAirPressure))
					//	nextCellAirPressure = 0f;

					//if (IsGridCell(ref previousCell))
					//	m_cellAirPressure[cell.Key] = cellAirPressure - airPressureChange;
					//else
					//	m_cellAirPressure.Remove(cell.Key);
					//if (IsGridCell(ref nextCell))
					//	m_cellAirPressure[nextCell] = nextCellAirPressure + airPressureChange;
				}
			}

			// add air pressure on the leading surfaces of the grid
			foreach (Vector3I direction in Base6Directions.IntDirections)
			{
				float airPressureChange = -Vector3.Dot(direction, localVelocity);
				if (airPressureChange < 1f)
					continue;
				airPressureChange *= pressureMulti;

				foreach (Vector3I hitCell in m_grid.FirstBlocks(direction))
				{
					Vector3I prevCell = hitCell - direction;
					float cellAirPressure;
					if (!m_cellAirPressure.TryGetValue(prevCell, out cellAirPressure))
						cellAirPressure = 0f;
					cellAirPressure += airPressureChange;
					m_cellAirPressure[prevCell] = cellAirPressure;
				}
			}
		}

		private void CalculateDrag(float airDensity)
		{
			Vector3 dragForce = Vector3.Zero;
			Vector3 localVelocity = ((DirectionWorld)m_grid.Physics.LinearVelocity).ToGrid(m_grid);

			foreach (Vector3I direction in Base6Directions.IntDirections)
			{
				float velocityInDirection = Vector3.Dot(direction, localVelocity);
				if (-1f < velocityInDirection && velocityInDirection < 1f)
					// skin friction
					continue;

				// form friction
				foreach (KeyValuePair<Vector3I, float> cell in m_cellAirPressure)
				{
					Vector3I checkCell = cell.Key + direction;
					if (!m_grid.CubeExists(checkCell))
						continue;

					dragForce += direction * cell.Value;
					//DebugDraw_Drag(cell.Key, cell.Value);
				}
			}

			float gridSize = m_grid.GridSize;
			dragForce *= gridSize * gridSize * airDensity * 100f;
			m_drag = ((DirectionGrid)dragForce).ToWorld(m_grid);
			m_logger.debugLog("Drag: " + dragForce + ", world: " + m_drag);
		}

#if DEBUG_DRAW
		private const int minDraw = 10, maxDraw = 1000, defaultDrawThreshold = 1;
		private int drawThreshold = defaultDrawThreshold;
#endif

		[System.Diagnostics.Conditional("DEBUG_DRAW")]
		private void DebugDraw_Pressure()
		{
#if DEBUG_DRAW
			if (!ThreadTracker.IsGameThread)
			{
				m_logger.alwaysLog("Can't draw, not game thread");
				return;
			}

			MatrixD worldMatrix = m_grid.WorldMatrix;
			float radius = m_grid.GridSize * 0.125f;
			int drawn = 0;
			foreach (KeyValuePair<Vector3I, float> pair in m_cellAirPressure)
			{
				int value = (int)(pair.Value * 10f);
				//if (value > -drawThreshold && value < drawThreshold)
				//	continue;
				drawn++;
				Color c = pair.Value > 0 ? new Color(255, 255 - value, 255 - value) : new Color(255 + value, 255 + value, 255);
				worldMatrix.Translation = m_grid.GridIntegerToWorld(pair.Key);
				MySimpleObjectDraw.DrawTransparentSphere(ref worldMatrix, radius, ref c, MySimpleObjectRasterizer.Solid, 4);
			}

			if (drawn > maxDraw)
			{
				drawThreshold = drawThreshold << 1;
				m_logger.debugLog("Increased draw threshold to " + drawThreshold);
			}
			else if (drawThreshold > defaultDrawThreshold && drawn < minDraw)
			{
				drawThreshold = drawThreshold >> 1;
				m_logger.debugLog("Decreased draw threshold to " + drawThreshold);
			}
#endif
		}

		[System.Diagnostics.Conditional("DEBUG_DRAW")]
		private void DebugDraw_Drag(Vector3I cell, float pressure)
		{
			if (!ThreadTracker.IsGameThread)
			{
				m_logger.debugLog("Can't draw, not game thread");
				return;
			}

			MatrixD worldMatrix = m_grid.WorldMatrix;
			float radius = m_grid.GridSize * 0.125f;

			Vector3D worldPosition = m_grid.GridIntegerToWorld(cell);
			worldMatrix.Translation = worldPosition;

			Color c = new Color(255, 255, 255 - (int)(Math.Abs(pressure) * 100f));
			MySimpleObjectDraw.DrawTransparentSphere(ref worldMatrix, radius, ref c, MySimpleObjectRasterizer.Solid, 4);
		}

	}
}
