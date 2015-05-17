// line removed by build.py 
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

using VRageMath;

using Rynchodon.Autopilot.Pathfinder;

namespace Rynchodon.Autopilot.Jumper
{
	public class GridJumper
	{
		public enum State : byte { OFF, SETUP, TRANSFER, READY, SUCCESS, FAILED }
		public State currentState = State.OFF;

		private static float setGravity_On = 1f; // set the same for large & small, radius will be different
		private static readonly int jumpEnergyDivisor = 10;
		private static readonly int maxChargersPerDrive = 6;

		// values from CubeBlocks.sbc
		/// <summary>
		/// in megawatts
		/// </summary>
		private float chargerOutput
		{
			get
			{
				if (myGrid.GridSizeEnum == MyCubeSize.Large)
					return 1000;
				else
					return 100;
			}
		}
		private static readonly float BasePowerInput = 10000;
		private static readonly float ConsumptionPower = 3;
		
		private static readonly bool SurvivalMode = MyAPIGateway.Session.SurvivalMode;

		private IMyCubeGrid myGrid;
		private Vector3D destination;
		private float destinationRadius;

		private List<JumpDrive> allJumpDrives;
		private List<JumpCharger> allJumpChargers;

		private Logger myLogger;
		[System.Diagnostics.Conditional("LOG_ENABLED")]
		private void log(string toLog, string method = null, Logger.severity level = Logger.severity.DEBUG)
		{ alwaysLog(toLog, method, level); }
		private void alwaysLog(string toLog, string method = null, Logger.severity level = Logger.severity.DEBUG)
		{
			if (myLogger == null) myLogger = new Logger(myGrid.DisplayName, "GridJumper");
			myLogger.log(level, method, toLog);
		}

		public GridJumper(GridDimensions gridDims, Vector3D destination)
		{
			this.myGrid = gridDims.myGrid;
			this.destination = destination;
			this.destinationRadius = gridDims.getLongestDim() * 2;

			allJumpDrives = new List<JumpDrive>();
			allJumpChargers = new List<JumpCharger>();

			List<IMySlimBlock> allBlocks = new List<IMySlimBlock>();
			myGrid.GetBlocks(allBlocks);//, block => block.FatBlock != null);
			foreach (IMySlimBlock block in allBlocks)
			{
				if (block.FatBlock == null)
					continue;
				MyRelationsBetweenPlayerAndBlock relationship = block.FatBlock.GetUserRelationToOwner(myGrid.BigOwners[0]);
				if (relationship == MyRelationsBetweenPlayerAndBlock.Enemies || relationship == MyRelationsBetweenPlayerAndBlock.Neutral)
				{
					log("cannot use block: "+block.FatBlock.DisplayNameText+", relationship = "+relationship, "..ctor()", Logger.severity.TRACE);
					continue;
				}

				//log("checking " + block.FatBlock.DisplayNameText + " for drive/charger", ".ctor()", Logger.severity.TRACE);
				if (block.FatBlock is Ingame.IMyBatteryBlock)
				{
					//log("... is a battery", ".ctor()", Logger.severity.TRACE);
					JumpCharger currentCharger;
					if (JumpCharger.tryGetJumpCharger(out currentCharger, block.FatBlock))
					{
						//log("JumpCharger is " + currentCharger, ".ctor()", Logger.severity.TRACE);
						allJumpChargers.Add(currentCharger);
						//log("added OK", ".ctor()", Logger.severity.TRACE);
					}
				}
				else if (block.FatBlock is Ingame.IMyGravityGeneratorSphere)
				{
					//log("... is a spherical generator", ".ctor()", Logger.severity.TRACE);
					JumpDrive currentDrive;
					if (JumpDrive.tryGetJumpDrive(out currentDrive, block.FatBlock))
						allJumpDrives.Add(currentDrive);
				}
			}
		}

		public bool canJump()
		{
			if (allJumpChargers.Count < 1 || allJumpDrives.Count < 1)
				return false;

			// energy available
			log("energy needed = "+getEnergyNeeded()+", energy available = "+getEnergyAvailable(), "canJump()", Logger.severity.DEBUG);
			if (getEnergyNeeded() > getEnergyAvailable())
				return false;

			// collision avoidance (speed is more important than getting close to the target)
			canJumpTo(destination);

			return true;
		}

