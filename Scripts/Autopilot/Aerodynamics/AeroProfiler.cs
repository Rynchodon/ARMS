#if DEBUG
//#define TRACE
#endif

using System;
using System.Collections.Generic;
using System.Diagnostics;
using Rynchodon.Threading;
using Rynchodon.Update;
using Rynchodon.Utility;
using Sandbox.Game.Entities;
using VRage.Game;
using VRage.Game.Components;
using VRage.Game.ModAPI;
using VRage.Game.Models;
using VRageMath;

namespace Rynchodon.Autopilot.Aerodynamics
{
	/// <summary>
	/// Performs the calculations of air flow around a grid. Disposed of upon completion unless drawing.
	/// </summary>
	class AeroProfiler
	{
		private class AeroCell
		{
			public bool Process, BlockTest;
			public float CurAirPress, NextAirPress;
			public Vector3 MoveVelocity, DiffuseVelocity;

			public Vector3 CurVelocity { get { return MoveVelocity + DiffuseVelocity; } }
			public bool AnyChange { get { return CurAirPress != NextAirPress || CurVelocity != (MoveVelocity + DiffuseVelocity); } }
			public bool LogChange { get { return Math.Abs(CurAirPress - NextAirPress) > 0.01f || !CurVelocity.Equals(MoveVelocity + DiffuseVelocity, 0.01f); } }

			public AeroCell() { }

			public AeroCell(float airPress, Vector3 moveVelocity)
			{
				this.NextAirPress = airPress;
				this.MoveVelocity = moveVelocity;
				this.DiffuseVelocity = Vector3.Zero;
				BlockTest = false;
				ApplyChanges();
			}

			public void Copy(AeroCell copy)
			{
				this.NextAirPress = copy.CurAirPress;
				this.MoveVelocity = copy.MoveVelocity;
				this.DiffuseVelocity = copy.DiffuseVelocity;
				BlockTest = false;
				ApplyChanges();
			}

			public void ApplyChanges()
			{
				CurAirPress = NextAirPress;
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
		private const int GridPadding = 2;

		private static ThreadManager Thread = new ThreadManager((byte)(Environment.ProcessorCount / 2), true, typeof(AeroProfiler).Name);

		private readonly MyCubeGrid m_grid;

		public readonly Base6Directions.Direction? m_drawDirection;

		private AeroCell[,,] m_data;
		private List<Vector3I> m_rayCastCells = new List<Vector3I>();

		private Vector3I m_minCell, m_maxCell, m_sizeCell, m_shipDirection;
		private AeroCell m_defaultData;

		public bool Running { get; private set; }
		public bool Success { get; private set; }
		public Vector3[] DragCoefficient { get; private set; }

		private Logable Log { get { return new Logable(m_grid); } }

		public AeroProfiler(IMyCubeGrid grid, Base6Directions.Direction? drawDirection = null)
		{
			this.m_grid = (MyCubeGrid)grid;
			this.m_drawDirection = drawDirection;

			Running = true;
			Success = false;
			Thread.EnqueueAction(Run);
		}

		public void DisableDraw()
		{
			Unregister(null);
		}

		private void Unregister(object obj)
		{
			m_grid.OnBlockAdded -= Unregister;
			m_grid.OnBlockRemoved -= Unregister;
			m_grid.OnClose -= Unregister;
			UpdateManager.Unregister(1, DrawPressureVelocity);
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
				if (!m_drawDirection.HasValue)
					m_data = null;
			}
		}

