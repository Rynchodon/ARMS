using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Rynchodon.Attached;
using Sandbox.Common.ObjectBuilders;
using Sandbox.ModAPI;
using VRage.ObjectBuilders;
using VRageMath;
using Sandbox.ModAPI.Interfaces;

namespace Rynchodon
{
	/// <summary>
	/// This class controls rotors for the purposes of pointing a block at a target. The turret does not have to be armed, it could be solar panels facing the sun, for example.
	/// </summary>
	/// <remarks>
	/// <para>To limit confusion, motor shall refer to the rotor and stator,  rotor shall only refer to the part that rotates.</para>
	/// <para>Will only change motor velocity, other parameters need to be set by the player.</para>
	/// </remarks>
	public class MotorTurret
	{
		public delegate void StatorChangeHandler(IMyMotorStator statorEl, IMyMotorStator statorAz);

		private const float def_RotationSpeedMultiplier = 100;
		private static readonly MyObjectBuilderType[] types_Rotor = new MyObjectBuilderType[] { typeof(MyObjectBuilder_MotorRotor), typeof(MyObjectBuilder_MotorAdvancedRotor), typeof(MyObjectBuilder_MotorStator), typeof(MyObjectBuilder_MotorAdvancedStator) };

		private readonly IMyCubeBlock FaceBlock;
		private readonly Logger myLogger;
		private readonly StatorChangeHandler OnStatorChange;

		/// <summary>Shall be the stator that is closer to the FaceBlock (by grids).</summary>
		public IMyMotorStator StatorEl { get; private set; }
		/// <summary>Shall be the stator that is further from the FaceBlock (by grids).</summary>
		public IMyMotorStator StatorAz { get; private set; }

		private float previousSpeed_StatorEl;
		private float previousSpeed_StatorAz;

		public MotorTurret(IMyCubeBlock block) : this(block, delegate { }) { }

		private float my_RotationSpeedMulitplier = def_RotationSpeedMultiplier;

		private float mySpeedLimit = 30f;

		public MotorTurret(IMyCubeBlock block, StatorChangeHandler handler)
		{
			this.FaceBlock = block;
			this.myLogger = new Logger("MotorTurret", block);
			this.OnStatorChange = handler;
			this.SetupStators();
		}

		public float RotationSpeedMultiplier
		{
			get { return my_RotationSpeedMulitplier; }
			set { my_RotationSpeedMulitplier = Math.Abs(value); }
		}

		public float SpeedLimt
		{
			get { return mySpeedLimit; }
			set { mySpeedLimit = Math.Abs(value); }
		}

		/// <summary>
		/// Rotate to face FaceBlock towards a particular direction.
		/// </summary>
		public void FaceTowards(RelativeDirection3F direction)
		{
			//myLogger.debugLog("entered FaceTowards()", "FaceTowards()");

			if (!SetupStators())
				return;

			// get the direction FaceBlock is currently facing
			Base6Directions.Direction closestDirection = FaceBlock.GetFaceDirection(direction.ToWorld());
			//myLogger.debugLog("closest direction: " + closestDirection, "FaceTowards()");
			RelativeDirection3F currentDir = RelativeDirection3F.FromWorld(FaceBlock.CubeGrid, FaceBlock.WorldMatrix.GetDirectionVector(closestDirection));
			// get the elevation and azimuth from currentDir
			float currentFaceEl, currentFaceAz;
			Vector3.GetAzimuthAndElevation(currentDir.ToBlockNormalized(StatorAz as IMyCubeBlock), out currentFaceAz, out currentFaceEl);

			Vector3 targetDir_block = direction.ToBlockNormalized(StatorAz as IMyCubeBlock);
			float targetEl, targetAz;
			Vector3.GetAzimuthAndElevation(targetDir_block, out targetAz, out targetEl);
			// target elevation and azimuth will change as weapon moves

			// elevation and azimuth are between -Pi and +Pi, stator angles are all over the place
			float elevationChange = MathHelper.WrapAngle(currentFaceEl - targetEl);
			float azimuthChange = MathHelper.WrapAngle(currentFaceAz - targetAz);

			SetVelocity(StatorEl, elevationChange);
			SetVelocity(StatorAz, azimuthChange);
		}

		public void Stop()
		{
			if (!StatorOK())
				return;

			SetVelocity(StatorEl, 0);
			SetVelocity(StatorAz, 0);
		}

