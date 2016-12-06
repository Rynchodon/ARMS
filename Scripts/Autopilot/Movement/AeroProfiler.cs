#if DEBUG
//#define TRACE
#endif

using System;
using Rynchodon.Threading;
using Sandbox.Game.Entities;
using VRage.Game;
using VRage.Game.Components;
using VRage.Game.ModAPI;
using VRageMath;

namespace Rynchodon.Autopilot.Movement
{
	class AeroProfiler
	{
		private class AeroCell
		{
			public float CurAirPress, NextAirPress;
			public float CurDemand, NextDemand;
			public Vector3 CurVelocity, NextVelocity;

			public AeroCell() { }

			public void Copy(AeroCell copy)
			{
				CurAirPress = copy.CurAirPress;
				CurDemand = copy.CurDemand;
				CurVelocity = copy.CurVelocity;
				NextAirPress = copy.NextAirPress;
				NextDemand = copy.NextDemand;
				NextVelocity = copy.NextVelocity;
			}

			public void ApplyChanges()
			{
				CurAirPress = NextAirPress;
				CurDemand = NextDemand;
				CurVelocity = NextVelocity;
			}

			public override string ToString()
			{
				return "AirPress: " + NextAirPress + ", Demand: " + NextDemand + ", Velocity: " + NextVelocity;
			}
		}

		private static readonly Vector3I[] LowHigh =
		{
			new Vector3I(0, 0, 0),
			new Vector3I(0, 0, 1),
			new Vector3I(0, 1, 0),
			new Vector3I(0, 1, 1),
			new Vector3I(1, 0, 0),
			new Vector3I(1, 0, 1),
			new Vector3I(1, 1, 0),
			new Vector3I(1, 1, 1),
		};

		/// <summary>
		/// Affects the ammount of air moved from a cell with high air pressure to a cell with low air pressure.
		/// CellEqualization * neighbour count must be less than one or the simulation becomes unstable.
		/// </summary>
		private const float CellEqualization = 1f / 32f;
		/// <summary>
		/// Extra cells around the grid where air flow will be considered
		/// </summary>
		private const int GridPadding = 4;

		private static ThreadManager Thread = new ThreadManager(2, true, typeof(AeroProfiler).Name);

		private readonly Logger m_logger;
		private readonly IMyCubeGrid m_grid;

		private AeroCell[,,] m_data;

		private Vector3I m_minCell, m_maxCell, m_sizeCell, m_direction;
		private AeroCell m_defaultData;

		public bool Running { get; private set; }
		public bool Success { get; private set; }

		public AeroProfiler(IMyCubeGrid grid)
		{
			this.m_logger = new Logger(grid);
			this.m_grid = grid;

			Running = true;
			Success = false;
			Thread.EnqueueAction(Run);
		}

		private void Run()
		{
			try
			{
				Calculate();
				Success = true;
			}
			finally
			{
				Running = false;
			}
		}

		private void Calculate()
		{
			m_minCell = m_grid.Min - GridPadding;
			m_maxCell = m_grid.Max + GridPadding;
			m_sizeCell = m_maxCell - m_minCell + 1;

			m_data = new AeroCell[m_sizeCell.X, m_sizeCell.Y, m_sizeCell.Z];

			m_defaultData.CurAirPress = 1f;

			// TODO: foreach direction
			m_direction = Base6Directions.IntDirections[0];

			m_defaultData.CurVelocity = m_direction * -1.5f;

			SetDefault();

			SaltAir();

			Vector3 drag = Vector3.Zero;
			Vector3 previousDrag = Vector3.Zero;

			int minSteps = m_sizeCell.AbsMax();
			int maxSteps = m_sizeCell.Volume();
			int steps = 0;
			while (true)
			{
				if (steps > minSteps)
				{
					float diff; Vector3.DistanceSquared(ref drag, ref previousDrag, out diff);
					if (diff < 0.0001f)
					{
						m_logger.debugLog("Drag is stable at " + drag, Logger.severity.DEBUG);
						break;
					}
					if (steps == maxSteps)
						throw new Exception("Algorithm is unstable");
				}

				m_logger.debugLog("Steps: " + steps + ", current drag: " + drag + ", previous drag: " + previousDrag);

				steps++;
				previousDrag = drag;

				DiffuseAir();
				ApplyAllChanges();
				MoveAir();
				ApplyAllChanges();
				CalculateDrag(out drag);

#if DEBUG
				if (m_defaultData.NextAirPress != 0f)
					m_logger.alwaysLog("Default air pressure modification attempt: " + m_defaultData.NextAirPress, Logger.severity.ERROR);
				if (m_defaultData.NextDemand != 0f)
					m_logger.alwaysLog("Default demand modification attempt: " + m_defaultData.NextDemand, Logger.severity.ERROR);
				if (m_defaultData.NextVelocity != Vector3.Zero)
					m_logger.alwaysLog("Default velocity modification attempt: " + m_defaultData.NextVelocity, Logger.severity.ERROR);
#endif
			}

			// TODO: report
			// TODO: cleanup
		}