		private void Calculate()
		{
			Profiler.StartProfileBlock();
			Log.TraceLog("entered");
			m_minCell = m_grid.Min - GridPadding;
			m_maxCell = m_grid.Max + GridPadding;
			m_sizeCell = m_maxCell - m_minCell + 1;
			int blockCount = m_grid.BlocksCount;

			m_data = new AeroCell[m_sizeCell.X, m_sizeCell.Y, m_sizeCell.Z];
			DragCoefficient = new Vector3[6];
			Init();
			if (m_drawDirection.HasValue)
			{
				UpdateManager.Register(1, DrawPressureVelocity);
				m_grid.OnBlockAdded += Unregister;
				m_grid.OnBlockRemoved += Unregister;
				m_grid.OnClose += Unregister;
			}

			for (int dirIndex = 0; dirIndex < 6; ++dirIndex)
			{
				if (m_drawDirection.HasValue)
					m_shipDirection = Base6Directions.GetIntVector(m_drawDirection.Value);
				else
					m_shipDirection = Base6Directions.IntDirections[dirIndex];

				m_defaultData = new AeroCell(1f, -m_shipDirection);
				m_defaultData.ApplyChanges();

				SetDefault();

				Vector3[] dragValues = new Vector3[3];

				int minSteps = m_sizeCell.AbsMax();
				int maxSteps = m_sizeCell.Volume();
				int steps = 0;
				while (true)
				{
					if (steps > minSteps)
					{
						float changeThreshold = Math.Max(dragValues[0].AbsMax(), 1f) * 0.01f;
						if (Vector3.RectangularDistance(ref dragValues[0], ref dragValues[1]) < changeThreshold || Vector3.RectangularDistance(ref dragValues[0], ref dragValues[2]) < changeThreshold)
						{
							dragValues[0] = (dragValues[0] + dragValues[1]) * 0.5f;
							Log.DebugLog("Drag is stable at " + dragValues[0] +", direction: "+ Base6Directions.GetDirection(m_shipDirection), Logger.severity.DEBUG);
							break;
						}
						if (steps == maxSteps)
						{
							Profiler.EndProfileBlock();
							Logger.DebugNotify("Algorithm is unstable", 10000, Logger.severity.ERROR);
							throw new Exception("Algorithm is unstable");
						}
					}

					Log.DebugLog("Steps: " + steps + ", current drag: " + dragValues[0] + ", previous drag: " + dragValues[1] + " and " + dragValues[2]);

					steps++;
					dragValues[2] = dragValues[1];
					dragValues[1] = dragValues[0];

					MoveAir();
					ApplyAllChanges();
					DiffuseAir();
					ApplyAllChanges();
					CalculateDrag(out dragValues[0]);

					if (m_grid.BlocksCount != blockCount)
					{
						Log.DebugLog("Block count changed from " + blockCount + " to " + m_grid.BlocksCount + ", cannot continue", Logger.severity.INFO);
						break;
					}
				}

				DragCoefficient[(int)Base6Directions.GetDirection(m_shipDirection)] = dragValues[0];
				if (m_drawDirection.HasValue)
					break;
			}

			Log.TraceLog("exiting");
			Profiler.EndProfileBlock();
		}

		private void Init()
		{
			Profiler.StartProfileBlock();
			Log.TraceLog("entered");
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
			Log.TraceLog("exiting");
			Profiler.EndProfileBlock();
		}

		private void SetDefault()
		{
			Profiler.StartProfileBlock();
			Log.TraceLog("entered");
			Vector3I index;
			for (index.X = 0; index.X < m_sizeCell.X; index.X++)
				for (index.Y = 0; index.Y < m_sizeCell.Y; index.Y++)
					for (index.Z = 0; index.Z < m_sizeCell.Z; index.Z++)
						m_data[index.X, index.Y, index.Z].Copy(m_defaultData);
			Log.TraceLog("exiting");
			Profiler.EndProfileBlock();
		}

		/// <summary>
		/// Move air by velocity.
		/// </summary>
		private void MoveAir()
		{
			Profiler.StartProfileBlock();
			Log.TraceLog("entered");
			int dirDim = m_shipDirection.Max() == 0 ? 0 : m_sizeCell.Dot(ref m_shipDirection) - 1;

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
						//Vector3 currentCellF = currentCell;

						Vector3 targetCellAvg = currentCell + currentData.CurVelocity;
						//Vector3.Add(ref currentCellF, ref currentData.MoveVelocity, out targetCellAvg);

						if (!currentData.BlockTest)
							BlockTest(currentData, ref currentCell, ref targetCellAvg);

						PushAir(ref currentCell, currentData, ref targetCellAvg);

						int dot; Vector3I.Dot(ref index, ref m_shipDirection, out dot);
						if (dot == dirDim)
							currentData.NextAirPress += m_defaultData.CurAirPress;

						Log.TraceLog("Cell: " + currentCell + ", " + currentData, condition: currentData.LogChange);
					}
			Log.TraceLog("exiting");
			Profiler.EndProfileBlock();
		}

