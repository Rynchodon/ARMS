using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Rynchodon.AttachedGrid;
using Sandbox.Common.ObjectBuilders;
using Sandbox.ModAPI;
using VRage.ObjectBuilders;
using VRageMath;

namespace Rynchodon
{
	/// <summary>
	/// This class controls rotors for the purposes of pointing a block at a target. The turret does not have to be armed, it could be solar panels facing the sun, for example.
	/// </summary>
	/// <remarks>
	/// <para>To limit confusion, motor shall refer to the rotor and stator,  rotor shall only refer to the part that rotates.</para>
	/// </remarks>
	public class MotorTurret
	{
		private readonly MyObjectBuilderType[] types_Rotor = new MyObjectBuilderType[] { typeof(MyObjectBuilder_MotorRotor), typeof(MyObjectBuilder_MotorAdvancedRotor), typeof(MyObjectBuilder_MotorStator), typeof(MyObjectBuilder_MotorAdvancedStator) };

		private readonly IMyCubeBlock FaceBlock;

		/// <summary>Shall be the stator that is closer to the FaceBlock (by grids).</summary>
		private IMyMotorStator StatorA;
		/// <summary>Shall be the stator that is further from the FaceBlock (by grids).</summary>
		private IMyMotorStator StatorB;
		/// <summary>Difference between rotor and weapon directions.</summary>
		private float StatorA_AzimuthOffset, StatorB_AzimuthOffset;

		/// <remarks>When this changes, offsets need to be recreated.</remarks>
		private Base6Directions.Direction? FaceDirection = null;

		/// <summary>Direction of the target</summary>
		public RelativeDirection3F TargetDirection { get; set; }

		private Logger myLogger;

		public MotorTurret(IMyCubeBlock block)
		{
			this.FaceBlock = block;
			this.myLogger = new Logger("RotorTurret", block);
		}

		public void Update()
		{
			SetupStators();
			SetupOffsets();
		}

		private void SetupStators()
		{
			if (StatorOK())
				return;

			StatorA_AzimuthOffset = float.NaN;
			StatorB_AzimuthOffset = float.NaN;

			// get RotorA from FaceBlock's grid
			IMyCubeBlock RotorA;
			if (!GetStatorRotor(FaceBlock.CubeGrid, out StatorA, out RotorA))
			{
				StatorA = null;
				return;
			}

			// get RotorB from grid from either StatorA or RotorA, whichever is not Faceblock's grid
			IMyCubeGrid getBFrom;
			if (RotorA.CubeGrid == FaceBlock.CubeGrid)
				getBFrom = StatorA.CubeGrid as IMyCubeGrid;
			else
				getBFrom = RotorA.CubeGrid;

			IMyCubeBlock RotorB;
			if (!GetStatorRotor(getBFrom, out StatorB, out RotorB, StatorA, RotorA))
			{
				StatorB = null;
				return;
			}
		}

		private void SetupOffsets()
		{
			if (StatorA_AzimuthOffset.IsValid() && StatorB_AzimuthOffset.IsValid())
				return;

			if (TargetDirection == null)
			{
				myLogger.debugLog("FaceTarget is null", "SetupOffsets()");
				return;
			}

			Base6Directions.Direction face = FaceBlock.GetFaceDirection(TargetDirection.ToWorldNormalized());
			if (face == FaceDirection)
			{
				myLogger.debugLog("Face direction has not changed: "+face, "SetupOffsets()");
				return;
			}

			// create StatorA_AzimuthOffset
			IMyCubeBlock motorPart;
			if (StatorA.CubeGrid == FaceBlock.CubeGrid)
				motorPart = StatorA as IMyCubeBlock;
			else
				if (!StatorRotor.TryGetRotor(StatorA, out motorPart))
				{
					myLogger.debugLog("failed to get rotor for stator: " + StatorA.DisplayNameText, "SetupOffsets()", Logger.severity.WARNING);
					return;
				}

			StatorA_AzimuthOffset = GetOffset(motorPart, FaceDirection.Value);
			// maybe use GetAzimuthAndElevation() and reverse if a stator?




			// compare facedirection to rotor/stator forward
		}

		private bool StatorOK()
		{ return StatorA != null && !StatorA.Closed && StatorA.IsAttached && StatorB != null && !StatorB.Closed && StatorB.IsAttached; }

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
	}
}
