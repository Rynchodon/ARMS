using System;
using System.Xml.Serialization;
using Rynchodon.Update;
using Sandbox.Common.ObjectBuilders;
using Sandbox.ModAPI;
using VRage.Game.ModAPI;
using VRage.ModAPI;
using VRageMath;

namespace Rynchodon.AntennaRelay
{
	// must be immutable
	public class LastSeen
	{

		[Serializable]
		public class Builder_LastSeen
		{
			[XmlAttribute]
			public long EntityId;
			public Vector3D LastKnownPosition;
			public Vector3 LastKnownVelocity;
			public SerializableGameTime LastSeenAt, LastBroadcast, LastRadar, LastJam;
			public Builder_RadarInfo Info;
		}

		[Serializable]
		public class Builder_RadarInfo
		{
			public SerializableGameTime DetectedAt;
			public float Volume;
		}

		[Flags]
		public enum DetectedBy : byte
		{
			None = 0 << 0,
			/// <summary>Entity was broadcasting its position</summary>
			Broadcasting = 1 << 1,
			/// <summary>Entity's radar was detected</summary>
			HasRadar = 1 << 2,
			/// <summary>Entity's jammer was detected</summary>
			HasJammer = 1 << 3,
			/// <summary>Entity was detected by radar</summary>
			ByRadar = 1 << 4
		}

		public enum EntityType : byte { None, Grid, Character, Missile, Unknown }

		public const long TicksPerTenthSecond = TimeSpan.TicksPerSecond / 10, RecentTicks = TimeSpan.TicksPerSecond * 2;

		public static readonly TimeSpan MaximumLifetime = new TimeSpan(24, 0, 0), RecentSpan = new TimeSpan(RecentTicks);

		public struct OlderBy
		{
			public static readonly OlderBy MinAge = new OlderBy() { RawValue = 255 },
				MaxAge = new OlderBy() { RawValue = 0 };

			public byte RawValue;

			public OlderBy(long baseTicks, long myTicks)
			{
				this.RawValue = (byte)(255 - Math.Min((baseTicks - myTicks) / TicksPerTenthSecond, 255));
			}

			public long SpanTicks(long baseTicks)
			{
				return baseTicks - (255 - RawValue) * TicksPerTenthSecond;
			}

			public bool IsRecent(long baseTicks)
			{
				return Globals.ElapsedTimeTicks - SpanTicks(baseTicks) < RecentTicks;
			}
		}

		/// <summary>
		/// Information available when an entity has been scanned by radar or is sending data.
		/// </summary>
		public struct RadarInfo
		{
			public static float GetVolume(IMyEntity entity)
			{
				IMyCubeGrid grid = entity as IMyCubeGrid;
				if (grid != null)
				{
					CubeGridCache cache = CubeGridCache.GetFor(grid);
					if (cache != null)
						return cache.CellCount * grid.GridSize * grid.GridSize * grid.GridSize;
				}
				return entity.LocalAABB.Volume();
			}

			public readonly LastSeen.OlderBy DetectedAt;
			public readonly float Volume;

			public string Pretty_Volume()
			{ return PrettySI.makePrettyCubic(Volume) + "m³"; }

			public RadarInfo(float volume)
			{
				this.DetectedAt = OlderBy.MinAge;
				this.Volume = volume;
			}

			public RadarInfo(IMyEntity grid) :
				this(GetVolume(grid)) { }

			public RadarInfo(TimeSpan lastSeen, Builder_RadarInfo builder)
			{
				this.DetectedAt = new LastSeen.OlderBy(lastSeen.Ticks, builder.DetectedAt.ToTicks());
				this.Volume = builder.Volume;
			}

			public bool IsRecent(long baseTicks)
			{
				return DetectedAt.IsRecent(baseTicks);
			}

			public Builder_RadarInfo GetBuilder(long baseTicks)
			{
				return new Builder_RadarInfo()
				{
					DetectedAt = new SerializableGameTime(DetectedAt.SpanTicks(baseTicks)),
					Volume = Volume
				};
			}
		}

		private EntityType m_type;

		public readonly IMyEntity Entity;
		public readonly TimeSpan LastSeenAt;
		public readonly Vector3D LastKnownPosition;
		public readonly RadarInfo Info;
		public readonly Vector3 LastKnownVelocity;

		private long LastBroadcast
		{ get { return m_lastBroadcast.SpanTicks(LastSeenAt.Ticks); } }

		private long LastJam
		{ get { return m_lastJam.SpanTicks(LastSeenAt.Ticks); } }

		private long LastRadar
		{ get { return m_lastRadar.SpanTicks(LastSeenAt.Ticks); } }

		private OlderBy m_lastBroadcast, m_lastJam, m_lastRadar;

