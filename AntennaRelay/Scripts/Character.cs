// skip file on build

using System;
using System.Collections.Generic;
using System.Linq;
using Sandbox.ModAPI;
using VRage.ModAPI;
using VRageMath;

namespace Rynchodon.AntennaRelay
{
	public class Character
	{
		public static readonly List<Character> AllCharacters = new List<Character>();

		private const int MaxGPS = 5;
		private const string defaultDescription = "entry added by Autopilot";
		private static readonly TimeSpan expireAfter = new TimeSpan(0, 1, 0);

		public readonly IMyCharacter myCharacter;
		public readonly IMyEntity myEntity;
		public readonly IMyIdentity myIdentity;
		public readonly IMyPlayer myPlayer;

		private readonly Logger myLogger;

		private Dictionary<long, LastSeen> myLastSeen = new Dictionary<long, LastSeen>();
		private IMyGps[] myGPS = new IMyGps[5];
		private bool IsDead = false;

		public Character(IMyCharacter character)
		{
			myCharacter = character;
			myEntity = character as IMyEntity;
			myIdentity = character.GetIdentity_Safe();
			myPlayer = character.GetPlayer_Safe();

			myLogger = new Logger(null, "Character");

			if (myCharacter == null)
				throw new NullReferenceException("myCharacter");
			if (myEntity == null)
				throw new NullReferenceException("myEntity");
			if (myIdentity == null)
				throw new NullReferenceException("myIdentity");
			if (myPlayer == null)
				throw new NullReferenceException("myPlayer");

			myLogger = new Logger(myPlayer.DisplayName, "Character");

			AllCharacters.Add(this);
			myLogger.debugLog("initialized", "Character()");
		}

		public void Update100()
		{
			if (!IsDead)
			{
				if (myIdentity.IsDead)
				{
					IsDead = true;
					AllCharacters.Remove(this);
				}
				else
					UpdateGPS();
			}
		}

		public void receive(LastSeen seen)
		{
			LastSeen toUpdate;
			if (myLastSeen.TryGetValue(seen.Entity.EntityId, out toUpdate))
			{
				if (seen.update(ref toUpdate))
				{
					myLastSeen[toUpdate.Entity.EntityId] = toUpdate;
					myLogger.debugLog("updated last seen for " + toUpdate.Entity.getBestName(), "receive()");
				}
			}
			else
			{
				myLastSeen.Add(seen.Entity.EntityId, seen);
				myLogger.debugLog("new last seen for " + seen.Entity.getBestName(), "receive()");
			}
		}

		private void UpdateGPS()
		{
			if (myLastSeen.Count == 0)
			{
				myLogger.debugLog("myLastSeen is empty", "UpdateGPS()");
				return;
			}

			Vector3D myPosition = (myCharacter as IMyEntity).GetPosition();
			SortedDictionary<float, LastSeen> distanceSeen = new SortedDictionary<float, LastSeen>();
			foreach (LastSeen seen in myLastSeen.Values)
			{
				if (distanceSeen.Count == MaxGPS)
					distanceSeen.Remove(distanceSeen.Keys.Last());
				float distance = Vector3.DistanceSquared(myPosition, seen.predictPosition());
				distanceSeen.Add(distance, seen);
			}

			for (int index = 0; index < MaxGPS && index < distanceSeen.Count; index++)
			{
				LastSeen seen = distanceSeen.Values.ElementAt(index);

				ExtensionsRelations.Relations relate;
				IMyCubeGrid asGrid = seen.Entity as IMyCubeGrid;
				if (asGrid != null)
					relate = myPlayer.getRelationsTo(asGrid, ExtensionsRelations.Relations.Enemy);
				else
				{
					IMyCharacter asChar = seen.Entity as IMyCharacter;
					if (asChar != null)
						relate = myPlayer.getRelationsTo(asChar.GetPlayer_Safe().PlayerID);
					else
						relate = ExtensionsRelations.Relations.None;
				}

				if (!relate.HasFlagFast(ExtensionsRelations.Relations.Enemy))
					continue;

				Vector3D coords = seen.predictPosition();
				TimeSpan discardAt = MyAPIGateway.Session.ElapsedPlayTime + expireAfter;
				string name = "Detected " + relate + " #" + index;

				if (myGPS[index] == null)
				{
					myLogger.debugLog("creating new GPS for " + seen.Entity.getBestName(), "UpdateGPS()");
					myGPS[index] = MyAPIGateway.Session.GPS.Create(name, defaultDescription, coords, false, true);
					myGPS[index].DiscardAt = discardAt;
					MyAPIGateway.Session.GPS.AddGps(myIdentity.IdentityId, myGPS[index]);
				}
				else
				{
					myLogger.debugLog("updating GPS for " + seen.Entity.getBestName(), "UpdateGPS()");
					myGPS[index].Coords = coords;
					myGPS[index].DiscardAt = discardAt;
					myGPS[index].Name = name;
					MyAPIGateway.Session.GPS.ModifyGps(myIdentity.IdentityId, myGPS[index]);
				}
			}
		}
	}
}
