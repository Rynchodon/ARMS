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
		public static bool looseContains(this string bigString, string smallString)
		{
			VRage.Exceptions.ThrowIf<ArgumentNullException>(bigString == null, "bigString");
			VRage.Exceptions.ThrowIf<ArgumentNullException>(smallString == null, "smallString");

			string compare1 = bigString.RemoveWhitespace().ToLower();
			string compare2 = smallString.RemoveWhitespace().ToLower();
			return compare1.Contains(compare2);
		}

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
		{ return RelativeVector3F.createFromGrid(grid.LocalAABB.Center, grid).getWorldAbsolute(); }

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

		/// <summary>
		/// From http://stackoverflow.com/a/20857897
		/// </summary>
		public static string RemoveWhitespace(this string input)
		{
			int j = 0, inputlen = input.Length;
			char[] newarr = new char[inputlen];

			for (int i = 0; i < inputlen; ++i)
			{
				char tmp = input[i];

				if (!char.IsWhiteSpace(tmp))
				{
					newarr[j] = tmp;
					++j;
				}
			}

			return new String(newarr, 0, j);
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

		/// <summary>
		/// aply an operation to each of x, y, z
		/// </summary>
		public static void ApplyOperation(this Vector3D vector, Func<double, double> operation, out Vector3D result)
		{
			double x = operation(vector.X);
			double y = operation(vector.Y);
			double z = operation(vector.Z);
			result = new Vector3D(x, y, z);
		}

		/// <summary>
		/// aply an operation to each of x, y, z
		/// </summary>
		public static void ApplyOperation(this Vector3D vector, Func<double, double> operation, out Vector3I result)
		{
			int x = (int)operation(vector.X);
			int y = (int)operation(vector.Y);
			int z = (int)operation(vector.Z);
			result = new Vector3I(x, y, z);
		}

		public static void ForEach(this Vector3I min, Vector3I max, Action<Vector3I> toInvoke)
		{
			for (int x = min.X; x < max.X; x++)
				for (int y = min.Y; y < max.Y; y++)
					for (int z = min.Z; z < max.Z; z++)
						toInvoke(new Vector3I(x, y, z));
		}
	}
}