		public EntityType Type
		{
			get
			{
				if (m_type == EntityType.None)
				{
					if (Entity is IMyCubeGrid)
						m_type = EntityType.Grid;
					else if (Entity is IMyCharacter)
						m_type = EntityType.Character;
					else if (Entity.IsMissile())
						m_type = EntityType.Missile;
					else
						m_type = EntityType.Unknown;
				}
				return m_type;
			}
		}

		private LastSeen(IMyEntity entity)
		{
			this.Entity = entity;
			this.LastSeenAt = Globals.ElapsedTime;
			this.LastKnownPosition = entity.GetCentre();
			if (entity.Physics == null)
				this.LastKnownVelocity = Vector3D.Zero;
			else
				this.LastKnownVelocity = entity.Physics.LinearVelocity;

			value_isValid = true;
		}

		private LastSeen(LastSeen first, LastSeen second)
		{
			this.Info = first.RadarInfoTicks() > second.RadarInfoTicks() ? first.Info : second.Info;

			LastSeen newer, older;
			if (first.LastSeenAt > second.LastSeenAt)
			{
				newer = first;
				older = second;
			}
			else
			{
				newer = second;
				older = first;
			}

			this.Entity = newer.Entity;
			this.LastSeenAt = newer.LastSeenAt;
			this.LastKnownPosition = newer.LastKnownPosition;
			this.LastKnownVelocity = newer.LastKnownVelocity;

			this.m_lastBroadcast = new OlderBy(this.LastSeenAt.Ticks, Math.Max(first.LastBroadcast, second.LastBroadcast));
			this.m_lastJam = new OlderBy(this.LastSeenAt.Ticks, Math.Max(first.LastJam, second.LastJam));
			this.m_lastRadar = new OlderBy(this.LastSeenAt.Ticks, Math.Max(first.LastRadar, second.LastRadar));

			if (first.m_type == EntityType.None)
				this.m_type = second.m_type;
			else
				this.m_type = first.m_type;

			value_isValid = true;
		}

		public LastSeen(IMyEntity entity, DetectedBy times)
			: this(entity)
		{
			if ((times & DetectedBy.Broadcasting) != 0)
				this.m_lastBroadcast = OlderBy.MinAge;
			if ((times & DetectedBy.HasJammer) != 0)
				this.m_lastRadar = OlderBy.MinAge;
			if ((times & DetectedBy.HasRadar) != 0)
				this.m_lastJam = OlderBy.MinAge;
		}

		/// <summary>
		/// Creates a LastSeen for an entity that was detected with an active radar scan.
		/// </summary>
		public LastSeen(IMyEntity entity, DetectedBy times, RadarInfo info)
			: this(entity, times)
		{
			this.Info = info;
		}

		public LastSeen(Builder_LastSeen builder)
		{
			if (!MyAPIGateway.Entities.TryGetEntityById(builder.EntityId, out this.Entity))
			{
				//Logger.AlwaysLog("Entity does not exist in world: " + builder.EntityId, Logger.severity.WARNING);
				return;
			}
			this.LastSeenAt = builder.LastSeenAt.ToTimeSpan();
			this.LastKnownPosition = builder.LastKnownPosition;
			this.LastKnownVelocity = builder.LastKnownVelocity;
			this.m_lastBroadcast = new OlderBy(this.LastSeenAt.Ticks, builder.LastBroadcast.ToTicks());
			this.m_lastJam = new OlderBy(this.LastSeenAt.Ticks, builder.LastJam.ToTicks());
			this.m_lastRadar = new OlderBy(this.LastSeenAt.Ticks, builder.LastRadar.ToTicks());
			if (builder.Info != null)
				this.Info = new RadarInfo(this.LastSeenAt, builder.Info);
			this.value_isValid = true;
		}

		/// <summary>
		/// If necissary, update toUpdate with this.
		/// </summary>
		/// <param name="toUpdate">LastSeen that may need an update</param>
		/// <returns>true iff an update was performed</returns>
		public bool update(ref LastSeen toUpdate)
		{
			if (this.LastSeenAt > toUpdate.LastSeenAt ||
				this.LastBroadcast > toUpdate.LastBroadcast ||
				this.LastJam > toUpdate.LastJam ||
				this.LastRadar > toUpdate.LastRadar ||
				this.RadarInfoTicks() > toUpdate.RadarInfoTicks())
			{
				toUpdate = new LastSeen(this, toUpdate);
				return true;
			}
			return false;
		}

		public Vector3D predictPosition()
		{ return LastKnownPosition + LastKnownVelocity * (float)(Globals.ElapsedTime - LastSeenAt).TotalSeconds; }

		public Vector3D predictPosition(out TimeSpan sinceLastSeen)
		{
			sinceLastSeen = Globals.ElapsedTime - LastSeenAt;
			return LastKnownPosition + LastKnownVelocity * (float)sinceLastSeen.TotalSeconds;
		}

