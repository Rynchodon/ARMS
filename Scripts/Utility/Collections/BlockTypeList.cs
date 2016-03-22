using System;
using System.Collections.Generic; // from mscorlib.dll, System.dll, System.Core.dll, and VRage.Library.dll
using System.Linq;
using VRage.Game.ModAPI; // from VRage.Game.dll
using VRage.ObjectBuilders;

namespace Rynchodon
{
	
	/// <summary>
	/// Finds block types that looseContains strings from BlockNamesContain so they can be looked up in a CubeGridCache.
	/// </summary>
	public class BlockTypeList
	{

		public static BlockTypeList Union(BlockTypeList first, BlockTypeList second)
		{
			return new BlockTypeList(first.BlockNamesContain.Union(second.BlockNamesContain).ToArray());
		}

		/// <summary>Strings to check against definitions, should be treated as read only.</summary>
		public readonly string[] BlockNamesContain;

		private List<List<MyObjectBuilderType>> m_blocks = new List<List<MyObjectBuilderType>>();
		private FastResourceLock m_lock = new FastResourceLock();
		private int m_sourceCount;

		public BlockTypeList(string[] blockNamesContain)
		{
			this.BlockNamesContain = blockNamesContain;
		}

		/// <summary>
		/// Iterates over every IMyCubeBlock with a definition matching any string from BlockNamesContain.
		/// </summary>
		/// <param name="cache">Where to get blocks from.</param>
		/// <param name="index">The index corresponding to the position in BlockNamesContain of the current block.</param>
		/// <param name="action">Invoked on each block</param>
		public void ForEach(CubeGridCache cache, ref int index, Action<IMyCubeBlock> action)
		{
			if (m_sourceCount != CubeGridCache.DefinitionType .Count)
				UpdateFromSource();

			using (m_lock.AcquireSharedUsing())
				for (index = 0; index < m_blocks.Count; index++)
					foreach (MyObjectBuilderType blockType in m_blocks[index])
					{
						var blockList = cache.GetBlocksOfType(blockType);
						if (blockList == null)
							continue;
						foreach (IMyCubeBlock block in blockList)
							action(block);
					}
		}

		/// <summary>
		/// Checks for the cache containing any blocks that match BlockNamesContain.
		/// </summary>
		/// <param name="cache">Where to search for blocks.</param>
		/// <returns>True iff the cache contains any blocks matching any string from BlockNamesContain</returns>
		public bool HasAny(CubeGridCache cache)
		{
			if (m_sourceCount != CubeGridCache.DefinitionType.Count)
				UpdateFromSource();

			using (m_lock.AcquireSharedUsing())
				foreach (List<MyObjectBuilderType> typesWithString in m_blocks)
					foreach (MyObjectBuilderType blockType in typesWithString)
						if (cache.CountByType(blockType) != 0)
							return true;

			return false;
		}

		// not used
		//public void Search(CubeGridCache cache, Action<IMyCubeBlock> action)
		//{
		//	if (m_sourceCount != CubeGridCache.DefinitionType.Count)
		//		UpdateFromSource();

		//	using (m_lock.AcquireSharedUsing())
		//		foreach (List<MyObjectBuilderType> typesWithString in m_blocks)
		//		{
		//			bool anyBlocksFound = false;

		//			foreach (MyObjectBuilderType blockType in typesWithString)
		//			{
		//				var blocksOfType = cache.GetBlocksOfType(blockType);
		//				bool atLeastOne = blocksOfType != null && blocksOfType.Count != 0;
		//				if (atLeastOne)
		//				{
		//					anyBlocksFound = true;
		//					foreach (IMyCubeBlock block in blocksOfType)
		//						action(block);
		//				}
		//			}

		//			if (anyBlocksFound)
		//				return;
		//		}
		//}

		private void UpdateFromSource()
		{
			using (m_lock.AcquireExclusiveUsing())
			{
				if (m_sourceCount == CubeGridCache.DefinitionType.Count)
					return;

				HashSet<MyObjectBuilderType> added = new HashSet<MyObjectBuilderType>();
				m_blocks.Clear();

				using (CubeGridCache.DefinitionType.lock_Dictionary.AcquireSharedUsing())
				{
					foreach (string name in BlockNamesContain)
					{
						List<MyObjectBuilderType> typeList = new List<MyObjectBuilderType>();
						m_blocks.Add(typeList);

						foreach (var pair in CubeGridCache.DefinitionType.Dictionary)
							if (pair.Key.looseContains(name) && added.Add(pair.Value))
								typeList.Add(pair.Value);
					}
					m_sourceCount = CubeGridCache.DefinitionType.Dictionary.Count;
				}
			}
		}

	}

}
