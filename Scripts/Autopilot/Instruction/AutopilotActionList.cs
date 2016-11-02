using System.Collections;
using System.Runtime.CompilerServices;
using Rynchodon.Autopilot.Pathfinding;

namespace Rynchodon.Autopilot.Instruction
{
	public class AutopilotActionList
	{

		private const int MaxIndex = 1000, MaxDepth = 10;
		public delegate void AutopilotAction(Pathfinder pathfinder);

		private readonly ArrayList m_mainList = new ArrayList();
		private int m_mainListIndex;
		private AutopilotActionList m_sublist;

		public AutopilotAction Current { get; private set; }
		public int CurrentIndex { get; private set; }
		public bool IsEmpty { get { return m_mainList.Count == 0; } }

		public AutopilotActionList()
		{
			Reset();
		}

		public void Add(AutopilotAction item)
		{
			m_mainList.Add(item);
			m_mainListIndex = int.MaxValue;
		}

		public void Add(TextPanelMonitor item)
		{
			m_mainList.Add(item);
			m_mainListIndex = int.MaxValue;
		}

		public void Clear()
		{
			m_mainList.Clear();
			Reset();
		}

		public bool MoveNext()
		{
			int depth = 0;
			return MoveNext(ref depth);
		}

		private bool MoveNext(ref int depth)
		{
			if (m_mainListIndex == int.MaxValue)
			{
				Logger.AlwaysLog("List was not reset since last addition", Logger.severity.ERROR);
				Reset();
				return false;
			}
			CurrentIndex++;
			depth++;
			if (CurrentIndex > MaxIndex)
			{
				Logger.AlwaysLog("Over command limit: " + CurrentIndex, Logger.severity.DEBUG);
				return false;
			}
			if (depth > MaxDepth)
			{
				Logger.AlwaysLog("Over maximum command depth: " + depth, Logger.severity.DEBUG);
				return false;
			}
			if (m_sublist != null)
			{
				if (m_sublist.MoveNext(ref depth))
				{
					Current = m_sublist.Current;
					Logger.DebugLog("CurrentIndex: " + CurrentIndex + ", m_mainListIndex: " + m_mainListIndex + ", sublist CurrentIndex: " + m_sublist.CurrentIndex + ", sublist m_mainListIndex: " + m_sublist.m_mainListIndex);
					return true;
				}
				m_sublist = null;
			}
			m_mainListIndex++;
			if (m_mainListIndex >= m_mainList.Count)
			{
				Logger.DebugLog("End of main list, m_mainListIndex: " + m_mainListIndex + ", m_mainList.Count: " + m_mainList.Count);
				Current = null;
				return false;
			}

			object element = m_mainList[m_mainListIndex];
			Current = element as AutopilotAction;
			if (Current != null)
			{
				Logger.DebugLog("CurrentIndex: " + CurrentIndex + ", m_mainListIndex: " + m_mainListIndex);
				return true;
			}

			TextPanelMonitor monitor = (TextPanelMonitor)element;
			Logger.DebugLog("Resting sublist: " + monitor.TextPanel.nameWithId());
			m_sublist = monitor.AutopilotActions;
			m_sublist.Reset();
			return MoveNext(ref depth);
		}

		public void Reset([CallerMemberName] string caller = null)
		{
			Logger.DebugLog("entered from " + caller);
			m_sublist = null;
			CurrentIndex = m_mainListIndex = -1;
		}

	}
}
