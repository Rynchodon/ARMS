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

		private struct CellData
		{
			public float AirPress;
			/// <summary>
			/// Negative velocity of air in a cell.
			/// </summary>
			public Vector3 Velocity;

			public override string ToString()
			{
				return "AirPress: " + AirPress + ", Velocity: " + Velocity;
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

		private CellData[,,] m_data, m_previous;

		private Vector3I m_minCell, m_maxCell, m_sizeCell, m_direction;
		private CellData m_defaultData;

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

			m_data = new CellData[m_sizeCell.X, m_sizeCell.Y, m_sizeCell.Z];
			m_previous = new CellData[m_sizeCell.X, m_sizeCell.Y, m_sizeCell.Z];

			m_defaultData.AirPress = 1f;

			// TODO: foreach direction
			m_direction = Base6Directions.IntDirections[0];

			m_defaultData.Velocity = m_direction * -2f;
			m_data.SetAll(m_defaultData);
			m_previous.SetAll(m_defaultData);
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
				MoveAir();
				CalculateDrag(out drag);
			}

			// TODO: report
			// TODO: cleanup

			Update.UpdateManager.Register(1, DebugDraw_Velocity, m_grid);
		}

		/// <summary>
		/// Salt air at the leading edges.
		/// </summary>
		private void SaltAir()
		{
			foreach (Vector3I hitCell in m_grid.FirstBlocks(m_direction))
			{
				Vector3I prevIndex = hitCell - m_direction - m_minCell;
				CellData cellData;
				GetValue(m_data, ref prevIndex, out cellData);
				cellData.AirPress += 0.001f;
				SetValue(m_data, ref prevIndex, ref cellData);
				//m_logger.traceLog("Salted air at " + (hitCell - m_direction) + ", " + cellData);
			}
		}

		/// <summary>
		/// Move air by m_cellVelocity.
		/// </summary>
		private void MoveAir()
		{
			Globals.Swap(ref m_data, ref m_previous);
			float gridSize = m_grid.GridSize;

			Vector3I index;
			for (index.X = 0; index.X < m_sizeCell.X; index.X++)
				for (index.Y = 0; index.Y < m_sizeCell.Y; index.Y++)
					for (index.Z = 0; index.Z < m_sizeCell.Z; index.Z++)
					{
						Vector3I currentCell = index + m_minCell;
						if (m_grid.CubeExists(currentCell))
						{
							m_logger.traceLog("Block exists at " + currentCell);
							continue;
						}

						CellData currentData;
						GetValue(m_previous, ref index, out currentData);
						if (currentData.AirPress == 1f)
						{
							//m_logger.traceLog("cell: " + currentCell + ", early return, " + currentData);
							continue;
						}

						Vector3 sourceCellAvg;

						while (true)
						{
							if (currentData.Velocity.AbsMax() < 0.1f)
							{
								m_logger.traceLog("Air is stopped at " + currentCell + ", " + currentData);
								goto SetValue;
							}

							sourceCellAvg = currentCell + currentData.Velocity;

							// check for blocks between here and source
							Vector3 sourceLocalAvg; Vector3.Multiply(ref sourceCellAvg, gridSize, out sourceLocalAvg);
							LineD line = new LineD(m_grid.GridIntegerToWorld(currentCell), Vector3D.Transform(sourceLocalAvg, m_grid.WorldMatrix));
							MyCubeGridHitInfo hitInfo = new MyCubeGridHitInfo();
							if (((MyCubeGrid)m_grid).GetIntersectionWithLine(ref line, ref hitInfo, IntersectionFlags.DIRECT_TRIANGLES))
							{
								Vector3 norm = hitInfo.Triangle.NormalInObjectSpace;
								m_logger.debugLog("Air intersects block, cell: " + currentCell + ", velocity: " + currentData.Velocity + ", block position: " + hitInfo.Position + ", normal: " + norm);

								float projectionLength; Vector3.Dot(ref currentData.Velocity, ref norm, out projectionLength);
								m_logger.debugLog("Normal is in wrong direction, velocity: " + currentData.Velocity + ", normal: " + norm + ", dot: " + projectionLength, Logger.severity.ERROR, condition: projectionLength >= 0f);
								Vector3 projection; Vector3.Multiply(ref norm, -projectionLength, out projection);
								Vector3.Add(ref currentData.Velocity, ref projection, out currentData.Velocity);
							}
							else
								break;
						}
						
						currentData = new CellData();
						
						// pull air
						Vector3I low;
						low.X = (int)Math.Floor(sourceCellAvg.X);
						low.Y = (int)Math.Floor(sourceCellAvg.Y);
						low.Z = (int)Math.Floor(sourceCellAvg.Z);

						Vector3I high = low + 1;

						Vector3 ratioLow = high - sourceCellAvg;
						Vector3 ratioHigh = sourceCellAvg - low;

						//m_logger.traceLog("ratioLow: " + ratioLow + ", ratioHigh: " + ratioHigh);

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

							CellData sourceData;
							GetValueOrDefault(m_previous, ref sourceIndex, out sourceData);
							//m_logger.traceLog("Air moving to " + currentCell + " from " + sourceCell + ", ratio: " + sourceRatio + "/" + sourceRatioParts + ", pressure: " + sourceData.AirPress + ", velocity: " + sourceData.Velocity);

							currentData.AirPress += sourceData.AirPress * sourceRatio;
							Vector3 transferredVelocity; Vector3.Multiply(ref sourceData.Velocity, sourceRatio, out transferredVelocity);
							Vector3.Add(ref currentData.Velocity, ref transferredVelocity, out currentData.Velocity);
						}

						SetValue:
						m_logger.traceLog("Cell: " + currentCell + ", pressure: " + currentData.AirPress + ", velocity: " + currentData.Velocity);
						SetValue(m_data, ref index, ref currentData);
					}
		}

		/// <summary>
		/// Partially equalize air between each cell and its neighbours.
		/// </summary>
		private void DiffuseAir()
		{
			Globals.Swap(ref m_data, ref m_previous);

			Vector3I index;
			for (index.X = 0; index.X < m_sizeCell.X; index.X++)
				for (index.Y = 0; index.Y < m_sizeCell.Y; index.Y++)
					for (index.Z = 0; index.Z < m_sizeCell.Z; index.Z++)
					{
						Vector3I cell = index + m_minCell;
						CellData currentData;
						GetValue(m_previous, ref index, out currentData);

						foreach (Vector3I offset in Globals.NeighboursOne)
						{
							Vector3I neighbourCell = cell + offset;
							if (m_grid.CubeExists(neighbourCell))
								continue;

							Vector3I neighbourIndex = neighbourCell - m_minCell;
							CellData neighbourData;
							GetValueOrDefault(m_previous, ref neighbourIndex, out neighbourData);

							if (currentData.AirPress == neighbourData.AirPress)
							{
								//m_logger.traceLog("Cell: " + cell + ", neighbour: " + neighbourCell + ", air pressure: " + currentData.AirPress + ", neighbour pressure: " + neighbourData.AirPress);
								continue;
							}

							float airChange = (neighbourData.AirPress - currentData.AirPress) * CellEqualization;
							currentData.AirPress += airChange;

							if (airChange > 0f)
							{
								// this cell gets air from neighbour
								Vector3 inheritedVelocity; Vector3.Multiply(ref neighbourData.Velocity, airChange, out inheritedVelocity);
								Vector3 diffusionVelocity = Vector3.Multiply(offset, airChange);
								Vector3 inheritDiffuse; Vector3.Add(ref inheritedVelocity, ref diffusionVelocity, out inheritDiffuse);
								Vector3.Add(ref currentData.Velocity, ref inheritDiffuse, out currentData.Velocity);

								m_logger.traceLog("Air moved from " + cell + " to " + neighbourCell + " is " + airChange + ", velocity change: " + inheritDiffuse + ", velocity: " + currentData.Velocity);
							}
							else
							{
								// this cell looses air to neighbour
								Vector3 inheritedVelocity; Vector3.Multiply(ref currentData.Velocity, airChange, out inheritedVelocity);
								Vector3 diffusionVelocity = Vector3.Multiply(-offset, airChange);
								Vector3 inheritDiffuse; Vector3.Add(ref inheritedVelocity, ref diffusionVelocity, out inheritDiffuse);
								Vector3.Subtract(ref currentData.Velocity, ref inheritDiffuse, out currentData.Velocity);

								m_logger.traceLog("Air moved from " + cell + " to " + neighbourCell + " is " + airChange + ", velocity change: " + inheritDiffuse + ", velocity: " + currentData.Velocity);
							}
						}

						SetValue(m_data, ref index, ref currentData);
					}
		}

		/// <summary>
		/// Calculate drag effect on the grid, from pressures and velocities.
		/// </summary>
		/// <param name="drag">The drag effect on the grid.</param>
		private void CalculateDrag(out Vector3 drag)
		{
			drag = Vector3.Zero;

			Vector3I index;
			for (index.X = 0; index.X < m_sizeCell.X; index.X++)
				for (index.Y = 0; index.Y < m_sizeCell.Y; index.Y++)
					for (index.Z = 0; index.Z < m_sizeCell.Z; index.Z++)
					{
						CellData currentData;
						GetValue(m_data, ref index, out currentData);

						Vector3 effect; Vector3.Subtract(ref currentData.Velocity, ref m_defaultData.Velocity, out effect);
						m_logger.traceLog("Index: " + index + ", velocity: " + currentData.Velocity + ", effect: " + effect);
						Vector3.Subtract(ref drag, ref effect, out drag);
					}

			float gridSize = m_grid.GridSize;
			Vector3.Multiply(ref drag, gridSize * gridSize, out drag);
		}

		private void GetValue(CellData[,,] array, ref Vector3I index, out CellData value)
		{
			value = array[index.X, index.Y, index.Z];
			if (value.AirPress == 1f)
				value = m_defaultData;
		}

		private void GetValueOrDefault(CellData[,,] array, ref Vector3I index, out CellData value)
		{
			if (!IsGridIndex(ref index))
			{
				value = m_defaultData;
				return;
			}
			value = array[index.X, index.Y, index.Z];
			if (value.AirPress == 1f)
				value = m_defaultData;
		}

		private void SetValue(CellData[,,] array, ref Vector3I index, ref CellData value)
		{
			array[index.X, index.Y, index.Z] = value;
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

		private void DebugDraw_Velocity()
		{
			if (!ThreadTracker.IsGameThread)
				throw new Exception("Not on game thread");

			MatrixD worldMatrix = m_grid.WorldMatrix;
			float radius = m_grid.GridSize * 0.125f;
			MatrixD rotationMatrix = worldMatrix.GetOrientation();
			float lineLength = m_grid.GridSize * 0.25f;
			float defaultSpeed = m_defaultData.Velocity.Length();

			Vector3I index;
			for (index.X = 0; index.X < m_sizeCell.X; index.X++)
				for (index.Y = 0; index.Y < m_sizeCell.Y; index.Y++)
					for (index.Z = 0; index.Z < m_sizeCell.Z; index.Z++)
					{
						CellData currentData;
						GetValue(m_data, ref index, out currentData);

						if (Math.Abs(currentData.AirPress - m_defaultData.AirPress) < 0.1f && currentData.Velocity.Equals(m_defaultData.Velocity, 0.1f))
							continue;

						Vector3D worldPosition = m_grid.GridIntegerToWorld(index + m_minCell);
						worldMatrix.Translation = worldPosition;
						Color airPressColour;
						if (currentData.AirPress > m_defaultData.AirPress)
						{
							int value = 255 - (int)((currentData.AirPress - m_defaultData.AirPress) / m_defaultData.AirPress * 255f);
							airPressColour = new Color(255, value, value);
						}
						else
						{
							int value = 255 - (int)((m_defaultData.AirPress - currentData.AirPress) / m_defaultData.AirPress * 255f);
							airPressColour = new Color(value, value, 255);
						}
						MySimpleObjectDraw.DrawTransparentSphere(ref worldMatrix, radius, ref airPressColour, MySimpleObjectRasterizer.Solid, 4);

						float speed = currentData.Velocity.Length();
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

						Vector3.Multiply(ref currentData.Velocity, lineLength / speed, out currentData.Velocity);
						Vector3D direction = Vector3D.Transform(currentData.Velocity, ref rotationMatrix);
						Vector3D worldEnd; Vector3D.Add(ref worldPosition, ref direction, out worldEnd);
						MySimpleObjectDraw.DrawLine(worldPosition, worldEnd, "WeaponLaser", ref speedColourVector, 0.025f);

						//m_logger.debugLog("Index: " + index + ", " + currentData + ", sphere: " + airPressColour + ", line: " + speedColour);
					}
		}

	}
}
