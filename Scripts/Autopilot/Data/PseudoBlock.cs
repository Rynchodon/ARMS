using System;

using Sandbox.Common.ObjectBuilders;
using Sandbox.ModAPI;
using VRage.Components;
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
		public readonly IMyCubeGrid Grid;

		private readonly Logger m_logger;
		private ulong m_lastCalc_worldMatrix;
		private MatrixD value_worldMatrix;

		public IMyCubeBlock Block { get; protected set; }
		public Matrix LocalMatrix { get; protected set; }
		public MyPhysicsComponentBase Physics { get { return Grid.Physics; } }
		public Vector3D LocalPosition { get { return LocalMatrix.Translation; } }
		public Vector3D WorldPosition { get { return WorldMatrix.Translation; } }

		public MatrixD WorldMatrix
		{
			get
			{
				m_logger.debugLog(Grid == null, "Grid == null", "get_WorldMatrix()", Logger.severity.FATAL);

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
			this.m_logger = new Logger(GetType().Name, block);
			this.LocalMatrix = block.LocalMatrix;
			this.Grid = block.CubeGrid;
			this.Block = block;
		}

		/// <summary>
		/// Creates a PseudoBlock from a grid and a local matrix.
		/// </summary>
		public PseudoBlock(IMyCubeGrid grid, Matrix local)
		{
			this.m_logger = new Logger(GetType().Name, () => grid.DisplayName);
			this.LocalMatrix = local;
			this.Grid = grid;
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
			Base6Directions.Direction for2 = forward ?? block.GetFaceDirection()[0];
			Base6Directions.Direction up2 = up ?? Base6Directions.GetPerpendicular(for2);

			if (for2 == up2 || for2 == Base6Directions.GetFlippedDirection(up2))
			{
				m_logger.debugLog("incompatible directions, for2: " + for2 + ", up2: " + up2, "PseudoBlock()");
				up2 = Base6Directions.GetPerpendicular(for2);
			}

			Matrix constructed = Matrix.Zero;

			constructed.Forward = block.LocalMatrix.GetDirectionVector(for2);
			constructed.Up = block.LocalMatrix.GetDirectionVector(up2);
			constructed.Right = block.LocalMatrix.GetDirectionVector(Base6Directions.GetCross(for2, up2));
			constructed.M41 = block.LocalMatrix.M41;
			constructed.M42 = block.LocalMatrix.M42;
			constructed.M43 = block.LocalMatrix.M43;
			constructed.M44 = block.LocalMatrix.M44;

			this.LocalMatrix = constructed;
		}

	}

	/// <summary>
	/// Combines multiple blocks into a pseudo-block.
	/// </summary>
	/// <typeparam name="T">The type of blocks to combine.</typeparam>
	public class MultiBlock<T> : PseudoBlock where T : MyObjectBuilder_CubeBlock
	{

		private readonly Logger m_logger;

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
		public MultiBlock(IMyCubeGrid grid)
			: base(grid, Matrix.Zero)
		{
			this.m_logger = new Logger(GetType().Name, () => grid.DisplayName);
			calculateLocalMatrix();
		}

		private void block_OnClose(IMyEntity obj)
		{
			try
			{
				m_logger.debugLog("Closed block: " + obj.getBestName(), "block_OnClose()");
				calculateLocalMatrix();
			}
			catch (Exception ex)
			{ m_logger.debugLog("Exception: " + ex, "block_OnClose()"); }
		}

		private void calculateLocalMatrix()
		{
			if (Grid.MarkedForClose)
				return;

			var blocksOfT = CubeGridCache.GetFor(Grid).GetBlocksOfType(typeof(T));

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
