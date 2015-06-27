using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Rynchodon.AttachedGrid;
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
	/// 
	/// Just setting up a test that will work for a single configuration, then consider expanding to others.
	public class MotorTurret
	{
		public delegate void StatorChangeHandler(IMyMotorStator statorEl, IMyMotorStator statorAz);
		public StatorChangeHandler OnStatorChange;

		private const float RotationSpeedMultiplier =- MathHelper.Pi;
		private static readonly MyObjectBuilderType[] types_Rotor = new MyObjectBuilderType[] { typeof(MyObjectBuilder_MotorRotor), typeof(MyObjectBuilder_MotorAdvancedRotor), typeof(MyObjectBuilder_MotorStator), typeof(MyObjectBuilder_MotorAdvancedStator) };

		private readonly IMyCubeBlock FaceBlock;

		/// <summary>Shall be the stator that is closer to the FaceBlock (by grids).</summary>
		public IMyMotorStator StatorEl { get; private set; }
		/// <summary>Shall be the stator that is further from the FaceBlock (by grids).</summary>
		public IMyMotorStator StatorAz { get; private set; }
		/// <summary>Difference between rotor and weapon directions.</summary>
		private float StatorEl_Offset, StatorAz_Offset = -MathHelper.PiOver2;

		/// <remarks>When this changes, offsets need to be recreated.</remarks>
		private Base6Directions.Direction? FaceDirection = null;

		private Logger myLogger;

		public MotorTurret(IMyCubeBlock block)
		{
			this.FaceBlock = block;
			this.myLogger = new Logger("MotorTurret", block);
		}

		//public void Update()
		//{
		//	SetupStators();
		//	//SetupOffsets();
		//}

		/// <summary>
		/// How close to a particular direction the motor-turret can get, 0 means perfect.
		/// </summary>
		public float HowClose(Vector3 targetDirection)
		{
			throw new Exception();
		}

		/// <summary>
		/// Rotate to face FaceBlock towards a particular direction.
		/// </summary>
		public void FaceTowards(RelativeDirection3F direction)
		{
			if (!SetupStators())
				return;

			Vector3 blockDir = direction.ToBlockNormalized(StatorAz as IMyCubeBlock);
			float targetEl, targetAz;
			Vector3.GetAzimuthAndElevation(blockDir, out targetAz, out targetEl);
			// elevation and azimuth will change as weapon moves

			// elevation and azimuth are between -Pi and +Pi, stator angles are all over the place
			float elevationChange = MathHelper.WrapAngle(StatorEl.Angle - targetEl + StatorEl_Offset);
			float azimuthChange = MathHelper.WrapAngle(StatorAz.Angle - targetAz + StatorAz_Offset);

			//float elevationChange = MathHelper.WrapAngle(targetEl - MathHelper.WrapAngle(StatorEl.Angle) + StatorEl_Offset);
			//float azimuthChange = MathHelper.WrapAngle(targetAz - MathHelper.WrapAngle(StatorAz.Angle) + StatorAz_Offset);

			//myLogger.debugLog((targetEl - MathHelper.WrapAngle(StatorEl.Angle) + StatorEl_Offset) + " wrapped to " + elevationChange, "FaceTowards()");
			//myLogger.debugLog((targetAz - MathHelper.WrapAngle(StatorAz.Angle) + StatorAz_Offset) + " wrapped to " + azimuthChange, "FaceTowards()");

			SetVelocity(StatorEl, elevationChange);
			SetVelocity(StatorAz, azimuthChange);

			myLogger.debugLog("target azimuth = " + targetAz + ", elevation = " + targetEl + "; current azimuth = " + StatorAz.Angle + ", elevation = " + StatorEl.Angle + ", change azimuth = " + azimuthChange + ", elevation = " + elevationChange, "FaceTowards()");
		}

		//private float WrapAngle(float angle)
		//{
		//	while (angle > MathHelper.Pi)
		//		angle -= MathHelper.TwoPi;
		//	while (angle < -MathHelper.Pi)
		//		angle += MathHelper.TwoPi;
		//	return angle;
		//}

		private bool StatorOK()
		{ return StatorEl != null && !StatorEl.Closed && StatorEl.IsAttached && StatorAz != null && !StatorAz.Closed && StatorAz.IsAttached; }

		private bool SetupStators()
		{
			if (StatorOK())
				return true;

			//StatorEl_Offset = float.NaN;
			//StatorAz_Offset = float.NaN;

			// get StatorEl from FaceBlock's grid
			IMyCubeBlock RotorEl;
			if (!GetStatorRotor(FaceBlock.CubeGrid, out StatorEl, out RotorEl))
			{
				myLogger.debugLog("Failed to get StatorEl", "SetupStators()");
				StatorEl = null;
				OnStatorChange(StatorEl, StatorAz);
				return false;
			}

			// get StatorAz from grid from either StatorEl or RotorEl, whichever is not on Faceblock's grid
			IMyCubeGrid getBFrom;
			if (RotorEl.CubeGrid == FaceBlock.CubeGrid)
				getBFrom = StatorEl.CubeGrid as IMyCubeGrid;
			else
				getBFrom = RotorEl.CubeGrid;

			IMyCubeBlock RotorAz;
			if (!GetStatorRotor(getBFrom, out StatorAz, out RotorAz, StatorEl, RotorEl))
			{
				myLogger.debugLog("Failed to get StatorAz", "SetupStators()");
				StatorAz = null;
				OnStatorChange(StatorEl, StatorAz);
				return false;
			}

			myLogger.debugLog("Successfully got stators. Elevation = " + StatorEl.DisplayNameText + ", Azimuth = " + StatorAz.DisplayNameText, "SetupStators()");
				OnStatorChange(StatorEl, StatorAz);
			return true;
		}

		//private void SetupOffsets()
		//{
		//	if (StatorEl_Offset.IsValid() && StatorAz_Offset.IsValid())
		//		return;

		//	if (TargetDirection == null)
		//	{
		//		myLogger.debugLog("FaceTarget is null", "SetupOffsets()");
		//		return;
		//	}

		//	Base6Directions.Direction face = FaceBlock.GetFaceDirection(TargetDirection.ToWorldNormalized()); // really a motor-turret should consider how close a face can get to target
		//	if (face == FaceDirection)
		//	{
		//		myLogger.debugLog("Face direction has not changed: "+face, "SetupOffsets()");
		//		return;
		//	}

		//	// create StatorA_AzimuthOffset
		//	IMyCubeBlock motorPart;
		//	if (StatorEl.CubeGrid == FaceBlock.CubeGrid)
		//		motorPart = StatorEl as IMyCubeBlock;
		//	else
		//		if (!StatorRotor.TryGetRotor(StatorEl, out motorPart))
		//		{
		//			myLogger.debugLog("failed to get rotor for stator: " + StatorEl.DisplayNameText, "SetupOffsets()", Logger.severity.WARNING);
		//			return;
		//		}

		//	StatorEl_Offset = GetOffset(motorPart, FaceDirection.Value);
		//	// maybe use GetAzimuthAndElevation() and reverse if a stator?




		//	// compare facedirection to rotor/stator forward
		//}

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
					Stator = motorPart as IMyMotorStator;
					if (Stator != null)
					{
						if (Stator == IgnoreStator)
							continue;
						if (StatorRotor.TryGetRotor(Stator, out Rotor))
							return true;
					}
					else
					{
						Rotor = motorPart;
						if (Rotor == IgnoreRotor)
							continue;
						if (StatorRotor.TryGetStator(Rotor, out Stator))
							return true;
					}
				}
			}

			Stator = null;
			Rotor = null;
			return false;
		}

		private float GetOffset(IMyCubeBlock motorPart, Base6Directions.Direction faceDirection)
		{
			if (faceDirection == motorPart.Orientation.Forward)
				return 0f;
			else if (faceDirection == Base6Directions.GetFlippedDirection(motorPart.Orientation.Forward))
				return MathHelper.Pi;
			else
			{
				Base6Directions.Direction rotorLeft, rotorRight;
				// if part is a stator, need to swap rotorLeft and rotorRight 
				if (motorPart is IMyMotorStator)
				{
					rotorLeft = Base6Directions.GetFlippedDirection(motorPart.Orientation.Left);
					rotorRight = motorPart.Orientation.Left;
				}
				else
				{
					rotorLeft = motorPart.Orientation.Left;
					rotorRight = Base6Directions.GetFlippedDirection(motorPart.Orientation.Left);
				}

				if (faceDirection == rotorLeft)
					return MathHelper.PiOver2;
				else if (faceDirection == rotorRight)
					return -MathHelper.PiOver2;
				else // none of those directions
				{
					myLogger.debugLog("Useless rotor part:" + motorPart.DisplayNameText, "GetOffset()");
					return 0f;
				}
			}
		}

		private void SetVelocity(IMyMotorStator Stator, float angle)
		{
			var prop = Stator.GetProperty("Velocity").AsFloat();
			//float setTo = Math.Min(Math.Max(prop.GetMininum(Stator), angle * RotationSpeedMultiplier), prop.GetMaximum(Stator));
			prop.SetValue(Stator, angle * RotationSpeedMultiplier);
		}
	}
}