		private bool value_isValid;
		public bool IsValid
		{
			get
			{
				if (value_isValid && (Entity == null || Entity.Closed || (Globals.ElapsedTime - LastSeenAt).CompareTo(MaximumLifetime) > 0))
					value_isValid = false;
				return value_isValid;
			}
		}

		/// <summary>True if the entity was detected recently</summary>
		public bool isRecent()
		{ return GetTimeSinceLastSeen() < RecentSpan; }

		/// <summary>True if the entity was seen broadcasting recently.</summary>
		public bool isRecent_Broadcast()
		{ return m_lastBroadcast.IsRecent(LastSeenAt.Ticks); }

		/// <summary>True if a radar jammer was seen on the entity recently.</summary>
		public bool isRecent_Jam()
		{ return m_lastJam.IsRecent(LastSeenAt.Ticks); }

		/// <summary>True if a radar was seen on the entity recently.</summary>
		public bool isRecent_Radar()
		{ return m_lastRadar.IsRecent(LastSeenAt.Ticks); }

		/// <summary>A time span object representing the difference between the current time and the last time this LastSeen was updated</summary>
		public TimeSpan GetTimeSinceLastSeen()
		{ return Globals.ElapsedTime - LastSeenAt; }

		/// <summary>
		/// If Entity has been seen recently, gets its position. Otherwise, predicts its position.
		/// </summary>
		/// <returns>The position of the Entity or its predicted position.</returns>
		public Vector3D GetPosition()
		{
			TimeSpan sinceLastSeen = GetTimeSinceLastSeen();
			return !Entity.MarkedForClose && sinceLastSeen < RecentSpan ? Entity.GetCentre()
				: LastKnownPosition + LastKnownVelocity * (float)sinceLastSeen.TotalSeconds;
		}

		/// <summary>
		/// If Entity has been seen recently, gets its current velocity. Otherwise, returns LastKnownVelocity
		/// </summary>
		public Vector3 GetLinearVelocity()
		{
			return !Entity.MarkedForClose && isRecent() ? Entity.Physics.LinearVelocity
				: (Vector3)LastKnownVelocity;
		}

		public string HostileName()
		{
			switch (Type)
			{
				case EntityType.Character:
					if (string.IsNullOrEmpty(Entity.DisplayName))
						return "Creature";
					return Entity.DisplayName;
				case EntityType.Grid:
					break;
				default:
					return Type.ToString();
			}

			if (!isRecent_Broadcast())
				return ((IMyCubeGrid)Entity).SimpleName();

			CubeGridCache cache = CubeGridCache.GetFor((IMyCubeGrid)Entity);
			if (cache == null)
				return "Error";

			float longestRange = float.MinValue;
			string name = null;
			foreach (IMyCubeBlock b in cache.BlocksOfType(typeof(MyObjectBuilder_Beacon)))
				if (b.IsWorking)
				{
					float radius = ((Sandbox.ModAPI.Ingame.IMyBeacon)b).Radius;
					if (radius > longestRange)
					{
						longestRange = radius;
						name = b.DisplayNameText;
					}
				}

			foreach (IMyCubeBlock ra in cache.BlocksOfType(typeof(MyObjectBuilder_RadioAntenna)))
				if (ra.IsWorking)
				{
					Sandbox.ModAPI.Ingame.IMyRadioAntenna asRA = (Sandbox.ModAPI.Ingame.IMyRadioAntenna)ra;
					if (asRA.IsBroadcasting && asRA.Radius > longestRange)
					{
						longestRange = asRA.Radius;
						name = ra.DisplayNameText;
					}
				}

			return name ?? ((IMyCubeGrid)Entity).SimpleName();
		}

		public Builder_LastSeen GetBuilder()
		{
			Builder_LastSeen result = new Builder_LastSeen()
			 {
				 EntityId = Entity.EntityId,
				 LastSeenAt = new SerializableGameTime(LastSeenAt),
				 LastKnownPosition = LastKnownPosition,
				 LastKnownVelocity = LastKnownVelocity,
				 LastBroadcast = new SerializableGameTime(LastBroadcast),
				 LastRadar = new SerializableGameTime(LastRadar),
				 LastJam = new SerializableGameTime(LastJam),
				 Info = Info.GetBuilder(LastSeenAt.Ticks)
			 };
			return result;
		}

		public bool RadarInfoIsRecent()
		{
			return Info.IsRecent(LastSeenAt.Ticks);
		}

		public TimeSpan RadarInfoTime()
		{
			return new TimeSpan(Info.DetectedAt.SpanTicks(LastSeenAt.Ticks));
		}

		public long RadarInfoTicks()
		{
			return Info.DetectedAt.SpanTicks(LastSeenAt.Ticks);
		}

	}
}