		public bool trySetJump()
		{
			if (!canJump())
			{
				currentState = State.FAILED;
				return false;
			}

			// transfer energy
			// bring drives online
			foreach (JumpDrive drive in allJumpDrives)
			{
				drive.gravityGees = setGravity_On;
				drive.enabled = true;
				if (drive.myBlock.IsWorking)
					charger_IsWorkingChanged(drive.myBlock);
				drive.myBlock.IsWorkingChanged += drive_IsWorkingChanged;
			}

			// bring chargers into standby
			foreach (JumpCharger charger in allJumpChargers)
			{
				charger.producer = true;
				charger.standby = true;
				//if (charger.myBlock.IsWorking)
				//	charger_IsWorkingChanged(charger.myBlock);
				if (charger.myBlock.IsWorking)
					chargersOnline++;
				chargersStandby.Enqueue(charger);
				charger.myBlock.IsWorkingChanged += charger_IsWorkingChanged;
			}

			log("starting to charge for "+destination, "trySetJump()", Logger.severity.INFO);
			currentState = State.SETUP;
			timeLastCalculatePower = DateTime.UtcNow;
			return true;
		}

		public void stopCharging()
		{
			// take drives offline
			foreach (JumpDrive drive in allJumpDrives)
			{
				drive.myBlock.IsWorkingChanged -= drive_IsWorkingChanged;
				drive.gravityGees = 0;
				drive.radius = 0;
				drive.enabled = false;
			}

			// take chargers offline
			foreach (JumpCharger charger in allJumpChargers)
			{
				charger.myBlock.IsWorkingChanged -= charger_IsWorkingChanged;
				charger.producer = false;
				charger.standby = false;
				charger.myBatBlock.RequestEnable(true);
			}
			currentState = State.OFF;
			log("stopped charging", "stopCharging()", Logger.severity.DEBUG);
		}

		public void update()
		{
			switch (currentState)
			{
				case State.OFF:
					{
						log("state is OFF", "update()", Logger.severity.TRACE);
						return;
					}
				case State.SETUP:
					{
						// wait for chargers to enter standby
						if (chargersOnline == 0)
						{
							log("all chargers are now in standby", "update()", Logger.severity.DEBUG);
							energyTransfered = 0;
							currentState = State.TRANSFER;
						}
						return;
					}
				case State.TRANSFER:
					{
						calculatePower();
						log("energyTransfered=" + energyTransfered + ", energyNeeded=" + getEnergyNeeded(), "update()", Logger.severity.TRACE);
						if (energyTransfered >= getEnergyNeeded())
						{
							stopCharging();
							currentState = State.READY;
							log("finished charging successfully", "update()", Logger.severity.DEBUG);
						}
						else
							adjustNumberChargers();
						return;
					}
				case State.READY:
					{
						myGrid.SetPosition(destination);
						currentState = State.SUCCESS;
						return;
					}
			}
		}

		private float value__getEnergyNeeded = -1;
		/// <summary>
		/// mass * distance / jumpEnergyDivisor
		/// units: Wh
		/// </summary>
		/// <returns></returns>
		private float getEnergyNeeded()
		{
			if (value__getEnergyNeeded < 0)
			{
				float distance = (float)Math.Abs((myGrid.Physics.CenterOfMassWorld - destination).Length());
				value__getEnergyNeeded = myGrid.Physics.Mass * distance / jumpEnergyDivisor;
				log("distance=" + distance + ", mass=" + myGrid.Physics.Mass + ", EnergyNeeded=" + value__getEnergyNeeded, "getEnergyNeeded()", Logger.severity.TRACE);
			}
			return value__getEnergyNeeded;
		}

		private float value__getEnergyAvailable = -1;
		/// <summary>
		/// sum all the available energy in chargers
		/// units: Wh
		/// </summary>
		/// <returns></returns>
		private float getEnergyAvailable()
		{
			if (value__getEnergyAvailable < 0)
			{
				if (SurvivalMode)
				{
					value__getEnergyAvailable = 0;
					foreach (JumpCharger charger in allJumpChargers)
					{
						value__getEnergyAvailable += charger.getEnergyStored() * 1000000; // stored is in MWh
						//log("adding " + charger.getEnergyStored(), "getEnergyAvailable()", Logger.severity.TRACE);
					}
				}
				else
				{
					value__getEnergyAvailable = float.PositiveInfinity;
					log("creative mode, setting energy available to infinity", "getEnergyAvailable()");
				}
			}
			return value__getEnergyAvailable;
		}

		private int drivesOnline = 0;

		private void drive_IsWorkingChanged(IMyCubeBlock block)
		{
			calculatePower();
			if (block.IsWorking)
				drivesOnline++;
			else
				drivesOnline--;
			log("number of drives is now " + drivesOnline, "drive_IsWorkingChanged()", Logger.severity.TRACE);
			distributePower();
		}

		private int chargersOnline = 0;

		private void charger_IsWorkingChanged(IMyCubeBlock block)
		{
			calculatePower();
			if (block.IsWorking)
				chargersOnline++;
			else
				chargersOnline--;
			log("number of chargers is now " + chargersOnline + ", changed: " + block.DisplayNameText, "charger_IsWorkingChanged()", Logger.severity.TRACE);
			distributePower();
		}

