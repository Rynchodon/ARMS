using System.Collections.Generic;
using System.Linq;
using Sandbox.Game.Entities;
using VRage;
using VRage.Game;

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

		private MyDefinitionId[][] m_blocks;
		private FastResourceLock m_lock = new FastResourceLock();
		private int m_sourceCount;

		public BlockTypeList(string[] blockNamesContain)
		{
			this.BlockNamesContain = blockNamesContain;
		}

		public IEnumerable<MyDefinitionId[]> IdGroups()
		{
			if (m_sourceCount != CubeGridCache.DefinitionType.Count)
				UpdateFromSource();

			m_lock.AcquireShared();
			try
			{
				for (int index = 0; index < m_blocks.Length; index++)
					yield return m_blocks[index];
			}
			finally
			{
				m_lock.ReleaseShared();
			}
		}

		public IEnumerable<MyCubeBlock> Blocks(CubeGridCache cache)
		{
			if (m_sourceCount != CubeGridCache.DefinitionType.Count)
				UpdateFromSource();

			m_lock.AcquireShared();
			try
			{
				for (int order = 0; order < m_blocks.Length; order++)
				{
					MyDefinitionId[] array = m_blocks[order];
					for (int index = 0; index < array.Length; index++)
						foreach (MyCubeBlock block in cache.BlocksOfType(array[index]))
							yield return block;
				}
			}
			finally
			{
				m_lock.ReleaseShared();
			}
		}

		/// <summary>
		/// Count the number of blocks the cache contains that match BlockNamesContain.
		/// </summary>
		/// <param name="cache">Where to search for blocks.</param>
		/// <returns>An array of integers indicating how many blocks match each string in BlockNamesContain.</returns>
		public int[] Count(CubeGridCache cache)
		{
			if (m_sourceCount != CubeGridCache.DefinitionType.Count)
				UpdateFromSource();

			int[] results = new int[m_blocks.Length];

			using (m_lock.AcquireSharedUsing())
				for (int order = 0; order < m_blocks.Length; order++)
				{
					MyDefinitionId[] array = m_blocks[order];
					for (int index = 0; index < array.Length; index++)
					{
						results[order] += cache.CountByType(array[index]);
					}
				}

			return results;
		}

		private void UpdateFromSource()
		{
			if (m_sourceCount == CubeGridCache.DefinitionType.Count)
				return;

			using (m_lock.AcquireExclusiveUsing())
			{
				int count = CubeGridCache.DefinitionType.Count;
				if (m_sourceCount == count)
					return;
				m_sourceCount = count;

				m_blocks = new MyDefinitionId[BlockNamesContain.Length][];

				HashSet<MyDefinitionId> added = new HashSet<MyDefinitionId>();
				List<MyDefinitionId> idList = new List<MyDefinitionId>();

				using (CubeGridCache.DefinitionType.Lock.AcquireSharedUsing())
				{
					for (int i = BlockNamesContain.Length - 1; i >= 0; i--)
					{
						idList.Clear();
						foreach (var pair in CubeGridCache.DefinitionType.Dictionary)
							if (pair.Value.looseContains(BlockNamesContain[i]) && added.Add(pair.Key))
								idList.Add(pair.Key);
						m_blocks[i] = idList.ToArray();
					}
				}
			}
		}

	}

}
