using VRage.Game.ModAPI;
using VRageMath;

namespace Rynchodon
{
	public class RelativeDirection3F
	{
		//One of these will always be set on creation.
		private Vector3?
			direction_world = null,
			direction_local = null,
			direction_block = null;

		private Vector3?
			norm_world = null,
			norm_local = null,
			norm_block = null;

		private readonly IMyCubeGrid CubeGrid;
		private IMyCubeBlock CubeBlock;

		private RelativeDirection3F(IMyCubeGrid grid)
		{
			this.CubeGrid = grid;
		}


		public static RelativeDirection3F FromWorld(IMyCubeGrid grid, Vector3 worldDirection)
		{
			RelativeDirection3F result = new RelativeDirection3F(grid);
			result.direction_world = worldDirection;
			return result;
		}

		public static RelativeDirection3F FromLocal(IMyCubeGrid grid, Vector3 localDirection)
		{
			RelativeDirection3F result = new RelativeDirection3F(grid);
			result.direction_local = localDirection;
			return result;
		}

		public static RelativeDirection3F FromBlock(IMyCubeBlock block, Vector3 blockDirection)
		{
			RelativeDirection3F result = new RelativeDirection3F(block.CubeGrid);
			result.direction_block = blockDirection;
			result.CubeBlock = block;
			return result;
		}


		public Vector3 ToWorld()
		{
			if (!direction_world.HasValue)
			{
				if (direction_local.HasValue)
				{
					MatrixD Transform = CubeGrid.WorldMatrix.GetOrientation();
					direction_world = Vector3.Transform(direction_local.Value, Transform);
				}
				else // value_block.HasValue
				{
					MatrixD Transform = CubeBlock.WorldMatrix.GetOrientation();
					direction_world = Vector3.Transform(direction_block.Value, Transform);
				}
			}

			return direction_world.Value;
		}

		public Vector3 ToLocal()
		{
			if (!direction_local.HasValue)
			{
				if (direction_world.HasValue)
				{
					MatrixD Transform = CubeGrid.WorldMatrixNormalizedInv.GetOrientation();
					direction_local = Vector3.Transform(direction_world.Value, Transform);
				}
				else // value_block.HasValue
				{
					MatrixD Transform = CubeBlock.LocalMatrix.GetOrientation();
					direction_local = Vector3.Transform(direction_block.Value, Transform);
				}
			}
			return direction_local.Value;
		}

		public Vector3 ToBlock(IMyCubeBlock block)
		{
			if (direction_block.HasValue && CubeBlock == block)
			{
				return direction_block.Value;
			}

			Vector3 result = Vector3.PositiveInfinity;
			if (block.CubeGrid == CubeGrid && direction_local.HasValue)
			{
				MatrixD Transform = Matrix.Invert(block.LocalMatrix).GetOrientation();
				result = Vector3.Transform(direction_local.Value, Transform);
			}
			else
			{
				MatrixD Transform = block.WorldMatrixNormalizedInv.GetOrientation();
				result = Vector3.Transform(ToWorld(), Transform);
			}

			if (CubeBlock == null)
			{
				CubeBlock = block;
				direction_block = result;
			}
			return result;
		}

		public Vector3 ToWorldNormalized()
		{ 
			if (!norm_world.HasValue)
				norm_world = Vector3.Normalize(ToWorld());
			return norm_world.Value;
		}

		public Vector3 ToLocalNormalized()
		{
			if (!norm_local.HasValue)
				norm_local = Vector3.Normalize(ToLocal());
			return norm_local.Value;
		}

		public Vector3 ToBlockNormalized(IMyCubeBlock block)
		{
			if (block == CubeBlock && norm_block.HasValue)
				return norm_block.Value;
			Vector3 NormalizedBlock = Vector3.Normalize(ToBlock(block));
			if (block == CubeBlock) // CubeBlock may have been set by ToBlock()
				norm_block = NormalizedBlock;
			return NormalizedBlock;
		}
	}
}
