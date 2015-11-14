using System;
using System.Collections.Generic;
using Sandbox.ModAPI;
using Ingame = Sandbox.ModAPI.Ingame;

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
				myLogger.debugLog("adding: " + kind, "Add()");

				ushort count;
				if (!dictionary.TryGetValue(kind, out count))
				{
					attachmentKinds |= kind;
					count = 0;
				}

				count++;
				myLogger.debugLog(kind + " count: " + count, "Add()");
				dictionary[kind] = count;
			}

			public void Remove(AttachmentKind kind)
			{
				myLogger.debugLog("removing: " + kind, "Remove()");

				ushort count = dictionary[kind];
				count--;
				if (count == 0)
				{
					attachmentKinds &= ~kind;
					dictionary.Remove(kind);
				}
				else
					dictionary[kind] = count;

				myLogger.debugLog(kind + " count: " + count, "Add()");
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

		private static FastResourceLock lock_search = new FastResourceLock();
		private static uint searchIdPool = 1;

		static AttachedGrid()
		{
			MyAPIGateway.Entities.OnCloseAll += Entities_OnCloseAll;
		}

		private static void Entities_OnCloseAll()
		{
			MyAPIGateway.Entities.OnCloseAll -= Entities_OnCloseAll;
			lock_search = null;
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

			using (lock_search.AcquireExclusiveUsing())
			{
				AttachedGrid attached1 = GetFor(grid0);
				if (attached1 == null)
					return false;
				AttachedGrid attached2 = GetFor(grid1);
				if (attached2 == null)
					return false;
				return attached1.IsGridAttached(attached2, allowedConnections, searchIdPool++);
			}
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
			using (lock_search.AcquireExclusiveUsing())
			{
				AttachedGrid attached = GetFor(startGrid);
				if (attached == null)
					return;
				attached.RunOnAttached(allowedConnections, runFunc, searchIdPool++);
			}
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

		internal static void AddRemoveConnection(AttachmentKind kind, Ingame.IMyCubeGrid grid1, Ingame.IMyCubeGrid grid2, bool add)
		{ AddRemoveConnection(kind, grid1 as IMyCubeGrid, grid2 as IMyCubeGrid, add); }

		internal static void AddRemoveConnection(AttachmentKind kind, IMyCubeGrid grid1, IMyCubeGrid grid2, bool add)
		{
			if (grid1 == grid2)
				return;

			AttachedGrid
				attached1 = GetFor(grid1),
				attached2 = GetFor(grid2);

			if (attached1 != null)
				attached1.AddRemoveConnection(kind, attached2, add);
			if (attached2 != null)
				attached2.AddRemoveConnection(kind, attached1, add);
		}

		private static AttachedGrid GetFor(IMyCubeGrid grid)
		{
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
		private readonly Dictionary<AttachedGrid, Attachments> Connections = new Dictionary<AttachedGrid, Attachments>();

		private readonly FastResourceLock lock_Connections = new FastResourceLock();
		private uint lastSearchId = 0;

		private AttachedGrid(IMyCubeGrid grid)
		{
			this.myLogger = new Logger("AttachedGrid", () => grid.DisplayName);
			this.myGrid = grid;
			Registrar.Add(grid, this);
		}

		private void AddRemoveConnection(AttachmentKind kind, AttachedGrid attached, bool add)
		{
			using (lock_Connections.AcquireExclusiveUsing())
			{
				Attachments attach;
				if (!Connections.TryGetValue(attached, out attach))
				{
					attach = new Attachments(myGrid, attached.myGrid);
					Connections.Add(attached, attach);
				}

				if (add)
					attach.Add(kind);
				else
					attach.Remove(kind);
			}
		}

		private bool IsGridAttached(AttachedGrid target, AttachmentKind allowedConnections, uint searchId)
		{
			myLogger.debugLog(lastSearchId == searchId, "Already searched! lastSearchId == searchId", "IsGridAttached()", Logger.severity.ERROR);
			lastSearchId = searchId;

			using (lock_Connections.AcquireSharedUsing())
				foreach (var gridAttachments in Connections)
				{
					if (searchId == gridAttachments.Key.lastSearchId)
						continue;

					if ((gridAttachments.Value.attachmentKinds & allowedConnections) == 0)
						continue;

					if (gridAttachments.Key == target)
						return true;
					if (gridAttachments.Key.IsGridAttached(target, allowedConnections, searchId))
						return true;
				}

			return false;
		}

		private void RunOnAttached(AttachmentKind allowedConnections, Func<IMyCubeGrid, bool> runFunc, uint searchId)
		{
			myLogger.debugLog(lastSearchId == searchId, "Already searched! lastSearchId == searchId", "GetAttached()", Logger.severity.ERROR);
			lastSearchId = searchId;

			using (lock_Connections.AcquireSharedUsing())
				foreach (var gridAttachments in Connections)
				{
					if (searchId == gridAttachments.Key.lastSearchId)
						continue;

					if ((gridAttachments.Value.attachmentKinds & allowedConnections) == 0)
						continue;

					if (runFunc(gridAttachments.Key.myGrid))
						return;

					gridAttachments.Key.RunOnAttached(allowedConnections, runFunc, searchId);
				}
		}

	}
}
