#if DEBUG
#define DEBUG_DRAW
#endif

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Rynchodon.Update;
using Sandbox.ModAPI;
using VRage.Game.ModAPI;

namespace Rynchodon.Autopilot.Movement
{
	class AeroEffects
	{

		private const ulong ProfileWait = 200uL;

		private readonly Logger m_logger;
		private readonly IMyCubeGrid m_grid;

		private ulong m_profileAt;
#if DEBUG_DRAW
		private bool m_profilerDebugDraw;
		private int m_maxSteps = int.MaxValue;
#endif

		private AeroProfiler value_profiler;
		private AeroProfiler m_profiler
		{
			get { return value_profiler; }
			set
			{
#if DEBUG_DRAW
				DebugDraw(false);
#endif
				value_profiler = value;
			}
		}

		public AeroEffects(IMyCubeGrid grid)
		{
			this.m_logger = new Logger(grid);
			this.m_grid = grid;
			this.m_profileAt = Globals.UpdateCount + ProfileWait;

			m_grid.OnBlockAdded += OnBlockChange;
			m_grid.OnBlockRemoved += OnBlockChange;
#if DEBUG_DRAW
			MyAPIGateway.Utilities.MessageEntered += Utilities_MessageEntered;
			m_grid.OnClose += OnGridClose;
#endif
		}

#if DEBUG_DRAW
		private void Utilities_MessageEntered(string messageText, ref bool sendToOthers)
		{
			int steps;
			if (!int.TryParse(messageText, out steps) || steps == m_maxSteps)
				return;
			m_maxSteps = steps;
			m_logger.debugLog("Running profiler", Logger.severity.DEBUG);
			m_profileAt = ulong.MaxValue;
			m_profiler = new AeroProfiler(m_grid, m_maxSteps);
		}
#endif

		public void Update100()
		{
			if (m_profileAt <= Globals.UpdateCount)
			{
				m_logger.debugLog("Running profiler", Logger.severity.DEBUG);
				m_profileAt = ulong.MaxValue;
#if DEBUG_DRAW
				m_profiler = new AeroProfiler(m_grid, m_maxSteps);
#else
				m_profiler = new AeroProfiler(m_grid);
#endif
			}
#if DEBUG_DRAW
			else if (m_profiler != null && !m_profiler.Running && m_profiler.Success)
				DebugDraw(true);
#endif
		}

		private void OnBlockChange(IMySlimBlock obj)
		{
			m_profileAt = Globals.UpdateCount + ProfileWait;
		}

#if DEBUG_DRAW
		private void OnGridClose(VRage.ModAPI.IMyEntity obj)
		{
			m_profiler = null;
			MyAPIGateway.Utilities.MessageEntered -= Utilities_MessageEntered;
		}

		private void DebugDraw(bool enable)
		{
			if (enable == m_profilerDebugDraw)
				return;

			if (enable)
			{
				m_profilerDebugDraw = true;
				UpdateManager.Register(1, m_profiler.DebugDraw_Velocity);
				Logger.DebugNotify("Debug drawing velocity");
			}
			else
			{
				m_profilerDebugDraw = false;
				UpdateManager.Unregister(1, m_profiler.DebugDraw_Velocity);
				Logger.DebugNotify("Stop debug drawing velocity");
			}
		}
#endif

	}
}