		private void SetDefault()
		{
			Vector3I index;
			for (index.X = 0; index.X < m_sizeCell.X; index.X++)
				for (index.Y = 0; index.Y < m_sizeCell.Y; index.Y++)
					for (index.Z = 0; index.Z < m_sizeCell.Z; index.Z++)
					{
						AeroCell data = m_data[index.X, index.Y, index.Z];
						if (data == null)
						{
							data = new AeroCell();
							m_data[index.X, index.Y, index.Z] = data;
						}
						data.Copy(m_defaultData);
					}
		}

		/// <summary>
		/// Salt air at the leading edges.
		/// </summary>
		private void SaltAir()
		{
			foreach (Vector3I hitCell in m_grid.FirstBlocks(m_direction))
			{
				Vector3I prevIndex = hitCell - m_direction - m_minCell;
				AeroCell cellData;
				GetValue(ref prevIndex, out cellData);
				cellData.NextAirPress = cellData.CurAirPress + 0.001f;
				cellData.ApplyChanges();
				m_logger.debugLog("Salted air at " + (hitCell - m_direction) + ", " + cellData);
			}
		}

		/// <summary>
		/// Move air by m_cellVelocity.
		/// </summary>
		private void MoveAir()
		{
			float gridSize = m_grid.GridSize;

			Vector3I index;
			for (index.X = 0; index.X < m_sizeCell.X; index.X++)
				for (index.Y = 0; index.Y < m_sizeCell.Y; index.Y++)
					for (index.Z = 0; index.Z < m_sizeCell.Z; index.Z++)
					{
						AeroCell currentData;
						GetValue(ref index, out currentData);
						if (currentData.CurAirPress == 1f)
							continue;

						Vector3I currentCell = index + m_minCell;
						// blocks should have air pressure == 1
						m_logger.debugLog("Block exists at " + currentCell + ", " + currentData, Logger.severity.ERROR, condition: m_grid.CubeExists(currentCell));

						currentData.NextVelocity = currentData.CurVelocity;
						Vector3 sourceCellAvg = currentCell + currentData.NextVelocity;

						while (true)
						{
							if (currentData.NextVelocity.AbsMax() < 0.1f)
							{
								m_logger.traceLog("Air is stopped at " + currentCell + ", " + currentData);
								break;
							}

							// check for blocks between here and source
							Vector3 sourceLocalAvg; Vector3.Multiply(ref sourceCellAvg, gridSize, out sourceLocalAvg);
							LineD line = new LineD(m_grid.GridIntegerToWorld(currentCell), Vector3D.Transform(sourceLocalAvg, m_grid.WorldMatrix));
							MyCubeGridHitInfo hitInfo = new MyCubeGridHitInfo();
							if (((MyCubeGrid)m_grid).GetIntersectionWithLine(ref line, ref hitInfo, IntersectionFlags.DIRECT_TRIANGLES))
							{
								Vector3 norm = hitInfo.Triangle.NormalInObjectSpace;
								m_logger.debugLog("Air intersects block, cell: " + currentCell + ", velocity: " + currentData.NextVelocity + ", block position: " + hitInfo.Position + ", normal: " + norm);

								float projectionLength; Vector3.Dot(ref currentData.NextVelocity, ref norm, out projectionLength);
								m_logger.traceLog("Normal is in wrong direction, velocity: " + currentData.NextVelocity + ", normal: " + norm + ", dot: " + projectionLength, Logger.severity.ERROR, condition: projectionLength >= 0f);
								Vector3 projection; Vector3.Multiply(ref norm, -projectionLength, out projection);
								Vector3.Add(ref currentData.NextVelocity, ref projection, out currentData.NextVelocity);
								sourceCellAvg = currentCell + currentData.NextVelocity;

								m_logger.debugLog("projection: " + projection + ", " + currentData);
							}
							else
							{
								m_logger.traceLog("Cell: " + currentCell + ", " + currentData);
								break;
							}
						}

						currentData.NextAirPress = 0f;
						currentData.NextVelocity = Vector3.Zero;

						// pull air
						Vector3I low;
						low.X = (int)Math.Floor(sourceCellAvg.X);
						low.Y = (int)Math.Floor(sourceCellAvg.Y);
						low.Z = (int)Math.Floor(sourceCellAvg.Z);

						Vector3I high = low + 1;

						Vector3 ratioLow = high - sourceCellAvg;
						Vector3 ratioHigh = sourceCellAvg - low;

						foreach (Vector3I sourceLowHigh in LowHigh)
						{
							Vector3I sourceCell = low + sourceLowHigh;
							Vector3I sourceIndex = sourceCell - m_minCell;

							Vector3 sourceRatioParts;
							float sourceRatio;

							if (sourceLowHigh.X == 0)
								sourceRatioParts.X = ratioLow.X;
							else
								sourceRatioParts.X = ratioHigh.X;

							if (sourceLowHigh.Y == 0)
								sourceRatioParts.Y = ratioLow.Y;
							else
								sourceRatioParts.Y = ratioHigh.Y;

							if (sourceLowHigh.Z == 0)
								sourceRatioParts.Z = ratioLow.Z;
							else
								sourceRatioParts.Z = ratioHigh.Z;

							sourceRatio = sourceRatioParts.Volume;

							if (sourceRatio < 0.01f)
								continue;

							AeroCell sourceData;
							GetValueOrDefault(ref sourceIndex, out sourceData);
							//m_logger.traceLog("Air moving to " + currentCell + " from " + sourceCell + ", ratio: " + sourceRatio + "/" + sourceRatioParts + ", pressure: " + sourceData.AirPress + ", velocity: " + sourceData.Velocity);

							currentData.NextAirPress += sourceData.CurAirPress * sourceRatio;
							Vector3 transferredVelocity; Vector3.Multiply(ref sourceData.CurVelocity, sourceRatio, out transferredVelocity);
							Vector3.Add(ref currentData.NextVelocity, ref transferredVelocity, out currentData.NextVelocity);
						}

						m_logger.traceLog("Cell: " + currentCell + ", " + currentData);
					}
		}

