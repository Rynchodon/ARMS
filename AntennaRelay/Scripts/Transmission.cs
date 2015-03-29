#define LOG_ENABLED // remove on build

using System;
using System.Collections.Generic;
//using System.Linq;
//using System.Text;

using Sandbox.ModAPI;
using VRageMath;

namespace Rynchodon.AntennaRelay
{
	// must be immutable
	public class LastSeen //: Transmission
	{
		private static readonly TimeSpan MaximumLifetime = new TimeSpan(1, 0, 0); // one hour

		public readonly IMyEntity Entity;
		public readonly DateTime LastSeenAt;
		public readonly Vector3D LastKnownPosition;
		public readonly Vector3D LastKnownVelocity;
		public readonly bool EntityHasRadar;
		public readonly RadarInfo Info;

		/// <summary>
		/// 
		/// </summary>
		/// <param name="entity">asteroid, grid, or player, never block</param>
		/// <param name="knownName"></param>
		public LastSeen(IMyEntity entity, bool EntityHasRadar = false, RadarInfo info = null)
		{
			//(new Logger(null, "LastSeen")).log(Logger.severity.TRACE, ".ctor()", "entity = " + entity + ", entity name = " + entity.getBestName() + ", EntityHasRadar = " + EntityHasRadar);
			this.Entity = entity;
			this.LastSeenAt = DateTime.UtcNow;
			this.LastKnownPosition = entity.WorldAABB.Center;
			if (entity.Physics == null)
				this.LastKnownVelocity = Vector3D.Zero;
			else
				this.LastKnownVelocity = entity.Physics.LinearVelocity;
			this.EntityHasRadar = EntityHasRadar;
			this.Info = info;

			value_isValid = true;
		}

		private LastSeen(LastSeen first, LastSeen second)
		{
			this.EntityHasRadar = first.EntityHasRadar || second.EntityHasRadar;
			this.Info = RadarInfo.getNewer(first.Info, second.Info);

			LastSeen newer;
			if (first.isNewerThan(second))
				newer = first;
			else
				newer = second;

			this.Entity = newer.Entity;
			this.LastSeenAt = newer.LastSeenAt;
			this.LastKnownPosition = newer.LastKnownPosition;
			this.LastKnownVelocity = newer.LastKnownVelocity;

			value_isValid = true;
		}

		private bool isNewerThan(LastSeen other)
		{ return this.LastSeenAt.CompareTo(other.LastSeenAt) > 0; }

		/// <summary>
		/// If necissary, update toUpdate with this.
		/// </summary>
		/// <param name="toUpdate">LastSeen that may need an update</param>
		/// <returns>true iff an update was performed</returns>
		public bool update(ref LastSeen toUpdate)
		{
			if ((!this.EntityHasRadar && toUpdate.EntityHasRadar) ||
				(toUpdate.Info != null && (this.Info == null || this.Info.IsNewerThan(toUpdate.Info))) ||
				(this.isNewerThan(toUpdate)))
			{
				LastSeen param = toUpdate;
				toUpdate = new LastSeen(this, toUpdate);
				return true;
			}
			return false;
		}

		public Vector3D predictPosition(TimeSpan elapsedTime)
		{ return LastKnownPosition + LastKnownVelocity * elapsedTime.TotalSeconds; }

		public Vector3D predictPosition()
		{ return LastKnownPosition + LastKnownVelocity * (DateTime.UtcNow - LastSeenAt).TotalSeconds; }

		public Vector3D predictPosition(out TimeSpan sinceLastSeen)
		{
			sinceLastSeen = DateTime.UtcNow - LastSeenAt;
			return LastKnownPosition + LastKnownVelocity * sinceLastSeen.TotalSeconds;
		}

		private bool value_isValid;
		public bool isValid
		{
			get
			{
				if (value_isValid && (Entity == null || Entity.Closed || (DateTime.UtcNow - LastSeenAt).CompareTo(MaximumLifetime) > 0))
					value_isValid = false;
				return value_isValid;
			}
		}

		/// <summary>
		/// Use this instead of hard-coding.
		/// </summary>
		/// <returns></returns>
		public bool isRecent()
		{ return (DateTime.UtcNow - LastSeenAt).TotalSeconds < 10; }

