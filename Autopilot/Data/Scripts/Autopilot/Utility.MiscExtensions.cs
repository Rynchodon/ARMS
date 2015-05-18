﻿using System;
//using System.Collections.Generic;
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
			string compare1 = bigString.Replace(" ", string.Empty).ToUpper();
			string compare2 = smallString.Replace(" ", string.Empty).ToUpper();
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
	}
}