		private bool StatorOK()
		{ return StatorEl != null && !StatorEl.Closed && StatorEl.IsAttached && StatorAz != null && !StatorAz.Closed && StatorAz.IsAttached; }

		private bool SetupStators()
		{
			if (StatorOK())
			{
				//myLogger.debugLog("Stators are already set up", "SetupStators()");
				return true;
			}

			// get StatorEl from FaceBlock's grid
			IMyMotorStator tempStator;
			IMyCubeBlock RotorEl;
			if (!GetStatorRotor(FaceBlock.CubeGrid, out tempStator, out RotorEl))
			{
				myLogger.debugLog("Failed to get StatorEl", "SetupStators()");
				StatorEl = null;
				OnStatorChange(StatorEl, StatorAz);
				return false;
			}
			StatorEl = tempStator;

			// get StatorAz from grid from either StatorEl or RotorEl, whichever is not on Faceblock's grid
			IMyCubeGrid getBFrom;
			if (RotorEl.CubeGrid == FaceBlock.CubeGrid)
				getBFrom = StatorEl.CubeGrid as IMyCubeGrid;
			else
				getBFrom = RotorEl.CubeGrid;

			IMyCubeBlock RotorAz;
			if (!GetStatorRotor(getBFrom, out tempStator, out RotorAz, StatorEl, RotorEl))
			{
				myLogger.debugLog("Failed to get StatorAz", "SetupStators()");
				StatorAz = null;
				OnStatorChange(StatorEl, StatorAz);
				return false;
			}
			StatorAz = tempStator;

			myLogger.debugLog("Successfully got stators. Elevation = " + StatorEl.DisplayNameText + ", Azimuth = " + StatorAz.DisplayNameText, "SetupStators()");
			OnStatorChange(StatorEl, StatorAz);
			Stop();
			return true;
		}

		private bool GetStatorRotor(IMyCubeGrid grid, out IMyMotorStator Stator, out IMyCubeBlock Rotor, IMyMotorStator IgnoreStator = null, IMyCubeBlock IgnoreRotor = null)
		{
			CubeGridCache cache = CubeGridCache.GetFor(grid);

			foreach (MyObjectBuilderType type in types_Rotor)
			{
				var blocksOfType = cache.GetBlocksOfType(type);
				if (blocksOfType == null || blocksOfType.Count == 0)
					continue;

				foreach (IMyCubeBlock motorPart in blocksOfType)
				{
					if (!FaceBlock.canControlBlock(motorPart))
						continue;

					Stator = motorPart as IMyMotorStator;
					if (Stator != null)
					{
						if (Stator == IgnoreStator)
							continue;
						if (StatorRotor.TryGetRotor(Stator, out Rotor) && FaceBlock.canControlBlock(Stator as IMyCubeBlock))
							return true;
					}
					else
					{
						Rotor = motorPart;
						if (Rotor == IgnoreRotor)
							continue;
						if (StatorRotor.TryGetStator(Rotor, out Stator) && FaceBlock.canControlBlock(Stator as IMyCubeBlock))
							return true;
					}
				}
			}

			Stator = null;
			Rotor = null;
			return false;
		}

		private void SetVelocity(IMyMotorStator Stator, float angle)
		{
			float speed = angle * RotationSpeedMultiplier;

			if (!speed.IsValid())
			{
				myLogger.debugLog("invalid speed: " + speed, "SetVelocity()");
				speed = 0;
			}
			else
				speed = MathHelper.Clamp(speed, -mySpeedLimit, mySpeedLimit);

			float prevSpeed;
			if (Stator == StatorEl)
				prevSpeed = previousSpeed_StatorEl;
			else
				prevSpeed = previousSpeed_StatorAz;

			if (Math.Abs(speed - prevSpeed) < 0.1)
			{
				//myLogger.debugLog(Stator.DisplayNameText + ", no change in speed: " + speed, "SetVelocity()");
				return;
			}

			myLogger.debugLog(Stator.DisplayNameText + ", speed changed to " + speed, "SetVelocity()");
			var prop = Stator.GetProperty("Velocity").AsFloat();

			if (Stator == StatorEl)
				previousSpeed_StatorEl = speed;
			else
				previousSpeed_StatorAz = speed;

			prop.SetValue(Stator, speed);
		}
	}
}
