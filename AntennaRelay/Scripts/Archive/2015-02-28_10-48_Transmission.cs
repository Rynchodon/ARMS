using System;
using System.Collections.Generic;
//using System.Linq;
//using System.Text;

using Sandbox.ModAPI;
using VRageMath;

namespace Rynchodon.AntennaRelay
{
	//public interface Transmission
	//{
	//	public bool isValid();
	//}

	public class LastSeen //: Transmission
	{
		private static readonly TimeSpan MaximumLifetime = new TimeSpan(1, 0, 0); // one hour

		public readonly IMyEntity Entity;
		public DateTime LastSeenAt { get; private set; }
		public Vector3D LastKnownPosition { get; private set; }
		public Vector3D LastKnownVelocity { get; private set; }
		//public Lazy<double> LastKnownSpeed;
		public bool EntityHasRadar { get; private set; }

		/// <summary>
		/// 
		/// </summary>
		/// <param name="entity">asteroid, grid, or player, never block</param>
		/// <param name="knownName"></param>
		public LastSeen(IMyEntity entity, bool EntityHasRadar = false)
		{
			(new Logger(null, "LastSeen")).log(Logger.severity.TRACE, ".ctor()", "entity = " + entity + ", entity name = " + entity.getBestName() + ", EntityHasRadar = " + EntityHasRadar);
			this.Entity = entity;
			this.LastSeenAt = DateTime.UtcNow;
			(new Logger(null, "LastSeen")).log(Logger.severity.TRACE, ".ctor()", "setting last known position...");
			this.LastKnownPosition = entity.WorldAABB.Center;
			(new Logger(null, "LastSeen")).log(Logger.severity.TRACE, ".ctor()", "setting last known velocity...");
			this.LastKnownVelocity = entity.Physics.LinearVelocity;
			//this.LastKnownSpeed = new Lazy<double>(() => { return LastKnownVelocity.Length(); });
			this.EntityHasRadar = EntityHasRadar;
		}

		///// <summary>
		///// if other.EntityHasRadar, so does this
		///// </summary>
		///// <param name="other"></param>
		///// <returns></returns>
		//public bool isNewerThan(LastSeen other)
		//{
		//	if (other.EntityHasRadar)
		//		EntityHasRadar = true;
		//	return LastSeenAt.CompareTo(other.LastSeenAt) > 0;
		//}

		/// <summary>
		/// 
		/// </summary>
		/// <param name="other"></param>
		/// <returns>true iff updated</returns>
		public bool updateWith(LastSeen other)
		{
			if (this == other)
				return false;
			if (this.LastSeenAt.CompareTo(other.LastSeenAt) > 0) // this is newer
				return false;
			this.LastSeenAt = other.LastSeenAt;
			this.LastKnownPosition = other.LastKnownPosition;
			this.LastKnownVelocity = other.LastKnownVelocity;
			//LastKnownSpeed = other.LastKnownSpeed;
			if (other.EntityHasRadar)
				this.EntityHasRadar = true;
			return true;
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

		private bool value_isValid = true;
		public bool isValid
		{
			get
			{
				if (value_isValid && (Entity == null || Entity.Closed || (DateTime.UtcNow - LastSeenAt).CompareTo(MaximumLifetime) > 0))
					value_isValid = false;
				return value_isValid;
			}
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

		private bool value_isValid = true;
		/// <summary>
		/// can only be set to false, once invalid always invalid
		/// </summary>
		public bool isValid
		{
			get
			{
				if (value_isValid && (DestCubeBlock == null || DestCubeBlock.Closed || destOwnerID != DestCubeBlock.OwnerId || (DateTime.UtcNow - created).CompareTo(MaximumLifetime) > 0))
					value_isValid = false;
				return value_isValid;
			}
			set
			{
				if (value == false)
					value_isValid = false;
			}
		}
	}
}
