#define LOG_ENABLED //remove on build

using System;
//using System.Collections.Generic;
//using System.Linq;
//using System.Text;

using Sandbox.ModAPI;
using VRageMath;

namespace Rynchodon
{
	/// <summary>
	/// For converting between local and world Vectors.
	/// </summary>
	public class RelativeVector3F
	{
		private const float precisionMultiplier = 1024;

		// one of these will always be set on create
		private Vector3? value__worldAbsolute = null;
		private Vector3? value__world = null;
		private Vector3? value__grid = null;
		private Vector3? value__block = null;

		private IMyCubeGrid cubeGrid; // always set on create
		private IMyCubeBlock cubeBlock = null;

		private RelativeVector3F() { }

		/// <summary>
		/// create from an absolute world vector (G.P.S.)
		/// </summary>
		public static RelativeVector3F createFromWorldAbsolute(Vector3 worldAbsoulte, IMyCubeGrid cubeGrid)
		{
			RelativeVector3F result = new RelativeVector3F();
			result.value__worldAbsolute = worldAbsoulte;
			result.cubeGrid = cubeGrid;
			result.getWorld();
			return result;
		}

		/// <summary>
		/// create from a relative world vector (current position - G.P.S.)
		/// </summary>
		public static RelativeVector3F createFromWorld(Vector3 world, IMyCubeGrid cubeGrid)
		{
			RelativeVector3F result = new RelativeVector3F();
			result.value__world = world;
			result.cubeGrid = cubeGrid;
			return result;
		}

		/// <summary>
		/// creates from a local position
		/// </summary>
		public static RelativeVector3F createFromLocal(Vector3 grid, IMyCubeGrid cubeGrid)
		{
			RelativeVector3F result = new RelativeVector3F();
			result.value__grid = grid;
			result.cubeGrid = cubeGrid;
			return result;
		}

		/// <summary>
		/// create from a vector relative to a block (including block orientation)
		/// </summary>
		public static RelativeVector3F createFromBlock(Vector3 fromBlock, IMyCubeBlock block)
		{
			RelativeVector3F result = new RelativeVector3F();
			result.value__block = fromBlock;
			result.cubeGrid = block.CubeGrid;
			result.cubeBlock = block;
			return result;
		}

		/// <summary>
		/// gets the aboslute world position (G.P.S.)
		/// </summary>
		public Vector3 getWorldAbsolute()
		{
			if (value__worldAbsolute != null)
				return (Vector3)value__worldAbsolute;

			// create from world
			return getWorld() + cubeGrid.GetPosition();
		}

		/// <summary>
		/// gets the relative world position
		/// </summary>
		public Vector3 getWorld()
		{
			if (value__world != null)
				return (Vector3)value__world;

			if (value__worldAbsolute != null)
			{
				// create from world absolute
				value__world = (Vector3)value__worldAbsolute - cubeGrid.GetPosition();
			}

			// create from grid
			Vector3I gridInt = Vector3I.Round(getLocal() * precisionMultiplier / cubeGrid.GridSize);
			value__world = (cubeGrid.GridIntegerToWorld(gridInt)) / precisionMultiplier;
			return (Vector3)value__world;
		}

		/// <summary>
		/// gets the local position
		/// </summary>
		public Vector3 getLocal()
		{
			if (value__grid != null)
				return (Vector3)value__grid;

			if (value__world != null)
			{
				// create from world
				Vector3I gridInt = cubeGrid.WorldToGridInteger((Vector3)value__world * precisionMultiplier + cubeGrid.GetPosition());
				value__grid = (gridInt * cubeGrid.GridSize) / precisionMultiplier;
				return (Vector3)value__grid;
			}

			// create from block
			// create from block
			// orient according to block
			Vector3D resultant = Vector3D.Zero;
			Vector3D blockRelative = (Vector3D)value__block;
			Base6Directions.Direction RCdirection = cubeBlock.Orientation.Left;
			resultant -= (Vector3D)Base6Directions.GetVector(RCdirection) * blockRelative.X;
			RCdirection = cubeBlock.Orientation.Up;
			resultant += (Vector3D)Base6Directions.GetVector(RCdirection) * blockRelative.Y;
			RCdirection = cubeBlock.Orientation.Forward;
			resultant -= (Vector3D)Base6Directions.GetVector(RCdirection) * blockRelative.Z;

			// add block position
			value__grid = resultant + cubeBlock.Position * cubeGrid.GridSize;
			return (Vector3D)value__grid;
		}

		/// <summary>
		/// gets the local postion relative to a block (including block orientation)
		/// </summary>
		public Vector3 getBlock(Sandbox.ModAPI.IMyCubeBlock cubeBlock)
		{
			cubeBlock.throwIfNull_argument("cubeBlock");

			if (this.cubeBlock == cubeBlock)
				return (Vector3)value__block;

			Vector3 v3;
			MatrixD matrix;
			if (value__grid != null)
			{
				// create from grid
				v3 = ((Vector3)this.value__grid);
				matrix = cubeBlock.LocalMatrix;
			}
			else // assuming (world != null)
			{
				v3 = ((Vector3)this.value__world);
				matrix = cubeBlock.WorldMatrix;
			}

			Vector3 resultant = v3.Dot(matrix.Right) * Base6Directions.GetVector(Base6Directions.Direction.Right);
			resultant += v3.Dot(matrix.Up) * Base6Directions.GetVector(Base6Directions.Direction.Up);
			resultant += v3.Dot(matrix.Backward) * Base6Directions.GetVector(Base6Directions.Direction.Backward);
			return resultant;
		}
	}
}
