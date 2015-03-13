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
		public readonly DateTime LastSeenAt;
		public readonly Vector3D LastKnownPosition;
		public readonly Vector3D LastKnownVelocity;
		public Lazy<double> LastKnownSpeed;

		public LastSeen(IMyEntity entity)
		{
			Entity = entity;
			LastSeenAt = DateTime.UtcNow;
			LastKnownPosition = entity.GetPosition();
			LastKnownVelocity = entity.Physics.LinearVelocity;
			LastKnownSpeed = new Lazy<double>(() => { return LastKnownVelocity.Length(); });
		}

		public bool isNewerThan(LastSeen other)
		{ return LastSeenAt.CompareTo(other.LastSeenAt) > 0; }

		public Vector3D predictPosition()
		{ return LastKnownPosition + LastKnownVelocity * (DateTime.UtcNow - LastSeenAt).TotalSeconds; }

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
