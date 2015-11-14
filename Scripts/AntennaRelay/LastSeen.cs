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
		private static readonly TimeSpan Recent = new TimeSpan(0, 0, 10);

		public readonly IMyEntity Entity;
		public readonly DateTime LastSeenAt;
		public readonly Vector3D LastKnownPosition;
		public readonly RadarInfo Info;
		public readonly Vector3 LastKnownVelocity;

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

			value_isValid = true;
		}

		public LastSeen(IMyEntity entity, UpdateTime times)
			: this(entity)
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

		public Vector3D predictPosition()
		{ return LastKnownPosition + LastKnownVelocity * (float)(DateTime.UtcNow - LastSeenAt).TotalSeconds; }

		public Vector3D predictPosition(out TimeSpan sinceLastSeen)
		{
			sinceLastSeen = DateTime.UtcNow - LastSeenAt;
			return LastKnownPosition + LastKnownVelocity * (float)sinceLastSeen.TotalSeconds;
		}

		private bool value_isValid;
		public bool IsValid
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
		{ return GetTimeSinceLastSeen() < Recent; }

		public bool isRecent_Broadcast()
		{ return (DateTime.UtcNow - LastBroadcast) < Recent; }

		public bool isRecent_Jam()
		{ return (DateTime.UtcNow - LastJam) < Recent; }

		public bool isRecent_Radar()
		{ return (DateTime.UtcNow - LastRadar) < Recent; }

		/// <summary>A time span object representing the difference between the current time and the last time this LastSeen was updated</summary>
		public TimeSpan GetTimeSinceLastSeen()
		{ return DateTime.UtcNow - LastSeenAt; }

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
