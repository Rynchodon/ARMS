using System;
//using System.Collections.Generic;
//using System.Linq;
//using System.Text;

using Sandbox.Common.ObjectBuilders;
using Sandbox.ModAPI;

namespace Rynchodon
{
	public static class Extensions
	{
		public static bool looseContains(this string bigString, string smallString)
		{
			string compare1 = bigString.Replace(" ", string.Empty).ToUpper();
			string compare2 = smallString.Replace(" ", string.Empty).ToUpper();
			return compare1.Contains(compare2);
		}

		public static string getBestName(this IMyEntity entity)
		{
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
						name = "unknown";
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
	}
}