		/// <summary>
		/// Partially equalize air between each cell and its neighbours.
		/// </summary>
		private void DiffuseAir()
		{
			Vector3I index;
			for (index.X = 0; index.X < m_sizeCell.X; index.X++)
				for (index.Y = 0; index.Y < m_sizeCell.Y; index.Y++)
					for (index.Z = 0; index.Z < m_sizeCell.Z; index.Z++)
					{
						Vector3I cell = index + m_minCell;
						if (m_grid.CubeExists(cell))
							continue;

						AeroCell currentData;
						GetValue(ref index, out currentData);

						foreach (Vector3I offset in Globals.NeighboursOne)
						{
							Vector3I neighbourCell = cell + offset;
							if (m_grid.CubeExists(neighbourCell))
								continue;

							Vector3I neighbourIndex = neighbourCell - m_minCell;
							AeroCell neighbourData;
							GetValueOrDefault(ref neighbourIndex, out neighbourData);

							if (currentData.CurAirPress == neighbourData.CurAirPress)
							{
								//m_logger.traceLog("Cell: " + cell + ", neighbour: " + neighbourCell + ", air pressure: " + currentData.AirPress + ", neighbour pressure: " + neighbourData.AirPress);
								continue;
							}

							float airChange = (neighbourData.CurAirPress - currentData.CurAirPress) * CellEqualization;
							currentData.NextAirPress = currentData.CurAirPress + airChange;

							if (airChange > 0f)
							{
								// this cell gets air from neighbour
								Vector3 inheritedVelocity; Vector3.Multiply(ref neighbourData.CurVelocity, airChange, out inheritedVelocity);
								Vector3 diffusionVelocity = Vector3.Multiply(offset, airChange);
								Vector3 inheritDiffuse; Vector3.Add(ref inheritedVelocity, ref diffusionVelocity, out inheritDiffuse);
								Vector3.Add(ref currentData.CurVelocity, ref inheritDiffuse, out currentData.NextVelocity);

								m_logger.traceLog("Air moved from " + cell + " to " + neighbourCell + " is " + airChange + ", velocity change: " + inheritDiffuse + ", velocity: " + currentData.NextVelocity);
							}
							else
							{
								// this cell looses air to neighbour
								Vector3 inheritedVelocity; Vector3.Multiply(ref currentData.CurVelocity, airChange, out inheritedVelocity);
								Vector3 diffusionVelocity = Vector3.Multiply(-offset, airChange);
								Vector3 inheritDiffuse; Vector3.Add(ref inheritedVelocity, ref diffusionVelocity, out inheritDiffuse);
								Vector3.Subtract(ref currentData.CurVelocity, ref inheritDiffuse, out currentData.NextVelocity);

								m_logger.traceLog("Air moved from " + cell + " to " + neighbourCell + " is " + airChange + ", velocity change: " + inheritDiffuse + ", velocity: " + currentData.NextVelocity);
							}
						}

						m_logger.traceLog("Cell: " + cell + ", " + currentData);
					}
		}