		private void BlockTest(AeroCell currentData, ref  Vector3I currentCell, ref Vector3 targetCellAvg)
		{
			float gridSize = m_grid.GridSize;
			double minLineLength = gridSize * 1.4d;

			int tries;
			for (tries = 0; tries < 100; ++tries)
			{
				if (currentData.CurVelocity.AbsMax() < 0.1f)
				{
					Log.TraceLog("Air is stopped at " + currentCell + ", " + currentData);
					break;
				}

				// check for blocks between here and target
				Vector3 targetLocalAvg; Vector3.Multiply(ref targetCellAvg, gridSize, out targetLocalAvg);
				LineD line = new LineD(currentCell * (double)gridSize, targetLocalAvg);
				if (line.Length < minLineLength)
				{
					line.Length = minLineLength;
					Vector3D disp; Vector3D.Multiply(ref line.Direction, minLineLength, out disp);
					Vector3D.Add(ref line.From, ref disp, out line.To);
				}

				MyIntersectionResultLineTriangleEx? result;
				if (m_grid.Intersects(ref line, m_rayCastCells, out result, IntersectionFlags.ALL_TRIANGLES))
				{
					Vector3 normal = result.Value.NormalInObjectSpace;
					if (result.Value.Entity != m_grid)
					{
						Matrix localMatrix = result.Value.Entity.LocalMatrix.GetOrientation();
						Vector3.Transform(ref normal, ref localMatrix, out normal);
					}
					BlockTest_ApplyNormal(currentData, ref currentCell, ref normal, ref targetCellAvg);
					continue;
				}
				else
				{
					Vector3I bestTarget;
					bestTarget.X = (int)Math.Round(targetCellAvg.X);
					bestTarget.Y = (int)Math.Round(targetCellAvg.Y);
					bestTarget.Z = (int)Math.Round(targetCellAvg.Z);
					if (m_grid.CubeExists(bestTarget))
					{
						Vector3 hitNormal;
						if (BlockTest_Multi(ref line, out hitNormal))
						{
							BlockTest_ApplyNormal(currentData, ref currentCell, ref hitNormal, ref targetCellAvg);
							continue;
						}
					}
					break;
				}
			}
			Log.DebugLog("Too many tries", Logger.severity.WARNING, condition: tries >= 100);
			currentData.BlockTest = true;
		}

		private void BlockTest_ApplyNormal(AeroCell currentData, ref Vector3I currentCell, ref Vector3 normal, ref Vector3 targetCellAvg)
		{
			Vector3 currentVelocity = currentData.CurVelocity;

			float projectionLength; Vector3.Dot(ref currentVelocity, ref normal, out projectionLength);
			Vector3 projection; Vector3.Multiply(ref normal, -projectionLength, out projection);

			Log.TraceLog("Air intersects block, cell: " + currentCell + ", velocity: " + currentVelocity + ", normal: " + normal + ", dot: " + projectionLength);
			Log.TraceLog("projection: " + projection + ", " + currentData);

			Vector3.Add(ref currentVelocity, ref projection, out currentVelocity);
			Vector3.Subtract(ref currentVelocity, ref currentData.DiffuseVelocity, out currentData.MoveVelocity);

			targetCellAvg = currentCell + currentData.CurVelocity;
		}

		/// <summary>
		/// Get normal of multiple lines and, if they have reasonably similar values, return true.
		/// </summary>
		private bool BlockTest_Multi(ref LineD line, out Vector3 hitNormal)
		{
			float gridSize = m_grid.GridSize;
			MyIntersectionResultLineTriangleEx? result;

			double minLineLength = gridSize * 10d;
			Vector3D perp1 = Vector3D.CalculatePerpendicularVector(line.Direction);
			Vector3D perp2; Vector3D.Cross(ref line.Direction, ref perp1, out perp2);

			double movePerp = gridSize * 0.444d;

			LineD extraLine;
			extraLine.Direction = line.Direction;
			extraLine.Length = 10d;

			Vector3D disp; Vector3D.Multiply(ref line.Direction, extraLine.Length, out disp);
			Vector3D.Add(ref line.From, ref disp, out line.To);

			Vector3D.Multiply(ref perp1, movePerp, out perp1);
			Vector3D.Multiply(ref perp2, movePerp, out perp2);

			hitNormal = Vector3.Zero;

			for (double flip1 = -1d; flip1 < 2d; flip1 += 2d)
				for (double flip2 = -1d; flip2 < 2d; flip2 += 2d)
				{
					Vector3D off1; Vector3D.Multiply(ref perp1, flip1, out off1);
					Vector3D off2; Vector3D.Multiply(ref perp2, flip2, out off2);
					Vector3D off; Vector3D.Add(ref off1, ref off2, out off);

					Vector3D.Add(ref line.From, ref off, out extraLine.From);
					Vector3D.Add(ref line.To, ref off, out extraLine.To);

					if (!m_grid.Intersects(ref extraLine, m_rayCastCells, out result, IntersectionFlags.ALL_TRIANGLES))
					{
						Log.TraceLog("No hit: " + extraLine.From + " to " + extraLine.To);
						return false;
					}

					Vector3 normal = result.Value.NormalInObjectSpace;
					if (result.Value.Entity != m_grid)
					{
						Matrix localMatrix = result.Value.Entity.LocalMatrix.GetOrientation();
						Vector3.Transform(ref normal, ref localMatrix, out normal);
					}

					if (hitNormal == Vector3.Zero)
						hitNormal = normal;
					else if (!hitNormal.Equals(normal, 0.01f) && !hitNormal.Equals(-normal, 0.01f))
					{
						Log.TraceLog("Normal discrepancy between " + hitNormal + " and " + normal);
						return false;
					}
				}

			return true;
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

				float airMove = Math.Min(currentData.CurAirPress, 2f) * targetRatio;
				currentData.NextAirPress -= airMove;
				targetData.NextAirPress += airMove;

				Log.TraceLog("Current: " + currentCell + ", other: " + targetCell + ", target ratio: " + targetRatio + ", air moving: " + airMove);
			}
		}

