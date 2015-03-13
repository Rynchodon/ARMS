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
	[MyEntityComponentDescriptor(typeof(MyObjectBuilder_BatteryBlock))]
	public class JumpCharger : UpdateEnforcer
	{
		public bool producer = false;
		public bool standby = false;

		public IMyCubeBlock myBlock { get; private set; }
		public Ingame.IMyBatteryBlock myBatBlock  { get; private set; }

		private static Dictionary<IMyCubeBlock, JumpCharger> registry = new Dictionary<IMyCubeBlock, JumpCharger>();

		private static readonly string SubTypeNameLarge = "LargeBlockJumpDriveCharger";
		private static readonly string SubTypeNameSmall = "SmallBlockJumpDriveCharger";

		private static ITerminalAction action_recharge = null;

		private static readonly long timeout_after = 5;
		private long updates = 0;
		private long toggled_recharge = 0;
		private long toggled_off_semi = 0;
		private long toggled_on_prod = 0;

		//private MyObjectBuilder_EntityBase builder_base = null;

		private bool isJumpCharger = false;

		protected override void DelayedInit()
		{
			try
			{
				myBlock = Entity as IMyCubeBlock;
				myBatBlock = Entity as Ingame.IMyBatteryBlock;

				if (myBlock.BlockDefinition.SubtypeName != SubTypeNameLarge && myBlock.BlockDefinition.SubtypeName != SubTypeNameSmall)
				{
					// normal battery
					log("SubtypeName=" + myBlock.BlockDefinition.SubtypeName + " did not match JumpDriveCharger", "Init()", Logger.severity.TRACE);
					return;
				}

				if (action_recharge == null)
					action_recharge = myBatBlock.GetActionWithName("Recharge");

				registry.Add(myBlock, this);
				isJumpCharger = true;
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
				if (!isJumpCharger) { return; }

				//log("builder=" + builder + ", producer=" + builder.ProducerEnabled + ", semiauto=" + builder.SemiautoEnabled);

				if (standby)
				{
					if (myBatBlock.Enabled)
					{
						myBatBlock.RequestEnable(false);
						toggled_on_prod = 0;
						toggled_recharge = 0;
						toggled_off_semi = 0;
					}
					return;
				}

				// block must be enabled while producer is enabled
				if (!myBatBlock.Enabled)
				{
					if (!producer)
					{
						//log("disabled and recharging", "UpdateAfterSimulation()", Logger.severity.TRACE);
						toggled_on_prod = 0;
						toggled_recharge = 0;
						toggled_off_semi = 0;
						return; // block is off, producer is false, do not need to change anything
					}

					// if producer is on, block must be on
					if (toggled_on_prod <= updates)
					{
						//action_on.Apply(myBatBlock);
						myBatBlock.RequestEnable(true);
						toggled_on_prod = updates + timeout_after;
						log("turned block on", "UpdateAfterSimulation()", Logger.severity.DEBUG);
					}
				}
				else
					toggled_on_prod = 0;

				MyObjectBuilder_BatteryBlock builder = myBlock.GetObjectBuilderCubeBlock() as MyObjectBuilder_BatteryBlock;

				// block's producer switch must match JumpCharger's
				if (builder.ProducerEnabled != producer)
				{
					if (toggled_recharge <= updates)
					{
						action_recharge.Apply(myBatBlock);
						toggled_recharge = updates + timeout_after;
						log("toggled producer to " + producer, "UpdateAfterSimulation()", Logger.severity.DEBUG);
					}
				}
				else
					toggled_recharge = 0;

				//log("Enabled=" + myBatBlock.Enabled + ", SemiautoEnabled=" + builder.SemiautoEnabled + ", producer=" + producer, "UpdateAfterSimulation()", Logger.severity.TRACE);
				// cannot set semiauto, so if it is enabled while block should be charging, turn block off
				if (myBatBlock.Enabled && builder.SemiautoEnabled && !producer) // only care if semiauto is enabled while charging
				{
					if (toggled_off_semi <= updates)
					{
						//action_off.Apply(myBatBlock);
						myBatBlock.RequestEnable(false);
						toggled_off_semi = updates + timeout_after;
						log("turned block off", "UpdateAfterSimulation()", Logger.severity.DEBUG);
					}
					//else
					//	log("waiting for block to switch off", "UpdateAfterSimulation()", Logger.severity.TRACE);
				}
				else
				{
					//if (toggled_off_semi)
					//	log("block switched off", "UpdateAfterSimulation()", Logger.severity.TRACE);
					toggled_off_semi = 0;
				}

			}
			catch (Exception e)
			{
				log("Exception occured: " + e, "UpdateAfterSimulation()", Logger.severity.FATAL);
				Close();
			}
		}

		//public override MyObjectBuilder_EntityBase GetObjectBuilder(bool copy = false)
		//{ return builder_base; }

		public static bool tryGetJumpCharger(out JumpCharger forBlock, IMyCubeBlock block)
		{ return registry.TryGetValue(block, out forBlock); }

		public float getEnergyStored()
		{
			MyObjectBuilder_BatteryBlock builder = myBlock.GetObjectBuilderCubeBlock() as MyObjectBuilder_BatteryBlock;
			return builder.CurrentStoredPower;
		}


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
				myLogger = new Logger(myBlock.CubeGrid.DisplayName, "JumpCharger");
			}
			myLogger.log(level, method, toLog);
		}

	}
}
