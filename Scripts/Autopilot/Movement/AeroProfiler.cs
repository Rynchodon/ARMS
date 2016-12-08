#if DEBUG
//#define TRACE
#endif

using System;
using System.Diagnostics;
using Rynchodon.Threading;
using Rynchodon.Utility;
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
			public bool Process;
			public float CurAirPress, NextAirPress;
			public Vector3 CurVelocity, MoveVelocity, DiffuseVelocity;

			public bool AnyChange { get { return CurAirPress != NextAirPress || CurVelocity != (MoveVelocity + DiffuseVelocity); } }
			public bool LogChange { get { return Math.Abs(CurAirPress - NextAirPress) > 0.01f || !CurVelocity.Equals(MoveVelocity + DiffuseVelocity, 0.01f); } }

			public AeroCell() { }

			public void Copy(AeroCell copy)
			{
				NextAirPress = copy.CurAirPress;
				MoveVelocity = copy.MoveVelocity;
				DiffuseVelocity = copy.DiffuseVelocity;
				ApplyChanges();
			}

			public void ApplyChanges()
			{
				CurAirPress = NextAirPress;
				Vector3.Add(ref MoveVelocity, ref DiffuseVelocity, out CurVelocity);
			}

			public override string ToString()
			{
				const string format = "F";
				return "AirPress: " + CurAirPress.ToString(format) + "/" + NextAirPress.ToString(format) +
					", Velocity: " + CurVelocity.ToString(format) + "/" + MoveVelocity.ToString(format) + "/" + DiffuseVelocity.ToString(format);
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
		private const float CellEqualization = 1f / 8f;
		/// <summary>
		/// Extra cells around the grid where air flow will be considered
		/// </summary>
		private const int GridPadding = 8;

		private static ThreadManager Thread = new ThreadManager(2, true, typeof(AeroProfiler).Name);

		private readonly Logger m_logger;
		private readonly IMyCubeGrid m_grid;

		private AeroCell[,,] m_data;

		private Vector3I m_minCell, m_maxCell, m_sizeCell, m_shipDirection;
		private AeroCell m_defaultData;

		private int m_maxSteps;

		public bool Running { get; private set; }
		public bool Success { get; private set; }

		public AeroProfiler(IMyCubeGrid grid, int maxSteps = int.MaxValue)
		{
			this.m_logger = new Logger(grid);
			this.m_grid = grid;
			this.m_maxSteps = maxSteps;

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
			Profiler.StartProfileBlock();
			m_logger.traceLog("entered");
			m_minCell = m_grid.Min - GridPadding;
			m_maxCell = m_grid.Max + GridPadding;
			m_sizeCell = m_maxCell - m_minCell + 1;
			int blockCount = ((MyCubeGrid)m_grid).BlocksCount;

			m_data = new AeroCell[m_sizeCell.X, m_sizeCell.Y, m_sizeCell.Z];
			Init();

			// TODO: foreach direction
			m_shipDirection = Base6Directions.IntDirections[0];

			m_defaultData = new AeroCell() { NextAirPress = 1f, MoveVelocity = -m_shipDirection };
			m_defaultData.ApplyChanges();

			SetDefault();

			Vector3 drag = Vector3.Zero;
			Vector3 previousDrag = Vector3.Zero;

			int minSteps = m_sizeCell.AbsMax();
			int maxSteps = m_sizeCell.Volume();
			int steps = 0;
			while (true)
			{
				if (steps == m_maxSteps)
					break;

				if (steps > minSteps)
				{
					float diff; Vector3.DistanceSquared(ref drag, ref previousDrag, out diff);
					if (diff < 0.0001f)
					{
						m_logger.debugLog("Drag is stable at " + drag, Logger.severity.DEBUG);
						Logger.DebugNotify("Drag is stable at " + drag, 10000, Logger.severity.DEBUG);
						break;
					}
					if (steps == maxSteps)
					{
						Profiler.EndProfileBlock();
						Logger.DebugNotify("Algorithm is unstable", 10000, Logger.severity.ERROR);
						throw new Exception("Algorithm is unstable");
					}
				}

				m_logger.debugLog("Steps: " + steps + ", current drag: " + drag + ", previous drag: " + previousDrag);

				steps++;
				previousDrag = drag;

				MoveAir();
				ApplyAllChanges();
				DiffuseAir();
				ApplyAllChanges();
				CalculateDrag(out drag);

				if (((MyCubeGrid)m_grid).BlocksCount != blockCount)
				{
					m_logger.debugLog("Block count changed from " + blockCount + " to " + ((MyCubeGrid)m_grid).BlocksCount + ", cannot continue");
					break;
				}
			}

			// TODO: report
			// TODO: cleanup
			m_logger.traceLog("exiting");
			Profiler.EndProfileBlock();
		}

		private void Init()
		{
			Profiler.StartProfileBlock();
			m_logger.traceLog("entered");
			Vector3I top = m_sizeCell - GridPadding - 1;
			Vector3I index;
			for (index.X = 0; index.X < m_sizeCell.X; index.X++)
				for (index.Y = 0; index.Y < m_sizeCell.Y; index.Y++)
					for (index.Z = 0; index.Z < m_sizeCell.Z; index.Z++)
					{
						AeroCell data = new AeroCell();
						m_data[index.X, index.Y, index.Z] = data;
						if (index.X < GridPadding || top.X < index.X || index.Y < GridPadding || top.Y < index.Y || index.Z < GridPadding || top.Z < index.Z)
							data.Process = true;
					}
			m_logger.traceLog("exiting");
			Profiler.EndProfileBlock();
		}

		private void SetDefault()
		{
			Profiler.StartProfileBlock();
			m_logger.traceLog("entered");
			Vector3I index;
			for (index.X = 0; index.X < m_sizeCell.X; index.X++)
				for (index.Y = 0; index.Y < m_sizeCell.Y; index.Y++)
					for (index.Z = 0; index.Z < m_sizeCell.Z; index.Z++)
						m_data[index.X, index.Y, index.Z].Copy(m_defaultData);
			m_logger.traceLog("exiting");
			Profiler.EndProfileBlock();
		}

		/// <summary>
		/// Move air by velocity.
		/// </summary>
		private void MoveAir()
		{
			Profiler.StartProfileBlock();
			m_logger.traceLog("entered");
			float gridSize = m_grid.GridSize;
			double minLineLength = gridSize * 2d;
			MyCubeGridHitInfo hitInfo = null;
			int dirDim = m_shipDirection.Max() == 0 ? 0 : m_maxCell.Dot(ref m_shipDirection);

			Vector3I index;
			for (index.X = 0; index.X < m_sizeCell.X; index.X++)
				for (index.Y = 0; index.Y < m_sizeCell.Y; index.Y++)
					for (index.Z = 0; index.Z < m_sizeCell.Z; index.Z++)
					{
						AeroCell currentData;
						GetValue(ref index, out currentData);
						if (!currentData.Process)
							continue;

						Vector3I currentCell = index + m_minCell;
						Vector3 currentCellF = currentCell;

						Vector3 targetCellAvg = currentCell + currentData.MoveVelocity;
						Vector3.Add(ref currentCellF, ref currentData.MoveVelocity, out targetCellAvg);

						while (true)
						{
							if (currentData.MoveVelocity.AbsMax() < 0.1f)
							{
								m_logger.traceLog("Air is stopped at " + currentCell + ", " + currentData);
								break;
							}

							// check for blocks between here and target
							Vector3 targetLocalAvg; Vector3.Multiply(ref targetCellAvg, gridSize, out targetLocalAvg);
							LineD line = new LineD(m_grid.GridIntegerToWorld(currentCell), Vector3D.Transform(targetLocalAvg, m_grid.WorldMatrix));
							if (line.Length < minLineLength)
							{
								line.Length = minLineLength;
								Vector3D disp; Vector3D.Multiply(ref line.Direction, minLineLength, out disp);
								Vector3D.Add(ref line.From, ref disp, out line.To);
							}
							if (((MyCubeGrid)m_grid).GetIntersectionWithLine(ref line, ref hitInfo, IntersectionFlags.DIRECT_TRIANGLES))
							{
								Vector3 norm = hitInfo.Triangle.NormalInObjectSpace;
								m_logger.debugLog("Air intersects block, cell: " + currentCell + ", velocity: " + currentData.MoveVelocity + ", block position: " + hitInfo.Position + ", normal: " + norm);

								float projectionLength; Vector3.Dot(ref currentData.MoveVelocity, ref norm, out projectionLength);
								Vector3 projection; Vector3.Multiply(ref norm, -projectionLength, out projection);
								Vector3.Add(ref currentData.MoveVelocity, ref projection, out currentData.MoveVelocity);
								targetCellAvg = currentCell + currentData.MoveVelocity;

								m_logger.debugLog("projection: " + projection + ", " + currentData);
								continue;
							}
							else
							{
//								// allow air into non-blocking cells
//								Vector3I closestTargetCell;
//								closestTargetCell.X = (int)Math.Round(targetCellAvg.X);
//								closestTargetCell.Y = (int)Math.Round(targetCellAvg.Y);
//								closestTargetCell.Z = (int)Math.Round(targetCellAvg.Z);
//								if (IsGridCell(ref closestTargetCell))
//								{
//									Vector3I closestTargetIndex = closestTargetCell - m_minCell;
//									AeroCell closestTargetData;
//									GetValue(ref closestTargetIndex, out closestTargetData);
//#if DEBUG
//									if (!closestTargetData.Process)
//									{
//										IMySlimBlock slim = m_grid.GetCubeBlock(closestTargetCell);
//										if (slim != null)
//											m_logger.debugLog("Not obstructing: " + slim.nameWithId());
//									}
//#endif
//									closestTargetData.Process = true;
//								}
								m_logger.traceLog("Cell: " + currentCell + ", " + currentData);
								break;
							}
						}

						PushAir(ref currentCell, currentData, ref targetCellAvg);

						int dot; Vector3I.Dot(ref index, ref m_shipDirection, out dot);
						if (dot == dirDim)
							currentData.NextAirPress += m_defaultData.CurAirPress;

						m_logger.traceLog("Cell: " + currentCell + ", " + currentData, condition: currentData.LogChange);
					}
			m_logger.traceLog("exiting");
			Profiler.EndProfileBlock();
		}

		private void PushAir(ref Vector3I currentCell, AeroCell currentData, ref Vector3 targetCellAvg)
		{
			Vector3I low;
			low.X = (int)Math.Floor(targetCellAvg.X);
			low.Y = (int)Math.Floor(targetCellAvg.Y);
			low.Z = (int)Math.Floor(targetCellAvg.Z);

			Vector3I high = low + 1;

			Vector3 ratioLow = high - targetCellAvg;
			Vector3 ratioHigh = targetCellAvg - low;

			foreach (Vector3I targetLowHigh in LowHigh)
			{
				Vector3I targetCell = low + targetLowHigh;
				if (m_grid.CubeExists(targetCell))
					continue;

				Vector3I targetIndex = targetCell - m_minCell;

				Vector3 targetRatioParts;
				float targetRatio;

				if (targetLowHigh.X == 0)
					targetRatioParts.X = ratioLow.X;
				else
					targetRatioParts.X = ratioHigh.X;

				if (targetLowHigh.Y == 0)
					targetRatioParts.Y = ratioLow.Y;
				else
					targetRatioParts.Y = ratioHigh.Y;

				if (targetLowHigh.Z == 0)
					targetRatioParts.Z = ratioLow.Z;
				else
					targetRatioParts.Z = ratioHigh.Z;

				targetRatio = targetRatioParts.Volume;

				if (targetRatio < 0.01f)
					continue;

				AeroCell targetData;
				GetValueOrDefault(ref targetIndex, out targetData);

				float airMove = currentData.CurAirPress * targetRatio;
				currentData.NextAirPress -= airMove;
				targetData.NextAirPress += airMove;

				m_logger.traceLog("Current: " + currentCell + ", other: " + targetCell + ", target ratio: " + targetRatio + ", air moving: " + airMove);
			}
		}

		/// <summary>
		/// Partially equalize air between each cell and its neighbours.
		/// </summary>
		private void DiffuseAir()
		{
			Profiler.StartProfileBlock();
			m_logger.traceLog("entered");
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
						currentData.DiffuseVelocity = Vector3.Zero;

						foreach (Vector3I offset in Globals.NeighboursOne)
						{
							Vector3I neighbourCell = cell + offset;
							if (m_grid.CubeExists(neighbourCell))
								continue;

							Vector3I neighbourIndex = neighbourCell - m_minCell;
							AeroCell neighbourData;
							GetValueOrDefault(ref neighbourIndex, out neighbourData);
							neighbourData.Process = true;

							float difference = neighbourData.CurAirPress - currentData.CurAirPress;
							if (difference < 0.01f && -0.01f < difference)
								continue;
							float airChange = difference * CellEqualization;
							currentData.NextAirPress += airChange;
							Vector3 diffusionVelocity = Vector3.Multiply(offset, -airChange);
							Vector3.Add(ref currentData.DiffuseVelocity, ref diffusionVelocity, out currentData.DiffuseVelocity);
						}
						
						m_logger.traceLog("Cell: " + cell + ", " + currentData, condition: currentData.LogChange);
					}
			m_logger.traceLog("exiting");
			Profiler.EndProfileBlock();
		}

		/// <summary>
		/// Calculate drag effect on the grid, from pressures and velocities.
		/// </summary>
		/// <param name="drag">The drag effect on the grid.</param>
		private void CalculateDrag(out Vector3 drag)
		{
			Profiler.StartProfileBlock();
			m_logger.traceLog("entered");

			drag = Vector3.Zero;
			float airPressure = 0;

			Vector3I index;
			for (index.X = 0; index.X < m_sizeCell.X; index.X++)
				for (index.Y = 0; index.Y < m_sizeCell.Y; index.Y++)
					for (index.Z = 0; index.Z < m_sizeCell.Z; index.Z++)
					{
						AeroCell currentData;
						GetValue(ref index, out currentData);

						airPressure += currentData.CurAirPress;
						Vector3 effect; Vector3.Subtract(ref m_defaultData.CurVelocity, ref currentData.CurVelocity, out effect);
						Vector3.Multiply(ref effect, currentData.CurAirPress, out effect);
						Vector3.Add(ref drag, ref effect, out drag);

						if (!m_defaultData.CurVelocity.Equals(currentData.CurVelocity, 0.01f))
							m_logger.debugLog("Cell: " + (index + m_minCell) + ", " + currentData);
					}

			m_logger.debugLog("Air pressure: " + airPressure + ", drag: " + drag);

			float gridSize = m_grid.GridSize;
			Vector3.Multiply(ref drag, gridSize * gridSize, out drag);
			m_logger.traceLog("exiting");
			Profiler.EndProfileBlock();
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

		#region Debugging

		[Conditional("DEBUG")]
		public void DebugSymmetry()
		{
			const float min = 0.001f;

			Vector3I index;
			for (index.X = 0; index.X < m_sizeCell.X; index.X++)
				for (index.Y = 0; index.Y < m_sizeCell.Y; index.Y++)
					for (index.Z = 0; index.Z < m_sizeCell.Z; index.Z++)
					{
						AeroCell currentData;
						GetValue(ref index, out currentData);

						if (Math.Abs(currentData.CurVelocity.X) > min)
						{
							Vector3I oppositeIndex;
							oppositeIndex.X = -index.X - m_minCell.X - m_minCell.X;
							oppositeIndex.Y = index.Y;
							oppositeIndex.Z = index.Z;

							if (oppositeIndex == index)
								m_logger.debugLog("X value: " + currentData.CurVelocity.X + ", same index: " + index + ", " + currentData);
							else if (!IsGridIndex(ref oppositeIndex))
								m_logger.debugLog("Opposite index is out of range, cell: " + (index + m_minCell) + ", " + currentData);
							else
							{
								AeroCell oppositeData;
								GetValue(ref oppositeIndex, out oppositeData);

								float sum = currentData.CurVelocity.X * currentData.CurAirPress + oppositeData.CurVelocity.X * oppositeData.CurAirPress;
								if (Math.Abs(sum) > min)
									m_logger.debugLog("X sum: " + sum + ", cell: " + (index + m_minCell) + ", " + currentData + ", opposite: " + (oppositeIndex + m_minCell) + ", " + oppositeData);
							}
						}

						if (Math.Abs(currentData.CurVelocity.Y) > min)
						{
							Vector3I oppositeIndex;
							oppositeIndex.X = index.X;
							oppositeIndex.Y = -index.Y - m_minCell.Y - m_minCell.Y;
							oppositeIndex.Z = index.Z;

							if (oppositeIndex == index)
								m_logger.debugLog("Y value: " + currentData.CurVelocity.Y + ", same index: " + index + ", " + currentData);
							else if (!IsGridIndex(ref oppositeIndex))
								m_logger.debugLog("Opposite index is out of range, cell: " + (index + m_minCell) + ", " + currentData);
							else
							{
								AeroCell oppositeData;
								GetValue(ref oppositeIndex, out oppositeData);

								float sum = currentData.CurVelocity.Y * currentData.CurAirPress + oppositeData.CurVelocity.Y * oppositeData.CurAirPress;
								if (Math.Abs(sum) > min)
									m_logger.debugLog("Y sum: " + sum + ", cell: " + (index + m_minCell) + ", " + currentData + ", opposite: " + (oppositeIndex + m_minCell) + ", " + oppositeData);
							}
						}
					}
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

						if (Math.Abs(currentData.CurAirPress - m_defaultData.CurAirPress) < 0.5f && currentData.CurVelocity.Equals(m_defaultData.CurVelocity, 0.5f))
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

						Vector3 velocity;  Vector3.Multiply(ref currentData.CurVelocity, lineLength / speed, out velocity);
						Vector3D direction = Vector3D.Transform(velocity, ref rotationMatrix);
						Vector3D worldEnd; Vector3D.Add(ref worldPosition, ref direction, out worldEnd);
						MySimpleObjectDraw.DrawLine(worldPosition, worldEnd, "WeaponLaser", ref speedColourVector, 0.05f);

						//m_logger.debugLog("Cell: " + (index + m_minCell) + ", " + currentData + ", sphere: " + airPressColour + ", line: " + speedColour);
					}
		}

		#endregion

	}
}