		/// <summary>
		/// Partially equalize air between each cell and its neighbours.
		/// </summary>
		private void DiffuseAir()
		{
			Profiler.StartProfileBlock();
			Log.TraceLog("entered");
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
						
						Log.TraceLog("Cell: " + cell + ", " + currentData, condition: currentData.LogChange);
					}
			Log.TraceLog("exiting");
			Profiler.EndProfileBlock();
		}

		/// <summary>
		/// Calculate drag effect on the grid, from pressures and velocities.
		/// </summary>
		/// <param name="drag">The drag effect on the grid.</param>
		private void CalculateDrag(out Vector3 drag)
		{
			Profiler.StartProfileBlock();
			Log.TraceLog("entered");

			drag = Vector3.Zero;

			Vector3I index;
			for (index.X = 0; index.X < m_sizeCell.X; index.X++)
				for (index.Y = 0; index.Y < m_sizeCell.Y; index.Y++)
					for (index.Z = 0; index.Z < m_sizeCell.Z; index.Z++)
					{
						AeroCell currentData;
						GetValue(ref index, out currentData);

						Vector3 effect = m_defaultData.CurVelocity - currentData.CurVelocity;
						Vector3.Add(ref drag, ref effect, out drag);
					}

			float gridSize = m_grid.GridSize;
			Vector3.Multiply(ref drag, gridSize * gridSize, out drag);
			Log.TraceLog("exiting");
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
								Log.DebugLog("X value: " + currentData.CurVelocity.X + ", same index: " + index + ", " + currentData);
							else if (!IsGridIndex(ref oppositeIndex))
								Log.DebugLog("Opposite index is out of range, cell: " + (index + m_minCell) + ", " + currentData);
							else
							{
								AeroCell oppositeData;
								GetValue(ref oppositeIndex, out oppositeData);

								float sum = currentData.CurVelocity.X * currentData.CurAirPress + oppositeData.CurVelocity.X * oppositeData.CurAirPress;
								if (Math.Abs(sum) > min)
									Log.DebugLog("X sum: " + sum + ", cell: " + (index + m_minCell) + ", " + currentData + ", opposite: " + (oppositeIndex + m_minCell) + ", " + oppositeData);
							}
						}

						if (Math.Abs(currentData.CurVelocity.Y) > min)
						{
							Vector3I oppositeIndex;
							oppositeIndex.X = index.X;
							oppositeIndex.Y = -index.Y - m_minCell.Y - m_minCell.Y;
							oppositeIndex.Z = index.Z;

							if (oppositeIndex == index)
								Log.DebugLog("Y value: " + currentData.CurVelocity.Y + ", same index: " + index + ", " + currentData);
							else if (!IsGridIndex(ref oppositeIndex))
								Log.DebugLog("Opposite index is out of range, cell: " + (index + m_minCell) + ", " + currentData);
							else
							{
								AeroCell oppositeData;
								GetValue(ref oppositeIndex, out oppositeData);

								float sum = currentData.CurVelocity.Y * currentData.CurAirPress + oppositeData.CurVelocity.Y * oppositeData.CurAirPress;
								if (Math.Abs(sum) > min)
									Log.DebugLog("Y sum: " + sum + ", cell: " + (index + m_minCell) + ", " + currentData + ", opposite: " + (oppositeIndex + m_minCell) + ", " + oppositeData);
							}
						}
					}
		}

		#endregion

		public void DrawPressureVelocity()
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
							int value = 255 - (int)((currentData.CurAirPress - m_defaultData.CurAirPress) / m_defaultData.CurAirPress * 63.75f);
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
							int value = 255 - (int)((speed - defaultSpeed) / defaultSpeed * 63.75f);
							speedColour = new Color(255, value, value);
						}
						else
						{
							int value = 255 - (int)((defaultSpeed - speed) / defaultSpeed * 255f);
							speedColour = new Color(value, value, 255);
						}
						Vector4 speedColourVector = speedColour.ToVector4();

						Vector3 velocity = Vector3.Multiply(currentData.CurVelocity, lineLength / speed);
						Vector3D direction = Vector3D.Transform(velocity, ref rotationMatrix);
						Vector3D worldEnd; Vector3D.Add(ref worldPosition, ref direction, out worldEnd);
						MySimpleObjectDraw.DrawLine(worldPosition, worldEnd, Globals.WeaponLaser, ref speedColourVector, 0.05f);

						//Log.DebugLog("Cell: " + (index + m_minCell) + ", " + currentData + ", sphere: " + airPressColour + ", line: " + speedColour);
					}
		}

	}
}
