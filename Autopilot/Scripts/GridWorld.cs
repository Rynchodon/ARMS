// skip file on build

#define LOG_ENABLED //remove on build

using System;
//using System.Collections.Generic;
//using System.Linq;
//using System.Text;

//using Sandbox.Common;
//using Sandbox.Common.Components;
//using Sandbox.Common.ObjectBuilders;
//using Sandbox.Definitions;
//using Sandbox.Engine;
//using Sandbox.Game;
using Sandbox.ModAPI;
//using Sandbox.ModAPI.Ingame;
//using Sandbox.ModAPI.Interfaces;
using VRageMath;

namespace Rynchodon.Autopilot
{
	/// <summary>
	/// for all the interactions between grid space and world space
	/// this class is deprecated, it is replaced by AbsoluteVector3D and RelativeVector3F
	/// </summary>
	public static class GridWorld
	{
		private static Logger myLogger = null;
		[System.Diagnostics.Conditional("LOG_ENABLED")]
		private static void log(string toLog, string method = null, Logger.severity level = Logger.severity.DEBUG)
		{
			if (myLogger == null)
				myLogger = new Logger("", "GridWorld");
			myLogger.log(level, method, toLog);
		}

		/// <summary>
		/// converts from local metres to world coordinates
		/// </summary>
		/// <param name="target"></param>
		/// <param name="localF"></param>
		/// <returns>as world vector</returns>
		public static Vector3D gridV3toWorld(Sandbox.ModAPI.IMyCubeGrid target, Vector3 localF)
		{
			int x = (int)Math.Round(localF.X / target.GridSize);
			int y = (int)Math.Round(localF.Y / target.GridSize);
			int z = (int)Math.Round(localF.Z / target.GridSize);
			return target.GridIntegerToWorld(new Vector3I(x, y, z));
		}

		/// <summary>
		/// converts from local metres to world relative coordinates
		/// </summary>
		/// <param name="target"></param>
		/// <param name="localF"></param>
		/// <returns></returns>
		public static Vector3D gridV3toWorldRelative(Sandbox.ModAPI.IMyCubeGrid target, Vector3 localF)
		{
			//(new Logger(null, "GridWorld")).log(Logger.severity.DEBUG, "gridV3toWorldRelative()", "target=" + target + ", localF" + localF + ", gridV3toWorld=" + gridV3toWorld(target, localF) + ", position=" + target.GetPosition());
			return gridV3toWorld(target, localF) - target.GetPosition();
		}

		public static Vector3D gridV3toWorldRelative(Sandbox.ModAPI.IMyCubeGrid target, Vector3I localIntegers)
		{
			return target.GridIntegerToWorld(localIntegers) - target.GetPosition();
		}

		/// <summary>
		/// take a Vector3D that is relative to a RC and convert it to world coordinates.
		/// uses the convention of (right, up, back)
		/// </summary>
		/// <param name="remoteControl"></param>
		/// <param name="RCrelative"></param>
		/// <returns>as world vector</returns>
		public static Vector3D RCtoWorld(Sandbox.ModAPI.IMyCubeBlock remoteControl, Vector3 RCrelative)
		{
			// orient according to RC
			Vector3D resultant = Vector3D.Zero;
			Base6Directions.Direction RCdirection = remoteControl.Orientation.Left;
			resultant -= Base6Directions.GetVector(RCdirection) * RCrelative.X;
			RCdirection = remoteControl.Orientation.Up;
			resultant += Base6Directions.GetVector(RCdirection) * RCrelative.Y;
			RCdirection = remoteControl.Orientation.Forward;
			resultant -= Base6Directions.GetVector(RCdirection) * RCrelative.Z;

			// add to RC position
			resultant += remoteControl.Position * remoteControl.CubeGrid.GridSize;

			// convert to world
			return gridV3toWorld(remoteControl.CubeGrid, resultant);
		}

		public static Vector3D relative_RCtoWorld(Sandbox.ModAPI.IMyCubeBlock remoteControl, Vector3 RCrelative)
		{
			return RCtoWorld(remoteControl, RCrelative) - remoteControl.GetPosition();
		}

		public static Vector3 relative_worldToMatrix(MatrixD matrix, Vector3 world)
		{
			Vector3 resultant = world.Dot(matrix.Right) * Base6Directions.GetVector(Base6Directions.Direction.Right);
			resultant += world.Dot(matrix.Up) *  Base6Directions.GetVector(Base6Directions.Direction.Up);
			resultant += world.Dot(matrix.Backward) *  Base6Directions.GetVector(Base6Directions.Direction.Backward);
			//log("given world=" + world + ", resultant=" + resultant, "relative_worldToMatrix()", Logger.severity.TRACE);
			//log("... matrix right = "+matrix.Right+", matrix up = "+matrix.Up+", matrix back = "+matrix.Backward, "relative_worldToMatrix()", Logger.severity.TRACE);
			return resultant;
		}

		public static Vector3 relative_worldToGrid(Sandbox.ModAPI.IMyCubeGrid grid, Vector3 world)
		{
			return relative_worldToMatrix(grid.WorldMatrix, world);
		}

		public static Vector3 relative_worldToBlock(Sandbox.ModAPI.IMyCubeBlock block, Vector3 world)
		{
			return relative_worldToMatrix(block.WorldMatrix, world);
		}

		///// <summary>
		///// returns block.dir
		///// </summary>
		///// <param name="block"></param>
		///// <param name="dir"></param>
		///// <returns></returns>
		//public static Base6Directions.Direction getBlockDirection(IMyCubeBlock block, Base6Directions.Direction dir)
		//{
		//	switch (dir)
		//	{
		//		case Base6Directions.Direction.Right:
		//			return Base6Directions.GetFlippedDirection( block.Orientation.Left);
		//		case Base6Directions.Direction.Left:
		//			return block.Orientation.Left;

		//		case Base6Directions.Direction.Down:
		//			return Base6Directions.GetFlippedDirection(block.Orientation.Up);
		//		case Base6Directions.Direction.Up:
		//			return block.Orientation.Up;

		//		case Base6Directions.Direction.Backward:
		//			return Base6Directions.GetFlippedDirection(block.Orientation.Forward);
		//		case Base6Directions.Direction.Forward:
		//			return block.Orientation.Forward;
		//	}

		//	myLogger.log(Logger.severity.ERROR, "getBlockDirection()", "could not match direction: " + dir);
		//	return Base6Directions.Direction.Forward;
		//}

		///// <summary>
		///// block.return = dir
		///// </summary>
		///// <param name="block"></param>
		///// <param name="dir"></param>
		///// <returns></returns>
		//public static Base6Directions.Direction reverseGetBlockDirection(IMyCubeBlock block, Base6Directions.Direction dir)
		//{
		//	foreach (Base6Directions.Direction direction in Base6Directions.EnumDirections)
		//	{
		//		if (getBlockDirection(block, direction) == dir)
		//			return direction;
		//	}

		//	myLogger.log(Logger.severity.ERROR, "reverseGetBlockDirection()", "could not match direction: " + dir);
		//	return Base6Directions.Direction.Forward;
		//}
	}
}
