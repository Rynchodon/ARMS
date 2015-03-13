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
		public readonly Vector3D LastKnownTrajectory;

		public LastSeen(IMyEntity entity)
		{
			Entity = entity;
			LastSeenAt = DateTime.UtcNow;
			LastKnownPosition = entity.GetPosition();
			LastKnownTrajectory = entity.Physics.LinearVelocity;
		}

		public bool isNewerThan(LastSeen other)
		{ return LastSeenAt.CompareTo(other.LastSeenAt) > 0; }

		public Vector3D predictPosition()
		{ return LastKnownPosition + LastKnownTrajectory * (DateTime.UtcNow - LastSeenAt).TotalSeconds; }

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

		//// cannot have multiple LastSeen in a HashSet with the same Entity
		///// <summary>
		///// Entity.GetHashCode()
		///// </summary>
		///// <returns></returns>
		//public override int GetHashCode()
		//{ return Entity.GetHashCode(); }
	}

	public class Message //: Transmission
	{
		public readonly string Content, SourceGridName, SourceBlockName;
		public bool received = false;
		public IMyCubeBlock DestCubeBlock, SourceCubeBlock;

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
		}
	}
}
