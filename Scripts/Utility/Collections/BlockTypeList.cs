using System;
using System.Collections.Generic; // from mscorlib.dll, System.dll, System.Core.dll, and VRage.Library.dll
using VRage.Game.ModAPI; // from VRage.Game.dll
using VRage.ObjectBuilders;

namespace Rynchodon
{
	
	public class BlockTypeList
	{

		public readonly string[] m_blockNamesContain;
		public readonly List<List<MyObjectBuilderType>> blocks = new List<List<MyObjectBuilderType>>();
		public readonly FastResourceLock m_lock = new FastResourceLock("BlockTypeList");

		private readonly LockedDictionary<string, MyObjectBuilderType> m_source;
		private int m_count;

		public BlockTypeList(LockedDictionary<string, MyObjectBuilderType> source, string[] blockNamesContain)
		{
			this.m_source = source;
			this.m_blockNamesContain = blockNamesContain;

			UpdateFromSource();
		}

		public void Search(CubeGridCache cache, ref int index, Func<IMyCubeBlock, bool> function)
		{
			if (m_count != m_source.Count)
				UpdateFromSource();

			using (m_lock.AcquireSharedUsing())
				for (index = 0; index < blocks.Count; index++)
					foreach (MyObjectBuilderType blockType in blocks[index])
					{
						var blockList = cache.GetBlocksOfType(blockType);
						if (blockList == null)
							continue;
						foreach (IMyCubeBlock block in blockList)
							if (function(block))
								return;
					}
		}

		public void UpdateFromSource()
		{
			if (m_count == m_source.Count)
				return;

			using (m_lock.AcquireExclusiveUsing())
			{
				if (m_count == m_source.Count)
					return;

				HashSet<MyObjectBuilderType> added = new HashSet<MyObjectBuilderType>();
				blocks.Clear();

				using (m_source.lock_Dictionary.AcquireSharedUsing())
				{
					foreach (string name in m_blockNamesContain)
					{
						List<MyObjectBuilderType> typeList = new List<MyObjectBuilderType>();
						blocks.Add(typeList);

						foreach (var pair in m_source.Dictionary)
							if (pair.Key.looseContains(name) && added.Add(pair.Value))
								typeList.Add(pair.Value);
					}
					m_count = m_source.Dictionary.Count;
				}
			}
		}

	}

}
