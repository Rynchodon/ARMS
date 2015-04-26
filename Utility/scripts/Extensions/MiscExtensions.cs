using System;
using System.Collections.Generic;
//using System.Linq;
//using System.Text;

using Sandbox.Common.Components;
using Sandbox.Common.ObjectBuilders;
using Sandbox.ModAPI;
using VRageMath;

namespace Rynchodon
{
	public static class MiscExtensions
	{
		public static string getBestName(this IMyEntity entity)
		{
			IMyCubeBlock asBlock = entity as IMyCubeBlock;
			if (asBlock != null)
				return asBlock.DisplayNameText;

			if (entity == null)
				return null;
			string name = entity.DisplayName;
			if (string.IsNullOrEmpty(name))
			{
				name = entity.Name;
				if (string.IsNullOrEmpty(name))
				{
					name = entity.GetFriendlyName();
					if (string.IsNullOrEmpty(name))
					{
						name = entity.ToString();
					}
				}
			}
			//MyObjectBuilder_EntityBase builder = entity.GetObjectBuilder();
			//if (builder != null)
			//	name += "." + builder.TypeId;
			//else
			//	name += "." + entity.EntityId;
			return name;
		}

		public static Vector3 GetLinearAcceleration(this MyPhysicsComponentBase Physics)
		{
			if (Physics.CanUpdateAccelerations && Physics.LinearAcceleration == Vector3.Zero)
				Physics.UpdateAccelerations();
			return Physics.LinearAcceleration;
		}

		public static Vector3D GetCentre(this IMyCubeGrid grid)
		{ return RelativeVector3F.createFromLocal(grid.LocalAABB.Center, grid).getWorldAbsolute(); }

		public static Vector3D GetCentre(this IMyEntity entity)
		{
			IMyCubeGrid asGrid = entity as IMyCubeGrid;
			if (asGrid != null)
				return GetCentre(asGrid);

			if (entity.WorldAABB != null)
				return entity.WorldAABB.Center;

			return entity.GetPosition();
		}

		public static double secondsSince(this DateTime previous)
		{ return (DateTime.UtcNow - previous).TotalSeconds; }

		public static bool moreThanSecondsAgo(this DateTime previous, double seconds)
		{ return (DateTime.UtcNow - previous).TotalSeconds > seconds; }

		public static bool Is_ID_NPC(this long playerID)
		{
			if (playerID == 0)
				return false;

			List<IMyPlayer> players = new List<IMyPlayer>();
			MyAPIGateway.Players.GetPlayers(players);
			foreach (IMyPlayer play in players)
				if (play.PlayerID == playerID)
					return false;

			return true;
		}

		public static bool IsClient(this IMyMultiplayer multiplayer)
		{
			if (!multiplayer.MultiplayerActive)
				return false;
			return !multiplayer.IsServer;
		}

		public static void throwIfNull_argument(this object argument, string name)
		{ VRage.Exceptions.ThrowIf<ArgumentNullException>(argument == null, name + " == null"); }

		public static void throwIfNull_variable(this object variable, string name)
		{ VRage.Exceptions.ThrowIf<NullReferenceException>(variable == null, name + " == null"); }

		public static string ToPrettySeconds(this VRage.Library.Utils.MyTimeSpan timeSpan)
		{ return PrettySI.makePretty(timeSpan.Seconds) + 's'; }

		/// <summary>
		/// Minimum distance squared between a line and a point.
		/// </summary>
		/// <remarks>
		/// based on http://stackoverflow.com/a/1501725
		/// </remarks>
		/// <returns>The minimum distance between the line and the point</returns>
		public static float DistanceSquared(this Line line, Vector3 point)
		{
			if (line.From == line.To)
				return Vector3.DistanceSquared(line.From, point);

			Vector3 line_disp = line.To - line.From;
			float line_distSq = line_disp.LengthSquared();

			float fraction = Vector3.Dot(point - line.From, line_disp) / line_distSq; // projection as a fraction of line_disp

			if (fraction < 0) // extends past From
				return Vector3.DistanceSquared(point, line.From);
			else if (fraction > 1) // extends past To
				return Vector3.DistanceSquared(point, line.To);

			Vector3 closestPoint = line.From + fraction * line_disp; // closest point on the line
			return Vector3.DistanceSquared(point, closestPoint);
		}
	}
}
