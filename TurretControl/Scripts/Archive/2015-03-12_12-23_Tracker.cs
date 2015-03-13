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
	[MyEntityComponentDescriptor(typeof(MyObjectBuilder_SmallMissileLauncher))]
	public class TrackerMissile : UpdateEnforcer
	{
		public static HashSet<IMyEntity> registry = new HashSet<IMyEntity>();

		protected override void DelayedInit() { registry.Add(Entity); }

		public override void Close() { registry.Remove(Entity); }
	}

	[MyEntityComponentDescriptor(typeof(MyObjectBuilder_Meteor))]
	public class TrackerMeteor : UpdateEnforcer
	{
		public static HashSet<IMyEntity> registry = new HashSet<IMyEntity>();

		protected override void DelayedInit() { registry.Add(Entity); }

		public override void Close() { registry.Remove(Entity); }
	}

	[MyEntityComponentDescriptor(typeof(MyObjectBuilder_Player))]
	public class TrackerPlayer : UpdateEnforcer
	{
		public static HashSet<IMyEntity> registry = new HashSet<IMyEntity>();

		protected override void DelayedInit() { registry.Add(Entity); }

		public override void Close() { registry.Remove(Entity); }
	}
}
