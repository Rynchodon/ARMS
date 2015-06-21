using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Sandbox.ModAPI;
using VRageMath;

namespace Rynchodon
{
	public class RelativePosition3F
	{
		//One of these will always be set on creation.
		private Vector3?
			position_world = null,
			position_local = null,
			position_block = null;

		private readonly IMyCubeGrid CubeGrid;
		private IMyCubeBlock CubeBlock;


		private RelativePosition3F(IMyCubeGrid grid)
		{ this.CubeGrid = grid; }


		public static RelativePosition3F FromWorld(IMyCubeGrid grid, Vector3 worldPosition)
		{
			RelativePosition3F result = new RelativePosition3F(grid);
			result.position_world = worldPosition;
			return result;
		}

		public static RelativePosition3F FromLocal(IMyCubeGrid grid, Vector3 localPosition)
		{
			RelativePosition3F result = new RelativePosition3F(grid);
			result.position_local = localPosition;
			return result;
		}

		public static RelativePosition3F FromBlock(IMyCubeBlock block, Vector3 blockPosition)
		{
			RelativePosition3F result = new RelativePosition3F(block.CubeGrid);
			result.position_block = blockPosition;
			result.CubeBlock = block;
			return result;
		}


		public Vector3 ToWorld()
		{
			if (!position_world.HasValue)
			{
				if (position_local.HasValue)
					position_world = Vector3.Transform(position_local.Value, CubeGrid.WorldMatrix);
				else // value_block.HasValue
					position_world = Vector3.Transform(position_block.Value, CubeBlock.WorldMatrix);
			}
			return position_world.Value;
		}

		public Vector3 ToLocal()
		{
			if (!position_local.HasValue)
			{
				if (position_world.HasValue)
					position_local = Vector3.Transform(position_world.Value, CubeGrid.WorldMatrixNormalizedInv);
				else // value_block.HasValue
					position_local = Vector3.Transform(position_block.Value, CubeBlock.LocalMatrix);
			}
			return position_local.Value;
		}

		public Vector3 ToBlock(IMyCubeBlock block)
		{
			if (position_block.HasValue && CubeBlock == block)
				return position_block.Value;

			Vector3 result = Vector3.PositiveInfinity;
			if (block.CubeGrid == CubeGrid && position_local.HasValue)
				result = Vector3.Transform(position_local.Value, Matrix.Invert(block.LocalMatrix));
			else
				result = Vector3.Transform(ToWorld(), block.WorldMatrixNormalizedInv);

			if (CubeBlock == null)
			{
				CubeBlock = block;
				position_block = result;
			}
			return result;
		}
	}
}