		/// <summary>
		/// in megawatts
		/// </summary>
		/// <returns></returns>
		private float transferRate()
		{
			if (drivesOnline > 0)
				return Math.Min(chargersOnline, maxChargers()) * chargerOutput;
			else
				return 0;
		}

		private int maxChargers()
		{ return maxChargersPerDrive * drivesOnline; }

		private Queue<JumpCharger> chargersStandby = new Queue<JumpCharger>();
		private Queue<JumpCharger> chargersEnabled = new Queue<JumpCharger>();

		private int adjustExpectsChargersOnline = 0;

		private void adjustNumberChargers()
		{
			// if too many chargers are online, take one down
			if (chargersOnline > maxChargers())
			{
				JumpCharger aCharger = chargersEnabled.Dequeue();
				log("setting one online charger to standby: " + aCharger.myBlock.DisplayNameText, "charger_IsWorkingChanged()", Logger.severity.DEBUG);
				aCharger.standby = true;
				chargersStandby.Enqueue(aCharger);
				adjustExpectsChargersOnline--;
			}
			else
				// if not enough chargers are online, bring one up
				if (chargersOnline < maxChargers())
				{
					if (chargersStandby.Count == 0)
						return;

					JumpCharger aCharger = chargersStandby.Dequeue();
					log("bringing one charger online from standby: " + aCharger.myBlock.DisplayNameText, "charger_IsWorkingChanged()", Logger.severity.DEBUG);
					aCharger.standby = false;
					chargersEnabled.Enqueue(aCharger);
					adjustExpectsChargersOnline++;
				}
		}

		/// <summary>
		/// if this is not reset, it may be possible to resume a jump, though it is likely we will need to allow for ship movement
		/// </summary>
		private float energyTransfered = 0;
		private DateTime timeLastCalculatePower;
		/// <summary>
		/// time change in seconds * current rate
		/// </summary>
		private void calculatePower()
		{
			float rate = transferRate();
			TimeSpan elapsed = DateTime.UtcNow - timeLastCalculatePower;
			energyTransfered += (float)elapsed.TotalMilliseconds * rate; // * 1000 for MW => kW, / 1000 for millis => seconds
			timeLastCalculatePower = DateTime.UtcNow;

			log("calculated power. rate="+rate+"MW, elapsed="+elapsed.TotalMilliseconds+", energy="+energyTransfered/1000+"MWh", "calculatePower()", Logger.severity.TRACE);
		}

		/// <summary>
		/// set radius for drives so they will match output of chargers
		/// </summary>
		private void distributePower()
		{
			float rate = transferRate();
			float powerPerDrive, radius;
			if (rate > 0)
			{
				powerPerDrive = rate / drivesOnline;
				radius = (float)Math.Pow(powerPerDrive * 1000000 / BasePowerInput / setGravity_On, 1 / ConsumptionPower); // TODO cache Math.pow result
			}
			else
			{
				powerPerDrive = 0;
				radius = 0;
			}

			log("calculated radius. rate=" + rate + "MW, powerPerDrive=" + powerPerDrive + "MW, radius=" + radius, "distributePower()", Logger.severity.TRACE);

			foreach (JumpDrive drive in allJumpDrives)
				if (drive.myBlock.IsWorking)
					drive.radius = radius;
		}

		public float estimatedTimeToReadyMillis()
		{ return (getEnergyNeeded() - energyTransfered) / transferRate(); }

		/// <summary>
		/// for this, BoundingBox collision checking shall be used
		/// </summary>
		/// <param name="point"></param>
		private void canJumpTo(Vector3D point)
		{
			//var boxTransformMatrix = myGrid.WorldAABB.Transform(myGrid.WorldMatrix);
			//log("");
			//log("boxTransformMatrix = "+boxTransformMatrix);
			//log("box = " + myGrid.WorldAABB);
			//log("matrix = " + myGrid.WorldMatrix);
			//log("");
			//var boxTranslateDestination = myGrid.WorldAABB.Translate(destination);
			//log("");
			//log("boxTranslateDestination = " + boxTranslateDestination);
			//log("box = " + myGrid.WorldAABB);
			//log("destination = " + destination);
			//log("");
			//var displacement = destination - myGrid.WorldAABB.Center;
			//var boxTranslateDisplacement = myGrid.WorldAABB.Translate(destination);
			//log("");
			//log("boxTranslateDisplacement = " + boxTranslateDisplacement);
			//log("box = " + myGrid.WorldAABB);
			//log("displacement = " + displacement);
			//log("");
			//var rotatedMatrix = myGrid.WorldMatrix.SetDirectionVector(Base6Directions.Direction.Forward, Vector3D.Normalize( displacement));
			//var boxTransformRotatedMatrix = myGrid.WorldAABB
		}
	}
}
