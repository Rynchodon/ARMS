using System;
using System.Collections.Generic;
using Sandbox.ModAPI;
using VRage.Collections;
using VRage.Game.ModAPI;
using VRage.ObjectBuilders;
using Ingame = VRage.Game.ModAPI.Ingame;

namespace Rynchodon.Attached
{
	public class AttachedGrid
	{
		private class Attachments
		{

			private readonly Logger myLogger;

			private readonly Dictionary<AttachmentKind, ushort> dictionary = new Dictionary<AttachmentKind, ushort>();

			public AttachmentKind attachmentKinds { get; private set; }

			public Attachments(IMyCubeGrid grid0, IMyCubeGrid grid1)
			{
				myLogger = new Logger("Attachments", () => grid0.DisplayName, () => grid1.DisplayName);
			}

			public void Add(AttachmentKind kind)
			{
				myLogger.debugLog("adding: " + kind);

				ushort count;
				if (!dictionary.TryGetValue(kind, out count))
				{
					attachmentKinds |= kind;
					count = 0;
				}

				count++;
				myLogger.debugLog(kind + " count: " + count);
				dictionary[kind] = count;
			}

			public void Remove(AttachmentKind kind)
			{
				myLogger.debugLog("removing: " + kind);

				ushort count = dictionary[kind];
				count--;
				if (count == 0)
				{
					attachmentKinds &= ~kind;
					dictionary.Remove(kind);
				}
				else
					dictionary[kind] = count;

				myLogger.debugLog(kind + " count: " + count);
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

		private class StaticVariables
		{
			public Logger s_logger = new Logger("AttachedGrid");
			public MyConcurrentPool<HashSet<AttachedGrid>> searchSet = new MyConcurrentPool<HashSet<AttachedGrid>>();
			public MyConcurrentPool<HashSet<SerializableDefinitionId>> foundBlockIds = new MyConcurrentPool<HashSet<SerializableDefinitionId>>();
		}

		private static StaticVariables Static = new StaticVariables();

		static AttachedGrid()
		{
			MyAPIGateway.Entities.OnCloseAll += Entities_OnCloseAll;
		}

		private static void Entities_OnCloseAll()
		{
			MyAPIGateway.Entities.OnCloseAll -= Entities_OnCloseAll;
			Static = null;
		}

		/// <summary>
		/// Determines if two grids are attached.
		/// </summary>
		/// <param name="grid0">The starting grid.</param>
		/// <param name="grid1">The grid to search for.</param>
		/// <param name="allowedConnections">The types of connections allowed between grids.</param>
		/// <returns>True iff the grids are attached.</returns>
		public static bool IsGridAttached(IMyCubeGrid grid0, IMyCubeGrid grid1, AttachmentKind allowedConnections)
		{
			if (grid0 == grid1)
				return true;

			AttachedGrid attached1 = GetFor(grid0);
			if (attached1 == null)
				return false;
			AttachedGrid attached2 = GetFor(grid1);
			if (attached2 == null)
				return false;

			HashSet<AttachedGrid> search = Static.searchSet.Get();
			bool result = attached1.IsGridAttached(attached2, allowedConnections, search);
			search.Clear();
			Static.searchSet.Return(search);
			return result;
		}

		/// <summary>
		/// Runs a function on all the attached grids.
		/// </summary>
		/// <param name="startGrid">The starting grid/</param>
		/// <param name="allowedConnections">The types of connections allowed between grids.</param>
		/// <param name="runFunc">Func that runs on attached grids, if it returns true, short-circuit</param>
		/// <param name="runOnStartGrid">Iff true, action will be run on startGrid.</param>
		public static void RunOnAttached(IMyCubeGrid startGrid, AttachmentKind allowedConnections, Func<IMyCubeGrid, bool> runFunc, bool runOnStartGrid = false)
		{
			if (runOnStartGrid && runFunc(startGrid))
				return;

			AttachedGrid attached = GetFor(startGrid);
			if (attached == null)
				return;

			HashSet<AttachedGrid> search = Static.searchSet.Get();
			attached.RunOnAttached(allowedConnections, runFunc, search);
			search.Clear();
			Static.searchSet.Return(search);
		}

		/// <summary>
		/// Runs a function on all the blocks in all the attached grids.
		/// </summary>
		/// <param name="startGrid">Grid to start at.</param>
		/// <param name="allowedConnections">The types of connections allowed between grids.</param>
		/// <param name="runFunc">Func that runs on blocks of attached grids, if it returns true, short-circuit</param>
		/// <param name="runOnStartGrid">Iff true, action will be run on blocks of startGrid.</param>
		public static void RunOnAttachedBlock(IMyCubeGrid startGrid, AttachmentKind allowedConnections, Func<IMySlimBlock, bool> runFunc, bool runOnStartGrid = false)
		{
			List<IMySlimBlock> dummy = new List<IMySlimBlock>();
			bool terminated = false;

			Func<IMyCubeGrid, bool> runOnGrid = grid => {
				grid.GetBlocks_Safe(dummy, slim => {
					if (!terminated)
						terminated = runFunc(slim);
					return false;
				});
				return terminated;
			};

			RunOnAttached(startGrid, allowedConnections, runOnGrid, runOnStartGrid);
		}

		public static IEnumerable<IMyCubeGrid> AttachedGrids(IMyCubeGrid startGrid, AttachmentKind allowedConnections, bool runOnStartGrid)
		{
			if (runOnStartGrid)
				yield return startGrid;

			AttachedGrid attached = GetFor(startGrid);
			if (attached == null)
				yield break;

			HashSet<AttachedGrid> search = Static.searchSet.Get();
			try
			{
				foreach (IMyCubeGrid grid in attached.Attached(allowedConnections, search))
					yield return grid;
			}
			finally
			{
				search.Clear();
				Static.searchSet.Return(search);
			}
		}

		public static IEnumerable<IMyCubeBlock> AttachedCubeBlocks(IMyCubeGrid startGrid, AttachmentKind allowedConnections, bool runOnStartGrid)
		{
			if (runOnStartGrid)
				foreach (IMyCubeBlock block in CubeGridCache.AllCubeBlocks(startGrid))
					yield return block;

			AttachedGrid attached = GetFor(startGrid);
			if (attached == null)
				yield break;

			HashSet<AttachedGrid> search = Static.searchSet.Get();
			try
			{
				foreach (IMyCubeGrid grid in attached.Attached(allowedConnections, search))
					foreach (IMyCubeBlock block in CubeGridCache.AllCubeBlocks(grid))
						yield return block;
			}
			finally
			{
				search.Clear();
				Static.searchSet.Return(search);
			}
		}

		public static IEnumerable<IMyCubeBlock> OneOfEachAttachedCubeBlock(IMyCubeGrid startGrid, AttachmentKind allowedConnections, bool runOnStartGrid)
		{
			HashSet<SerializableDefinitionId> foundTypes = Static.foundBlockIds.Get();
			HashSet<AttachedGrid> search = Static.searchSet.Get();
			try
			{
				if (runOnStartGrid)
					foreach (IMyCubeBlock block in CubeGridCache.OneOfEachCubeBlock(startGrid))
						if (foundTypes.Add(block.BlockDefinition))
							yield return block;

				AttachedGrid attached = GetFor(startGrid);
				if (attached == null)
					yield break;

				foreach (IMyCubeGrid grid in attached.Attached(allowedConnections, search))
					foreach (IMyCubeBlock block in CubeGridCache.OneOfEachCubeBlock(grid))
						if (foundTypes.Add(block.BlockDefinition))
							yield return block;
			}
			finally
			{
				foundTypes.Clear();
				Static.foundBlockIds.Return(foundTypes);
				search.Clear();
				Static.searchSet.Return(search);
			}
		}

		internal static void AddRemoveConnection(AttachmentKind kind, IMyCubeGrid grid1, Ingame.IMyCubeGrid grid2, bool add)
		{ AddRemoveConnection(kind, grid1 as IMyCubeGrid, grid2 as IMyCubeGrid, add); }

		internal static void AddRemoveConnection(AttachmentKind kind, IMyCubeGrid grid1, IMyCubeGrid grid2, bool add)
		{
			if (grid1 == grid2 || Static == null)
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

			AttachedGrid attached;
			if (!Registrar.TryGetValue(grid.EntityId, out attached))
			{
				if (grid.Closed)
					return null;
				attached = new AttachedGrid(grid);
			}

			return attached;
		}

		private readonly Logger myLogger;
		private readonly IMyCubeGrid myGrid;

		/// <summary>
		/// The grid connected to, the types of connection, the number of connections.
		/// Some connections are counted twice (this reduces code).
		/// </summary>
		private readonly LockedDictionary<AttachedGrid, Attachments> Connections = new LockedDictionary<AttachedGrid, Attachments>();

		private AttachedGrid(IMyCubeGrid grid)
		{
			this.myLogger = new Logger("AttachedGrid", () => grid.DisplayName);
			this.myGrid = grid;
			Registrar.Add(grid, this);

			myLogger.debugLog("Initialized");
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
					myLogger.alwaysLog("cannot remove, no attachments of kind " + kind, Logger.severity.ERROR);
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

			foreach (var gridAttach in Connections.GetEnumerator())
			{
				if ((gridAttach.Value.attachmentKinds & allowedConnections) == 0 || searched.Contains(gridAttach.Key))
					continue;

				if (gridAttach.Key == target || gridAttach.Key.IsGridAttached(target, allowedConnections, searched))
					return true;
			}

			return false;
		}

		private bool RunOnAttached(AttachmentKind allowedConnections, Func<IMyCubeGrid, bool> runFunc, HashSet<AttachedGrid> searched)
		{
			if (!searched.Add(this))
				throw new Exception("AttachedGrid already searched");

			foreach (var gridAttach in Connections.GetEnumerator())
			{
				if ((gridAttach.Value.attachmentKinds & allowedConnections) == 0 || searched.Contains(gridAttach.Key))
					continue;

				if (runFunc(gridAttach.Key.myGrid) || gridAttach.Key.RunOnAttached(allowedConnections, runFunc, searched))
					return true;
			}

			return false;
		}

		private IEnumerable<IMyCubeGrid> Attached(AttachmentKind allowedConnections, HashSet<AttachedGrid> searched)
		{
			if (!searched.Add(this))
				throw new Exception("AttachedGrid already searched");

			foreach (var gridAttach in Connections.GetEnumerator())
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
