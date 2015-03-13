#define LOG_ENABLED //remove on build

using System;
using System.Collections.Generic;
//using System.Linq;
//using System.Text;

using Sandbox.Common;
using Sandbox.Common.Components;
using Sandbox.Common.ObjectBuilders;
using Sandbox.ModAPI;
using Ingame = Sandbox.ModAPI.Ingame;
using Sandbox.ModAPI.Interfaces;

namespace Rynchodon.Autopilot.Jumper
{
	[MyEntityComponentDescriptor(typeof(MyObjectBuilder_GravityGeneratorSphere))]
	public class JumpDrive : UpdateEnforcer
	{
		public bool enabled = false;
		private float value__radius = 0.1f;
		/// <summary>
		/// in metres
		/// </summary>
		public float radius
		{
			get { return value__radius; }
			set { value__radius = Math.Max(value, 0.1f); }
		}
		private float gravityMetresPer = 0;
		/// <summary>
		/// how many Gees [-1 : 1]
		/// </summary>
		public float gravityGees
		{
			get { return gravityMetresPer / oneGee; }
			set { gravityMetresPer = value * oneGee; }
		}

		public IMyCubeBlock myBlock { get; private set; }

		private static readonly float oneGee = 9.81f;

		private static Dictionary<IMyCubeBlock, JumpDrive> registry = new Dictionary<IMyCubeBlock, JumpDrive>();

		private static readonly string SubTypeNameLarge = "LargeBlockJumpDrive";
		private static readonly string SubTypeNameSmall = "SmallBlockJumpDrive";

		private static ITerminalProperty<float> property_radius = null;
		private static ITerminalProperty<float> property_gravity = null;

		private static readonly long timeout_after = 5;
		private long updates = 0;
		private long last_set_radius = 0;
		private long last_set_gravity = 0;
		private long last_toggled_onoff = 0;

		private Ingame.IMyGravityGeneratorSphere myGravBlock = null;

		private bool isJumpDrive = false;

		protected override void DelayedInit()
		{
			try
			{
				myBlock = Entity as IMyCubeBlock;
				myGravBlock = Entity as Ingame.IMyGravityGeneratorSphere;

				if (myBlock.BlockDefinition.SubtypeName != SubTypeNameLarge && myBlock.BlockDefinition.SubtypeName != SubTypeNameSmall)
				{
					// normal gravity
					log("SubtypeName=" + myBlock.BlockDefinition.SubtypeName + " did not match JumpDrive", "Init()", Logger.severity.TRACE);
					return;
				}

				if (property_radius == null)
				{
					property_radius = (ITerminalProperty<float>)myGravBlock.GetProperty("Radius");
					//log("radius attributes: " + property_radius.GetMininum(myGravBlock) + ", " + property_radius.GetDefaultValue(myGravBlock) + ", " + property_radius.GetMaximum(myGravBlock), "setup()", Logger.severity.DEBUG);
				}
				if (property_gravity == null)
				{
					property_gravity = (ITerminalProperty<float>)myGravBlock.GetProperty("Gravity");
					//log("gravity attributes: " + property_gravity.GetMininum(myGravBlock) + ", " + property_gravity.GetDefaultValue(myGravBlock) + ", " + property_gravity.GetMaximum(myGravBlock), "setup()", Logger.severity.DEBUG);
				}
				//if (action_onoff == null)
				//	action_onoff = myGravBlock.GetActionWithName("OnOff");

				registry.Add(myBlock, this);
				isJumpDrive = true;
				//Entity.NeedsUpdate |= MyEntityUpdateEnum.EACH_FRAME;
				EnforcedUpdate = MyEntityUpdateEnum.EACH_FRAME;

				//log("setup OK", "setup()", Logger.severity.INFO);
			}
			catch (Exception e)
			{
				alwaysLog("failed to setup" + e, "Init()", Logger.severity.FATAL);
				Close();
			}
		}

		public override void Close()
		{
			try
			{
				if (myBlock != null && registry.ContainsKey(myBlock))
					registry.Remove(myBlock);
			}
			catch (Exception e)
			{ alwaysLog("exception on removing from registry: " + e, "Close()", Logger.severity.WARNING); }
			myBlock = null;
		}

		public override void UpdateAfterSimulation()
		{
			if (!IsInitialized) return;
			if (Closed) return;
			try
			{
				updates++;
				if (updates < timeout_after) return;
				if (!isJumpDrive) { return; }

				//MyObjectBuilder_GravityGeneratorSphere builder = myBlock.GetObjectBuilderCubeBlock() as MyObjectBuilder_GravityGeneratorSphere;
				//log("builder=" + builder + ", producer=" + builder.ProducerEnabled + ", semiauto=" + builder.SemiautoEnabled);

				//log("enabled=" + myGravBlock.Enabled + ", IsWorking=" + myBlock.IsWorking + ":" + myGravBlock.IsWorking, "UpdateAfterSimulation()", Logger.severity.TRACE);

				// block's enabled switch must match JumpDrive's
				if (myGravBlock.Enabled != enabled)
				{
					if (last_toggled_onoff <= updates)
					{
						//action_onoff.Apply(myGravBlock);
						myGravBlock.RequestEnable(enabled);
						last_toggled_onoff = updates + timeout_after;
						log("toggled enabled to " + enabled, "UpdateAfterSimulation()", Logger.severity.DEBUG);
					}
				}
				else
					last_toggled_onoff = 0;

				// block's radius must match JumpDrive's
				if (property_radius.GetValue(myGravBlock) != radius)
				{
					//log("property_radius=" + property_radius.GetValue(myGravBlock)+", radius="+radius);
					if (last_set_radius <= updates )
					{
						property_radius.SetValue(myGravBlock, radius);
						last_set_radius = updates + timeout_after;
						log("set property radius to " + radius, "UpdateAfterSimulation()", Logger.severity.DEBUG);
					}
				}
				else
					last_set_radius = 0;

				// block's gravity must match JumpDrive's
				if (property_gravity.GetValue(myGravBlock) != gravityMetresPer)
				{
					//log("property_gravity=" + property_gravity.GetValue(myGravBlock) + ", gravity=" + gravity);
					if (last_set_gravity <= updates)
					{
						property_gravity.SetValue(myGravBlock, gravityMetresPer);
						last_set_gravity = updates + timeout_after;
						log("set property gravity to " + gravityMetresPer, "UpdateAfterSimulation()", Logger.severity.DEBUG);
					}
				}
				else
				{
					//if (last_set_gravity != 0)
					//	log("gravity matched: " + gravity, "UpdateAfterSimulation()", Logger.severity.DEBUG);
					last_set_gravity = 0;
				}
			}
			catch (Exception e)
			{
				log("Exception occured: " + e, "UpdateAfterSimulation()", Logger.severity.FATAL);
				Close();
			}
		}

		public static bool tryGetJumpDrive(out JumpDrive forBlock, IMyCubeBlock block)
		{ return registry.TryGetValue(block, out forBlock); }


		private Logger myLogger;
		[System.Diagnostics.Conditional("LOG_ENABLED")]
		private void log(string toLog, string method = null, Logger.severity level = Logger.severity.DEBUG)
		{ alwaysLog(toLog, method, level); }
		protected override void alwaysLog(string toLog, string method = null, Logger.severity level = Logger.severity.DEBUG)
		{
			if (myLogger == null)
			{
				if (myBlock == null)
					return; // I give up, forget about logging this
				myLogger = new Logger(myBlock.CubeGrid.DisplayName, "JumpDrive");
			}
			myLogger.log(level, method, toLog);
		}
	}
}
