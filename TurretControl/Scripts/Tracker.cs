// skip file on build

using System;
using System.Collections.Generic;
//using System.Linq;
//using System.Text;

using Sandbox.Common;
using Sandbox.Common.Components;
using Sandbox.Common.ObjectBuilders;
using Sandbox.ModAPI;

namespace Rynchodon.Autopilot.Turret
{
	public class TrackerMissile
	{
		public static HashSet<IMyEntity> registry = new HashSet<IMyEntity>();

		//protected override void DelayedInit() { registry.Add(Entity); }

		//public override void Close() { registry.Remove(Entity); }
	}

	public class TrackerMeteor
	{
		public static HashSet<IMyEntity> registry = new HashSet<IMyEntity>();

		static TrackerMeteor()
		{
			HashSet<IMyEntity> allEntities = new HashSet<IMyEntity>();
			MyAPIGateway.Entities.GetEntities(allEntities);
			foreach (IMyEntity entity in allEntities)
				Entities_OnEntityAdd(entity);
			MyAPIGateway.Entities.OnEntityAdd += Entities_OnEntityAdd;
			MyAPIGateway.Entities.OnEntityRemove += Entities_OnEntityRemove;
		}

		static void Entities_OnEntityAdd(IMyEntity obj)
		{
		}

		static void Entities_OnEntityRemove(IMyEntity obj)
		{
		}

		//protected override void DelayedInit() { registry.Add(Entity); }

		//public override void Close() { registry.Remove(Entity); }
	}

	public class TrackerPlayer
	{
		public static HashSet<IMyEntity> registry = new HashSet<IMyEntity>();

		//protected override void DelayedInit() { registry.Add(Entity); }

		//public override void Close() { registry.Remove(Entity); }
	}
}
