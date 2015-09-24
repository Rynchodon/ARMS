using System;
using VRage.ModAPI;
using VRageMath;

namespace Rynchodon.AntennaRelay
{
	// must be immutable
	public class LastSeen
	{
		[Flags]
		public enum UpdateTime : byte
		{
			None = 1 << 0,
			Broadcasting = 1 << 1,
			HasRadar = 1 << 3,
			HasJammer = 1 << 4
		}

		private static readonly TimeSpan MaximumLifetime = new TimeSpan(1, 0, 0);

		public readonly IMyEntity Entity;
		public readonly DateTime LastSeenAt;
		public readonly Vector3D LastKnownPosition;
		public readonly Vector3D LastKnownVelocity;
		public readonly RadarInfo Info;

		/// <summary>The last time Entity was broadcasting</summary>
		public readonly DateTime LastBroadcast;
		/// <summary>The last time Entity was using a radar</summary>
		public readonly DateTime LastRadar;
		/// <summary>The last time Entity was using a jammer</summary>
		public readonly DateTime LastJam;

		private LastSeen(IMyEntity entity)
		{
			this.Entity = entity;
			this.LastSeenAt = DateTime.UtcNow;
			this.LastKnownPosition = entity.WorldAABB.Center;
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

			value_isValid = true;
		}

		public LastSeen(IMyEntity entity, UpdateTime times)
			:	this (entity)
		{
			if ((times & UpdateTime.Broadcasting) != 0)
				this.LastBroadcast = DateTime.UtcNow;
			if ((times & UpdateTime.HasJammer) != 0)
				this.LastJam = DateTime.UtcNow; 
			if ((times & UpdateTime.HasRadar) != 0)
				this.LastRadar = DateTime.UtcNow;
		}

		/// <summary>
		/// Creates a LastSeen for an entity that was detected with an active radar scan.
		/// </summary>
		public LastSeen(IMyEntity entity, UpdateTime times, RadarInfo info)
			: this(entity, times)
		{
			this.Info = info;
		}

		private bool isNewerThan(LastSeen other)
		{ return this.LastSeenAt.CompareTo(other.LastSeenAt) > 0; }

		private bool anyNewer(LastSeen other)
		{
			return this.LastBroadcast.CompareTo(other.LastBroadcast) > 0
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
		public bool isRecent()
		{ return (DateTime.UtcNow - LastSeenAt).TotalSeconds < 10; }

		public bool isRecent_Broadcast()
		{ return (DateTime.UtcNow - LastBroadcast).TotalSeconds < 10; }

		public bool isRecent_Jam()
		{ return (DateTime.UtcNow - LastJam).TotalSeconds < 10; }

		public bool isRecent_Radar()
		{ return (DateTime.UtcNow - LastRadar).TotalSeconds < 10; }
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
}
