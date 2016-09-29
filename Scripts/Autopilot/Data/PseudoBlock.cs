using System;
using VRage.Game;
using VRage.Game.Components;
using VRage.Game.ModAPI;
using VRage.ModAPI;
using VRageMath;

namespace Rynchodon.Autopilot.Data
{

	public class BlockNameOrientation
	{

		public readonly string BlockName;
		public readonly Base6Directions.Direction? Forward;
		public readonly Base6Directions.Direction? Upward;

		public BlockNameOrientation(string blockName, Base6Directions.Direction? forward, Base6Directions.Direction? upward)
		{
			this.BlockName = blockName;
			this.Forward = forward;
			this.Upward = upward;
		}

	}

	/// <summary>
	/// A block-like thing used for navigation. It has a LocalMatrix and a Grid.
	/// </summary>
	public class PseudoBlock
	{

		protected readonly Logger m_logger;
		private readonly Func<IMyCubeGrid> m_grid;
		private ulong m_lastCalc_worldMatrix;
		private MatrixD value_worldMatrix;

		public IMyCubeBlock Block { get; protected set; }
		public Matrix LocalMatrix { get; protected set; }
		public MyPhysicsComponentBase Physics { get { return Grid.Physics; } }
		public Vector3D LocalPosition { get { return LocalMatrix.Translation; } }
		public Vector3D WorldPosition { get { return WorldMatrix.Translation; } }

		public IMyCubeGrid Grid { get { return m_grid.Invoke(); } }

		public MatrixD WorldMatrix
		{
			get
			{
				m_logger.debugLog("Grid == null", Logger.severity.FATAL, condition: Grid == null);

				if (m_lastCalc_worldMatrix != Globals.UpdateCount)
				{
					m_lastCalc_worldMatrix = Globals.UpdateCount;
					value_worldMatrix = LocalMatrix * Grid.WorldMatrix;
				}
				return value_worldMatrix;
			}
		}

		/// <summary>
		/// Creates a PsuedoBlock using values from the specified block.
		/// </summary>
		public PseudoBlock(IMyCubeBlock block)
		{
			this.m_logger = new Logger(block);
			this.LocalMatrix = block.LocalMatrix;
			this.m_grid = () => block.CubeGrid;
			this.Block = block;
		}

		/// <summary>
		/// Creates an incomplete PsuedoBlock from a grid.
		/// </summary>
		protected PseudoBlock(Func<IMyCubeGrid> grid)
		{
			this.m_logger = new Logger(() => grid.Invoke().DisplayName);
			this.m_grid = grid;
		}

		/// <summary>
		/// Creates a PseudoBlock from a grid and a local matrix.
		/// </summary>
		public PseudoBlock(Func<IMyCubeGrid> grid, Matrix local)
		{
			this.m_logger = new Logger(() => grid.Invoke().DisplayName);
			this.LocalMatrix = local;
			this.m_grid = grid;
		}

		/// <summary>
		/// Creates a PseudoBlock from a block and an orientation.
		/// </summary>
		/// <param name="block">The block to calculate the local matrix from.</param>
		/// <param name="forward">The direction the block should face towards the target.</param>
		/// <param name="up">A direction perpendicular to forward.</param>
		public PseudoBlock(IMyCubeBlock block, Base6Directions.Direction? forward, Base6Directions.Direction? up)
			: this(block)
		{
			Base6Directions.Direction for2 = forward ?? block.FirstFaceDirection();
			Base6Directions.Direction up2 = up ??
				(for2 == Base6Directions.Direction.Forward ? Base6Directions.Direction.Up : Base6Directions.GetPerpendicular(for2));

			if (for2 == up2 || for2 == Base6Directions.GetFlippedDirection(up2))
			{
				m_logger.debugLog("incompatible directions, for2: " + for2 + ", up2: " + up2);
				up2 = Base6Directions.GetPerpendicular(for2);
			}

			this.LocalMatrix = new Matrix()
			{
				Forward = block.LocalMatrix.GetDirectionVector(for2),
				Up = block.LocalMatrix.GetDirectionVector(up2),
				Right = block.LocalMatrix.GetDirectionVector(Base6Directions.GetCross(for2, up2)),
				M41 = block.LocalMatrix.M41,
				M42 = block.LocalMatrix.M42,
				M43 = block.LocalMatrix.M43,
				M44 = block.LocalMatrix.M44
			};
		}

	}

	public class StandardFlight : PseudoBlock
	{

		public StandardFlight(IMyCubeBlock autopilot, Base6Directions.Direction forward, Base6Directions.Direction up)
			: base(autopilot)
		{
			SetMatrixOrientation(forward, up);
		}

		public void SetMatrixOrientation(Base6Directions.Direction forward, Base6Directions.Direction up)
		{
			if (forward == up || forward == Base6Directions.GetFlippedDirection(up))
			{
				m_logger.alwaysLog("incompatible directions, for2: " + forward + ", up2: " + up, Logger.severity.FATAL);
				throw new ArgumentException("forward is not perpendicular to up");
			}

			Matrix localMatrix = LocalMatrix;
			localMatrix.Forward = Base6Directions.GetVector(forward);
			localMatrix.Up = Base6Directions.GetVector(up);
			localMatrix.Right = Base6Directions.GetVector(Base6Directions.GetCross(forward, up));

			this.LocalMatrix = localMatrix;
		}

	}

	/// <summary>
	/// Combines multiple blocks into a pseudo-block.
	/// </summary>
	/// <typeparam name="T">The type of blocks to combine.</typeparam>
	public class MultiBlock<T> : PseudoBlock where T : MyObjectBuilder_CubeBlock
	{

		public int FunctionalBlocks { get; private set; }

		/// <summary>
		/// Creates a MultiBlock from a single block. However, if the block closes, will rebuild with all the blocks of type T.
		/// </summary>
		/// <param name="block">The block to use as the multi-block.</param>
		public MultiBlock(IMyCubeBlock block)
			: base(block)
		{
			this.FunctionalBlocks = 1;
		}

		/// <summary>
		/// Creates a MultiBlock from all blocks of type T found on the specified grid.
		/// </summary>
		/// <param name="grid">The grid to use blocks from.</param>
		public MultiBlock(Func<IMyCubeGrid> grid)
			: base(grid)
		{
			calculateLocalMatrix();
		}

		private void block_OnClose(IMyEntity obj)
		{
			if (Globals.WorldClosed)
				return;

			try
			{
				m_logger.debugLog("Closed block: " + obj.getBestName());
				calculateLocalMatrix();
			}
			catch (Exception ex)
			{ m_logger.debugLog("Exception: " + ex); }
		}

		private void calculateLocalMatrix()
		{
			if (Grid.MarkedForClose)
				return;

			var blocksOfT = CubeGridCache.GetFor(Grid).GetBlocksOfType(typeof(T));
			Block = null;

			FunctionalBlocks = 0;
			Matrix LocalMatrix = Matrix.Zero;
			Vector3 Translation = Vector3.Zero;
			foreach (IMyCubeBlock block in blocksOfT)
			{
				if (Block == null)
				{
					Block = block;
					LocalMatrix = block.LocalMatrix;
				}

				if (block.IsFunctional)
				{
					FunctionalBlocks++;
					Translation += block.LocalMatrix.Translation;
					block.OnClose -= block_OnClose;
					block.OnClose += block_OnClose;
				}
			}

			if (FunctionalBlocks == 0)
				return;
			LocalMatrix.Translation = Translation / FunctionalBlocks;
			this.LocalMatrix = LocalMatrix;

			return;
		}

	}
}
