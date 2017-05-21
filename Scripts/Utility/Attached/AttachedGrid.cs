using System;
using System.Collections.Generic;
using Sandbox.Game.Entities;
using Rynchodon.Utility;
using VRage.Collections;
using VRage.Game.ModAPI;

namespace Rynchodon.Attached
{
	public class AttachedGrid
	{
		private class Attachments
		{
			private readonly Dictionary<AttachmentKind, ushort> dictionary = new Dictionary<AttachmentKind, ushort>();

			private IMyCubeGrid m_grid_0;
			private IMyCubeGrid m_grid_1;

			public AttachmentKind attachmentKinds { get; private set; }
			private Logable Log { get { return new Logable(m_grid_0?.DisplayName, m_grid_1?.DisplayName); } }

			public Attachments(IMyCubeGrid grid0, IMyCubeGrid grid1)
			{
				m_grid_0 = grid0;
				m_grid_1 = grid1;
			}

			public void Add(AttachmentKind kind)
			{
				Log.DebugLog("adding: " + kind);

				ushort count;
				if (!dictionary.TryGetValue(kind, out count))
				{
					attachmentKinds |= kind;
					count = 0;
				}

				count++;
				Log.DebugLog(kind + " count: " + count);
				dictionary[kind] = count;
			}

			public void Remove(AttachmentKind kind)
			{
				Log.DebugLog("removing: " + kind);

				ushort count = dictionary[kind];
				count--;
				if (count == 0)
				{
					attachmentKinds &= ~kind;
					dictionary.Remove(kind);
				}
				else
					dictionary[kind] = count;

				Log.DebugLog(kind + " count: " + count);
			}

		}

		[Flags]
		public enum AttachmentKind : byte
		{
			None = 0,
			Piston = 1 << 0,
			Motor = 1 << 1,
			Connector = 1 << 2,
			LandingGear = 1 << 3,

			Permanent = Piston | Motor,
			Terminal = Permanent | Connector,
			Physics = LandingGear | Terminal
		}

		private static MyConcurrentPool<HashSet<AttachedGrid>> s_searchSet = new MyConcurrentPool<HashSet<AttachedGrid>>();
		private static FastResourceLock lock_construct = new FastResourceLock();

		/// <summary>
		/// Determines if two grids are attached.
		/// </summary>
		/// <param name="grid0">The starting grid.</param>
		/// <param name="grid1">The grid to search for.</param>
		/// <param name="allowedConnections">The types of connections allowed between grids.</param>
		/// <returns>True iff the grids are attached.</returns>
		public static bool IsGridAttached(IMyCubeGrid grid0, IMyCubeGrid grid1, AttachmentKind allowedConnections)
		{
			Logger.DebugLog("condition: grid0 == null", Logger.severity.FATAL, condition: grid0 == null);
			Logger.DebugLog("condition: grid1 == null", Logger.severity.FATAL, condition: grid1 == null);

			if (grid0 == grid1)
				return true;

			AttachedGrid attached1 = GetFor(grid0);
			if (attached1 == null)
				return false;
			AttachedGrid attached2 = GetFor(grid1);
			if (attached2 == null)
				return false;

			HashSet<AttachedGrid> search = s_searchSet.Get();
			bool result = attached1.IsGridAttached(attached2, allowedConnections, search);
			search.Clear();
			s_searchSet.Return(search);
			return result;
		}

		/// <summary>
		/// Yields attached grids.
		/// </summary>
		/// <param name="startGrid">Grid to start search from.</param>
		/// <param name="allowedConnections">The types of connections allowed between grids.</param>
		/// <param name="runOnStartGrid">If true, yields the startGrid.</param>
		/// <returns>Each attached grid.</returns>
		public static IEnumerable<IMyCubeGrid> AttachedGrids(IMyCubeGrid startGrid, AttachmentKind allowedConnections, bool runOnStartGrid)
		{
			if (runOnStartGrid)
				yield return startGrid;

			AttachedGrid attached = GetFor(startGrid);
			if (attached == null)
				yield break;

			HashSet<AttachedGrid> search = s_searchSet.Get();
			try
			{
				foreach (IMyCubeGrid grid in attached.Attached(allowedConnections, search))
					yield return grid;
			}
			finally
			{
				search.Clear();
				s_searchSet.Return(search);
			}
		}

		/// <summary>
		/// Yields CubeGridCache for each attached grid, if the grid is closed it is skipped.
		/// </summary>
		/// <param name="startGrid">Grid to start search from.</param>
		/// <param name="allowedConnections">The types of connections allowed between grids.</param>
		/// <param name="runOnStartGrid">If true, yields the startGrid.</param>
		/// <returns>CubeGridCache for each attached grid.</returns>
		public static IEnumerable<CubeGridCache> AttachedGridCache(IMyCubeGrid startGrid, AttachmentKind allowedConnections, bool runOnStartGrid)
		{
			if (runOnStartGrid)
			{
				CubeGridCache cache = CubeGridCache.GetFor(startGrid);
				if (cache != null)
					yield return cache;
			}

			AttachedGrid attached = GetFor(startGrid);
			if (attached == null)
				yield break;

			HashSet<AttachedGrid> search = s_searchSet.Get();
			try
			{
				foreach (IMyCubeGrid grid in attached.Attached(allowedConnections, search))
				{
					CubeGridCache cache = CubeGridCache.GetFor(grid);
					if (cache != null)
						yield return cache;
				}
			}
			finally
			{
				search.Clear();
				s_searchSet.Return(search);
			}
		}