		private static string ClassName = "LastSeen";
		private static Logger myLogger;
		[System.Diagnostics.Conditional("LOG_ENABLED")]
		private static void log(string toLog, string method = null, Logger.severity level = Logger.severity.DEBUG)
		{ alwaysLog(toLog, method, level); }
		private static  void alwaysLog(string toLog, string method = null, Logger.severity level = Logger.severity.DEBUG)
		{
			if (myLogger == null)
				myLogger = new Logger(null, ClassName);
			myLogger.log(level, method, toLog);
		}
	}

	public class RadarInfo
	{
		public readonly DateTime DetectedAt;
		public readonly float Volume;

		public string Pretty_Volume()
		{ return PrettySI.makePrettyCubic(Volume) + "m³"; }

		public RadarInfo(float volume)
		{
			this.DetectedAt = DateTime.UtcNow;
			this.Volume = volume;
		}

		public bool IsNewerThan(RadarInfo other)
		{ return this.DetectedAt.CompareTo(other.DetectedAt) > 0; }

		public static RadarInfo getNewer(RadarInfo first, RadarInfo second)
		{
			if (first == null)
				return second;
			if (second == null)
				return first;
			if (first.IsNewerThan(second))
				return first;
			return second;
		}
	}

	public class Message //: Transmission
	{
		private static readonly TimeSpan MaximumLifetime = new TimeSpan(1, 0, 0); // one hour

		public readonly string Content, SourceGridName, SourceBlockName;
		public readonly IMyCubeBlock DestCubeBlock, SourceCubeBlock;
		public readonly DateTime created;
		private readonly long destOwnerID;

		public Message(string Content, IMyCubeBlock DestCubeblock, IMyCubeBlock SourceCubeBlock, string SourceBlockName = null)
		{
			this.Content = Content;
			this.DestCubeBlock = DestCubeblock;

			this.SourceCubeBlock = SourceCubeBlock;
			this.SourceGridName = SourceCubeBlock.CubeGrid.DisplayName;
			if (SourceBlockName == null)
				this.SourceBlockName = SourceCubeBlock.DisplayNameText;
			else
				this.SourceBlockName = SourceBlockName;
			this.destOwnerID = DestCubeblock.OwnerId;

			created = DateTime.UtcNow;
		}

		public static List<Message> buildMessages(string Content, string DestGridName, string DestBlockName, IMyCubeBlock SourceCubeBlock, string SourceBlockName = null)
		{
			List<Message> result = new List<Message>();
			log("testing " + ProgrammableBlock.registry.Count + " programmable blocks", "buildMessages()", Logger.severity.TRACE);
			foreach (IMyCubeBlock DestBlock in ProgrammableBlock.registry.Keys)
			{
				log("testing "+DestBlock.gridBlockName(), "buildMessages()", Logger.severity.TRACE);
				//IMyCubeBlock DestBlock = Pair.Key;
				IMyCubeGrid DestGrid = DestBlock.CubeGrid;
				if (DestGrid.DisplayName.looseContains(DestGridName) // grid matches
					&& DestBlock.DisplayNameText.looseContains(DestBlockName)) // block matches
					if (SourceCubeBlock.canControlBlock(DestBlock)) // can control
						result.Add(new Message(Content, DestBlock, SourceCubeBlock, SourceBlockName));
			}
			return result;
		}

		private bool value_isValid = true;
		/// <summary>
		/// can only be set to false, once invalid always invalid
		/// </summary>
		public bool isValid
		{
			get
			{
				if (value_isValid && (DestCubeBlock == null 
					|| DestCubeBlock.Closed
					|| destOwnerID != DestCubeBlock.OwnerId // dest owner changed
					|| (DateTime.UtcNow - created).CompareTo(MaximumLifetime) > 0)) // expired
					value_isValid = false;
				return value_isValid;
			}
			set
			{
				if (value == false)
					value_isValid = false;
			}
		}


		private static Logger myLogger;
		[System.Diagnostics.Conditional("LOG_ENABLED")]
		private static void log(string toLog, string method = null, Logger.severity level = Logger.severity.DEBUG)
		{ alwaysLog(toLog, method, level); }
		private static void alwaysLog(string toLog, string method = null, Logger.severity level = Logger.severity.DEBUG)
		{
			if (myLogger == null)
				myLogger = new Logger(null, "Message");
			myLogger.log(level, method, toLog);
		}
	}
}