		/// <summary>
		/// Calculate drag effect on the grid, from pressures and velocities.
		/// </summary>
		/// <param name="drag">The drag effect on the grid.</param>
		private void CalculateDrag(out Vector3 drag)
		{
			drag = Vector3.Zero;

			foreach (AeroCell currentData in m_data)
			{
				Vector3 effect; Vector3.Subtract(ref currentData.CurVelocity, ref m_defaultData.CurVelocity, out effect);
				Vector3.Subtract(ref drag, ref effect, out drag);
			}

			float gridSize = m_grid.GridSize;
			Vector3.Multiply(ref drag, gridSize * gridSize, out drag);
		}

		private void GetValue(ref Vector3I index, out AeroCell value)
		{
			value = m_data[index.X, index.Y, index.Z];
		}

		private void GetValueOrDefault(ref Vector3I index, out AeroCell value)
		{
			if (!IsGridIndex(ref index))
			{
				value = m_defaultData;
				return;
			}
			value = m_data[index.X, index.Y, index.Z];
		}

		private void ApplyAllChanges()
		{
			foreach (AeroCell data in m_data)
				data.ApplyChanges();
		}

		private bool IsGridCell(ref Vector3I cell)
		{
			return
				m_minCell.X <= cell.X && cell.X <= m_maxCell.X &&
				m_minCell.Y <= cell.Y && cell.Y <= m_maxCell.Y &&
				m_minCell.Z <= cell.Z && cell.Z <= m_maxCell.Z;
		}

		private bool IsGridIndex(ref Vector3I index)
		{
			return
				0 <= index.X && index.X < m_sizeCell.X &&
				0 <= index.Y && index.Y < m_sizeCell.Y &&
				0 <= index.Z && index.Z < m_sizeCell.Z;
		}

		public void DebugDraw_Velocity()
		{
			if (!ThreadTracker.IsGameThread)
				throw new Exception("Not on game thread");

			MatrixD worldMatrix = m_grid.WorldMatrix;
			float radius = m_grid.GridSize * 0.125f;
			MatrixD rotationMatrix = worldMatrix.GetOrientation();
			float lineLength = m_grid.GridSize * 0.25f;
			float defaultSpeed = m_defaultData.CurVelocity.Length();

			Vector3I index;
			for (index.X = 0; index.X < m_sizeCell.X; index.X++)
				for (index.Y = 0; index.Y < m_sizeCell.Y; index.Y++)
					for (index.Z = 0; index.Z < m_sizeCell.Z; index.Z++)
					{
						AeroCell currentData;
						GetValue(ref index, out currentData);

						if (Math.Abs(currentData.CurAirPress - m_defaultData.CurAirPress) < 0.1f && currentData.CurVelocity.Equals(m_defaultData.CurVelocity, 0.1f))
							continue;

						Vector3D worldPosition = m_grid.GridIntegerToWorld(index + m_minCell);
						worldMatrix.Translation = worldPosition;
						Color airPressColour;
						if (currentData.CurAirPress > m_defaultData.CurAirPress)
						{
							int value = 255 - (int)((currentData.CurAirPress - m_defaultData.CurAirPress) / m_defaultData.CurAirPress * 255f);
							airPressColour = new Color(255, value, value);
						}
						else
						{
							int value = 255 - (int)((m_defaultData.CurAirPress - currentData.CurAirPress) / m_defaultData.CurAirPress * 255f);
							airPressColour = new Color(value, value, 255);
						}
						MySimpleObjectDraw.DrawTransparentSphere(ref worldMatrix, radius, ref airPressColour, MySimpleObjectRasterizer.Solid, 4);

						float speed = currentData.CurVelocity.Length();
						Color speedColour;
						if (speed > defaultSpeed)
						{
							int value = 255 - (int)((speed - defaultSpeed) / defaultSpeed * 255f);
							speedColour = new Color(255, value, value);
						}
						else
						{
							int value = 255 - (int)((defaultSpeed - speed) / defaultSpeed * 255f);
							speedColour = new Color(value, value, 255);
						}
						Vector4 speedColourVector = speedColour.ToVector4();

						Vector3.Multiply(ref currentData.CurVelocity, lineLength / speed, out currentData.CurVelocity);
						Vector3D direction = Vector3D.Transform(currentData.CurVelocity, ref rotationMatrix);
						Vector3D worldEnd; Vector3D.Add(ref worldPosition, ref direction, out worldEnd);
						MySimpleObjectDraw.DrawLine(worldPosition, worldEnd, "WeaponLaser", ref speedColourVector, 0.025f);

						//m_logger.debugLog("Index: " + index + ", " + currentData + ", sphere: " + airPressColour + ", line: " + speedColour);
					}
		}

	}
}
