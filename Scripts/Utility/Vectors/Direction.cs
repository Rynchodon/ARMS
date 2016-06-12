using VRage.Game.ModAPI;
using VRageMath;

namespace Rynchodon.Utility.Vectors
{

	/*
	 * Trying something new: multiple structs to replace RelativeDirection3
	 */

	/// <summary>
	/// Directional vector in block space.
	/// </summary>
	public struct DirectionBlock
	{

		public static implicit operator Vector3(DirectionBlock direction)
		{
			return direction.vector;
		}

		public static implicit operator DirectionBlock(Vector3 vector)
		{
			return new DirectionBlock() { vector = vector };
		}

		public Vector3 vector;

		public DirectionGrid ToGrid(IMyCubeBlock block)
		{
			Vector3 result;
			Matrix orientation = block.LocalMatrix.GetOrientation();
			Vector3.Transform(ref vector, ref orientation, out result);
			return new DirectionGrid() { vector = result };
		}

		public DirectionWorld ToWorld(IMyCubeBlock block)
		{
			Vector3 result;
			Matrix orientation = block.WorldMatrix.GetOrientation();
			Vector3.Transform(ref vector, ref orientation, out result);
			return new DirectionWorld() { vector = result };
		}

		public override string ToString()
		{
			return vector.ToString();
		}

	}

	/// <summary>
	/// Directional vector in grid space.
	/// </summary>
	public struct DirectionGrid
	{

		public static implicit operator Vector3(DirectionGrid direction)
		{
			return direction.vector;
		}

		public static implicit operator DirectionGrid(Vector3 vector)
		{
			return new DirectionGrid() { vector = vector };
		}

		public static DirectionGrid operator *(DirectionGrid direction, float value)
		{
			return new DirectionGrid() { vector = direction.vector * value };
		}

		public Vector3 vector;

		public DirectionBlock ToBlock(IMyCubeBlock block)
		{
			Matrix orientation = block.LocalMatrix.GetOrientation();
			Matrix inverted;
			Matrix.Invert(ref orientation, out inverted);
			Vector3 result;
			Vector3.Transform(ref vector, ref inverted, out result);
			return new DirectionBlock() { vector = result };
		}

		public DirectionWorld ToWorld(IMyCubeGrid grid)
		{
			Vector3 result;
			Matrix matrix = grid.WorldMatrix.GetOrientation();
			Vector3.Transform(ref vector, ref matrix, out result);
			return new DirectionWorld() { vector = result };
		}

		public override string ToString()
		{
			return vector.ToString();
		}

	}

	/// <summary>
	/// Directional vector in world space.
	/// </summary>
	public struct DirectionWorld
	{

		public static implicit operator Vector3(DirectionWorld direction)
		{
			return direction.vector;
		}

		public static implicit operator DirectionWorld(Vector3 vector)
		{
			return new DirectionWorld() { vector = vector };
		}

		public Vector3 vector;

		public DirectionBlock ToBlock(IMyCubeBlock block)
		{
			Matrix inverted = block.WorldMatrixNormalizedInv.GetOrientation();
			Vector3 result;
			Vector3.Transform(ref vector, ref inverted, out result);
			return new DirectionBlock() { vector = result };
		}

		public DirectionGrid ToGrid(IMyCubeGrid grid)
		{
			Matrix inverted = grid.WorldMatrixNormalizedInv.GetOrientation();
			Vector3 result;
			Vector3.Transform(ref vector, ref inverted, out result);
			return new DirectionGrid() { vector = result };
		}

		public override string ToString()
		{
			return vector.ToString();
		}

	}
}
