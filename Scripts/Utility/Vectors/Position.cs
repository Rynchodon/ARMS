using System; // partial

using VRage.Game.ModAPI;
using VRageMath;

namespace Rynchodon.Utility.Vectors
{

	/// <summary>
	/// Cell position in grid.
	/// </summary>
	public struct PositionCell
	{

		public static implicit operator Vector3I(PositionCell position)
		{
			return position.vector;
		}

		public static implicit operator PositionCell(Vector3I vector)
		{
			return new PositionCell() { vector = vector };
		}

		public Vector3I vector;

		public PositionGrid ToGrid(IMyCubeGrid grid)
		{
			return new PositionGrid() { vector = vector * grid.GridSize };
		}

		public PositionWorld ToWorld(IMyCubeGrid grid)
		{
			return grid.GridIntegerToWorld(vector);
		}

		public override string ToString()
		{
			return vector.ToString();
		}

	}

	/// <summary>
	/// Position in block space.
	/// </summary>
	public struct PositionBlock
	{

		public static implicit operator Vector3(PositionBlock position)
		{
			return position.vector;
		}

		public static implicit operator PositionBlock(Vector3 vector)
		{
			return new PositionBlock() { vector = vector };
		}

		public Vector3 vector;

		public PositionGrid ToGrid(IMyCubeBlock block)
		{
			return Vector3.Transform(vector, block.LocalMatrix);
		}

		public PositionWorld ToWorld(IMyCubeBlock block)
		{
			return Vector3.Transform(vector, block.WorldMatrix);
		}

		public override string ToString()
		{
			return vector.ToString();
		}
	
	}

	/// <summary>
	/// Position in grid space.
	/// </summary>
	public struct PositionGrid
	{

		public static implicit operator Vector3(PositionGrid position)
		{
			return position.vector;
		}

		public static implicit operator PositionGrid(Vector3 vector)
		{
			return new PositionGrid() { vector = vector };
		}

		public Vector3 vector;

		public PositionCell ToCell(IMyCubeGrid grid)
		{
			Vector3I cell;
			vector.ApplyOperation(x => (int)Math.Round(x / grid.GridSize), out cell);
			return cell;
		}

		public PositionBlock ToBlock(IMyCubeBlock block)
		{
			return Vector3.Transform(vector, Matrix.Invert(block.LocalMatrix));
		}

		public PositionWorld ToWorld(IMyCubeGrid grid)
		{
			return Vector3.Transform(vector, grid.WorldMatrix);
		}

		public override string ToString()
		{
			return vector.ToString();
		}

	}

	/// <summary>
	/// Postion in world space.
	/// </summary>
	public struct PositionWorld
	{

		public static implicit operator Vector3D(PositionWorld position)
		{
			return position.vector;
		}

		public static implicit operator PositionWorld(Vector3D vector)
		{
			return new PositionWorld() { vector = vector };
		}

		public Vector3D vector;

		public PositionBlock ToBlock(IMyCubeBlock block)
		{
			return new PositionBlock() { vector = Vector3D.Transform(vector, block.WorldMatrixNormalizedInv) };
		}

		public PositionGrid ToGrid(IMyCubeGrid grid)
		{
			return new PositionGrid() { vector = Vector3D.Transform(vector, grid.WorldMatrixNormalizedInv) };
		}

		public override string ToString()
		{
			return vector.ToString();
		}

	}

}
