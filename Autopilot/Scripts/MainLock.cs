using System;
using System.Collections.Generic;
//using System.Linq;
//using System.Text;

using Sandbox.Common.ObjectBuilders;
using Sandbox.ModAPI;
using VRage;
using VRageMath;

namespace Rynchodon.Autopilot
{
	/// <summary>
	/// Obtains a shared lock on core thread while performing an action. Locks can only be obtained while core is running.
	/// </summary>
	internal static class MainLock
	{
		public static FastResourceLock Lock = new FastResourceLock();

		public static void GetBlocks_Safe(this IMyCubeGrid grid, List<IMySlimBlock> blocks, Func<IMySlimBlock, bool> collect = null)
		{
			using (Lock.AcquireSharedUsing())
				grid.GetBlocks(blocks, collect);
		}

		public static void GetEntities_Safe(this IMyEntities entitiesObject, HashSet<IMyEntity> entities, Func<IMyEntity, bool> collect = null)
		{
			using (Lock.AcquireSharedUsing())
				entitiesObject.GetEntities(entities, collect);
		}

		public static List<IMyEntity> GetEntitiesInAABB_Safe(this IMyEntities entitiesObject, ref BoundingBoxD boundingBox)
		{
			using (Lock.AcquireSharedUsing())
				return entitiesObject.GetEntitiesInAABB(ref boundingBox);
		}

		public static List<IMyEntity> GetEntitiesInSphere_Safe(this IMyEntities entitiesObject, ref BoundingSphereD boundingSphere)
		{
			using (Lock.AcquireSharedUsing())
				return entitiesObject.GetEntitiesInSphere(ref boundingSphere);
		}

		public static MyObjectBuilder_CubeBlock GetObjectBuilder_Safe(this IMySlimBlock block)
		{
			using (Lock.AcquireSharedUsing())
				return block.GetObjectBuilder();
		}

		public static MyObjectBuilder_CubeBlock GetSlimObjectBuilder_Safe(this IMyCubeBlock block)
		{
			using (Lock.AcquireSharedUsing())
			{
				IMySlimBlock slim = block.CubeGrid.GetCubeBlock(block.Position);
				return slim.GetObjectBuilder();
			}
		}

	}
}