		/// <summary>
		/// Yields each cube block on each attached grid. If a grid is closed, it is skipped.
		/// </summary>
		/// <param name="startGrid">Grid to start search from.</param>
		/// <param name="allowedConnections">The types of connections allowed between grids.</param>
		/// <param name="runOnStartGrid">If true, yields the startGrid.</param>
		/// <returns>Each block on each attached grid</returns>
		public static IEnumerable<MyCubeBlock> AttachedCubeBlocks(IMyCubeGrid startGrid, AttachmentKind allowedConnections, bool runOnStartGrid)
		{
			foreach (CubeGridCache cache in AttachedGridCache(startGrid, allowedConnections, runOnStartGrid))
				foreach (MyCubeBlock block in cache.AllCubeBlocks())
					yield return block;
		}

		/// <summary>
		/// Yields each slim block on each attached grid. If a grid is closed, it is skipped.
		/// </summary>
		/// <param name="startGrid">Grid to start search from.</param>
		/// <param name="allowedConnections">The types of connections allowed between grids.</param>
		/// <param name="runOnStartGrid">If true, yields the startGrid.</param>
		/// <returns>Each block on each attached grid</returns>
		public static IEnumerable<IMySlimBlock> AttachedSlimBlocks(IMyCubeGrid startGrid, AttachmentKind allowedConnections, bool runOnStartGrid)
		{
			foreach (CubeGridCache cache in AttachedGridCache(startGrid, allowedConnections, runOnStartGrid))
				foreach (IMySlimBlock block in cache.AllSlimBlocks())
					yield return block;
		}

		internal static void AddRemoveConnection(AttachmentKind kind, IMyCubeGrid grid1, IMyCubeGrid grid2, bool add)
		{
			if (grid1 == grid2 || Globals.WorldClosed)
				return;

			AttachedGrid
				attached1 = GetFor(grid1),
				attached2 = GetFor(grid2);

			if (attached1 == null || attached2 == null)
				return;

			attached1.AddRemoveConnection(kind, attached2, add);
			attached2.AddRemoveConnection(kind, attached1, add);
		}

		private static AttachedGrid GetFor(IMyCubeGrid grid)
		{
			if (Globals.WorldClosed)
				return null;

			Logger.DebugLog("grid == null", Logger.severity.FATAL, condition: grid == null);

			AttachedGrid attached;
			if (!Registrar.TryGetValue(grid.EntityId, out attached))
			{
				if (grid.Closed)
					return null;
				using (lock_construct.AcquireExclusiveUsing())
					if (!Registrar.TryGetValue(grid.EntityId, out attached))
						attached = new AttachedGrid(grid);
			}

			return attached;
		}

		private readonly IMyCubeGrid myGrid;

		private Logable Log { get { return new Logable(myGrid); } }

		/// <summary>
		/// The grid connected to, the types of connection, the number of connections.
		/// Some connections are counted twice (this reduces code).
		/// </summary>
		private readonly LockedDictionary<AttachedGrid, Attachments> Connections = new LockedDictionary<AttachedGrid, Attachments>();

		private AttachedGrid(IMyCubeGrid grid)
		{
			this.myGrid = grid;
			Registrar.Add(grid, this);

			Log.DebugLog("Initialized");
		}

		private void AddRemoveConnection(AttachmentKind kind, AttachedGrid attached, bool add)
		{
			Attachments attach;
			if (!Connections.TryGetValue(attached, out attach))
			{
				if (add)
				{
					attach = new Attachments(myGrid, attached.myGrid);
					Connections.Add(attached, attach);
				}
				else
				{
					Log.AlwaysLog("cannot remove, no attachments of kind " + kind, Logger.severity.ERROR);
					return;
				}
			}

			if (add)
				attach.Add(kind);
			else
				attach.Remove(kind);
		}

		private bool IsGridAttached(AttachedGrid target, AttachmentKind allowedConnections, HashSet<AttachedGrid> searched)
		{
			if (!searched.Add(this))
				throw new Exception("AttachedGrid already searched");

			foreach (var gridAttach in Connections)
			{
				if ((gridAttach.Value.attachmentKinds & allowedConnections) == 0 || searched.Contains(gridAttach.Key))
					continue;

				if (gridAttach.Key == target || gridAttach.Key.IsGridAttached(target, allowedConnections, searched))
					return true;
			}

			return false;
		}

		private IEnumerable<IMyCubeGrid> Attached(AttachmentKind allowedConnections, HashSet<AttachedGrid> searched)
		{
			if (!searched.Add(this))
				throw new Exception("AttachedGrid already searched");

			foreach (var gridAttach in Connections)
			{
				if ((gridAttach.Value.attachmentKinds & allowedConnections) == 0 || searched.Contains(gridAttach.Key))
					continue;

				yield return gridAttach.Key.myGrid;

				foreach (var result in gridAttach.Key.Attached(allowedConnections, searched))
					yield return result;
			}
		}

	}
}
