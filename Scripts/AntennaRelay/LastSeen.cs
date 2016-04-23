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
			public RadarInfo.Builder_RadarInfo Info;
		}

		[Flags]
		public enum UpdateTime : byte
		{
			None = 1 << 0,
			Broadcasting = 1 << 1,
			HasRadar = 1 << 3,
			HasJammer = 1 << 4
		}

		public enum EntityType : byte { None, Grid, Character, Missile, Unknown }

		private static readonly TimeSpan MaximumLifetime = new TimeSpan(24, 0, 0);
		public static readonly TimeSpan Recent = new TimeSpan(0, 0, 10);

		private EntityType m_type;

		public readonly IMyEntity Entity;
		public readonly TimeSpan LastSeenAt;
		public readonly Vector3D LastKnownPosition;
		public readonly RadarInfo Info;
		public readonly Vector3 LastKnownVelocity;

		/// <summary>The last time Entity was broadcasting</summary>
		public readonly TimeSpan LastBroadcast = new TimeSpan(long.MinValue / 2);
		/// <summary>The last time Entity was using a radar</summary>
		public readonly TimeSpan LastRadar = new TimeSpan(long.MinValue / 2);
		/// <summary>The last time Entity was using a jammer</summary>
		public readonly TimeSpan LastJam = new TimeSpan(long.MinValue / 2);

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
					else if (Entity.ToString().StartsWith("MyMissile"))
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
			this.Info = RadarInfo.getNewer(first.Info, second.Info);

			LastSeen newer = first.isNewerThan(second) ? first : second;

			this.Entity = newer.Entity;
			this.LastSeenAt = newer.LastSeenAt;
			this.LastKnownPosition = newer.LastKnownPosition;
			this.LastKnownVelocity = newer.LastKnownVelocity;

			this.LastBroadcast = first.LastBroadcast.CompareTo(second.LastBroadcast) > 0 ? first.LastBroadcast : second.LastBroadcast;
			this.LastRadar = first.LastRadar.CompareTo(second.LastBroadcast) > 0 ? first.LastRadar : second.LastRadar;
			this.LastJam = first.LastJam.CompareTo(second.LastBroadcast) > 0 ? first.LastJam : second.LastJam;

			if (first.m_type == EntityType.None)
				this.m_type = second.m_type;
			else
				this.m_type = first.m_type;

			value_isValid = true;
		}

		public LastSeen(IMyEntity entity, UpdateTime times)
			: this(entity)
		{
			if ((times & UpdateTime.Broadcasting) != 0)
				this.LastBroadcast = Globals.ElapsedTime;
			if ((times & UpdateTime.HasJammer) != 0)
				this.LastJam = Globals.ElapsedTime;
			if ((times & UpdateTime.HasRadar) != 0)
				this.LastRadar = Globals.ElapsedTime;
		}

		/// <summary>
		/// Creates a LastSeen for an entity that was detected with an active radar scan.
		/// </summary>
		public LastSeen(IMyEntity entity, UpdateTime times, RadarInfo info)
			: this(entity, times)
		{
			this.Info = info;
		}

		public LastSeen(Builder_LastSeen builder)
		{
			if (!MyAPIGateway.Entities.TryGetEntityById(builder.EntityId, out this.Entity))
			{
				(new Logger(GetType().Name)).alwaysLog("Entity does not exist in world: " + builder.EntityId, "LastSeen()", Logger.severity.WARNING);
				return;
			}
			this.LastSeenAt = builder.LastSeenAt.ToTimeSpan();
			this.LastKnownPosition = builder.LastKnownPosition;
			this.LastKnownVelocity = builder.LastKnownVelocity;
			this.LastBroadcast = builder.LastBroadcast.ToTimeSpan();
			this.LastRadar = builder.LastRadar.ToTimeSpan();
			this.LastJam = builder.LastJam.ToTimeSpan();
			if (builder.Info != null)
				this.Info = new RadarInfo(builder.Info);
			this.value_isValid = true;
		}

		private bool isNewerThan(LastSeen other)
		{ return this.LastSeenAt.CompareTo(other.LastSeenAt) > 0; }

		private bool anyNewer(LastSeen other)
		{
			return this.LastSeenAt.CompareTo(other.LastSeenAt) > 0
				|| this.LastBroadcast.CompareTo(other.LastBroadcast) > 0
				|| this.LastJam.CompareTo(other.LastJam) > 0
				|| this.LastRadar.CompareTo(other.LastRadar) > 0;
		}

		/// <summary>
		/// If necissary, update toUpdate with this.
		/// </summary>
		/// <param name="toUpdate">LastSeen that may need an update</param>
		/// <returns>true iff an update was performed</returns>
		public bool update(ref LastSeen toUpdate)
		{
			if (this.anyNewer(toUpdate)
				|| (this.Info != null && (toUpdate.Info == null || this.Info.IsNewerThan(toUpdate.Info))))
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
		{ return GetTimeSinceLastSeen() < Recent; }

		/// <summary>True if the entity was seen broadcasting recently.</summary>
		public bool isRecent_Broadcast()
		{ return (Globals.ElapsedTime - LastBroadcast) < Recent; }

		/// <summary>True if a radar jammer was seen on the entity recently.</summary>
		public bool isRecent_Jam()
		{ return (Globals.ElapsedTime - LastJam) < Recent; }

		/// <summary>True if a radar was seen on the entity recently.</summary>
		public bool isRecent_Radar()
		{ return (Globals.ElapsedTime - LastRadar) < Recent; }

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
			return !Entity.MarkedForClose && sinceLastSeen < Recent ? Entity.GetCentre()
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
			var blocks = cache.GetBlocksOfType(typeof(MyObjectBuilder_Beacon));
			if (blocks != null)
				foreach (var b in blocks)
					if (b.IsWorking)
					{
						float radius = ((Sandbox.ModAPI.Ingame.IMyBeacon)b).Radius;
						if (radius > longestRange)
						{
							longestRange = radius;
							name = b.DisplayNameText;
						}
					}

			blocks = cache.GetBlocksOfType(typeof(MyObjectBuilder_RadioAntenna));
			if (blocks != null)
				foreach (var ra in blocks)
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
				 LastJam = new SerializableGameTime(LastJam)
			 };
			if (Info != null)
				result.Info = Info.GetBuilder();
			return result;
		}

	}

	public class RadarInfo
	{

		[Serializable]
		public class Builder_RadarInfo
		{
			public SerializableGameTime DetectedAt;
			public float Volume;
		}

		public readonly TimeSpan DetectedAt;
		public readonly float Volume;

		public string Pretty_Volume()
		{ return PrettySI.makePrettyCubic(Volume) + "m³"; }

		public RadarInfo(float volume)
		{
			this.DetectedAt = Globals.ElapsedTime;
			this.Volume = volume;
		}

		public RadarInfo(Builder_RadarInfo builder)
		{
			this.DetectedAt = builder.DetectedAt.ToTimeSpan();
			this.Volume = builder.Volume;
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

		public bool IsRecent()
		{
			return (Globals.ElapsedTime - DetectedAt) < LastSeen.Recent;
		}

		public Builder_RadarInfo GetBuilder()
		{
			return new Builder_RadarInfo()
			{
				DetectedAt = new SerializableGameTime(DetectedAt),
				Volume = Volume
			};
		}

	}
}
