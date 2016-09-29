using System;
using System.Collections.Generic;
using Rynchodon.Attached;
using Rynchodon.Utility.Vectors;
using Sandbox.Common.ObjectBuilders;
using Sandbox.ModAPI;
using Sandbox.ModAPI.Interfaces;
using VRage.Game.ModAPI;
using VRage.ObjectBuilders;
using VRageMath;

namespace Rynchodon
{
	/// <summary>
	/// This class controls rotors for the purposes of pointing a block at a target. The turret does not have to be armed, it could be solar panels facing the sun, for example.
	/// </summary>
	/// <remarks>
	/// <para>To limit confusion, motor shall refer to the rotor and stator,  rotor shall only refer to the part that rotates.</para>
	/// <para>Will only change motor velocity, other parameters need to be set by the player.</para>
	/// </remarks>
	public class MotorTurret : IDisposable
	{
		private class StaticVariables
		{
			public readonly MyObjectBuilderType[] types_Rotor = new MyObjectBuilderType[] { typeof(MyObjectBuilder_MotorRotor), typeof(MyObjectBuilder_MotorAdvancedRotor), typeof(MyObjectBuilder_MotorStator), typeof(MyObjectBuilder_MotorAdvancedStator) };
			public readonly HashSet<IMyMotorStator> claimedStators = new HashSet<IMyMotorStator>();
			public ITerminalProperty<float> statorVelocity;
		}

		private static StaticVariables Static = new StaticVariables();
		public delegate void StatorChangeHandler(IMyMotorStator statorEl, IMyMotorStator statorAz);
		private const float def_RotationSpeedMultiplier = 100;

		static MotorTurret()
		{
			MyAPIGateway.Entities.OnCloseAll += Entities_OnCloseAll;
		}

		private static void Entities_OnCloseAll()
		{
			MyAPIGateway.Entities.OnCloseAll -= Entities_OnCloseAll;
			Static = null;
		}

		private static void GetElevationAndAzimuth(Vector3 vector, out float elevation, out float azimuth)
		{
			elevation = (float)Math.Asin(vector.Y);

			// azimuth is arbitrary when elevation is +/- 1
			if (Math.Abs(vector.Y) > 0.9999f)
				azimuth = float.NaN;
			else
				azimuth = (float)Math.Atan2(vector.X, vector.Z);
		}

		/// <summary>
		/// Determines if a stator can rotate a specific amount.
		/// </summary>
		/// <returns>True iff the stator can rotate the specified amount.</returns>
		private static bool CanRotate(IMyMotorStator stator, float amount)
		{
			// NaN is treated as zero
			if (float.IsNaN(amount) || stator.UpperLimit - stator.LowerLimit > MathHelper.TwoPi)
				return true;

			return amount > 0f ? amount <= stator.UpperLimit - stator.Angle : amount >= stator.LowerLimit - stator.Angle;
		}

		private readonly IMyCubeBlock FaceBlock;
		private readonly Logger myLogger;
		private readonly StatorChangeHandler OnStatorChange;

		private float my_RotationSpeedMulitplier = def_RotationSpeedMultiplier;
		private float mySpeedLimit = 30f;

		private bool m_claimedElevation, m_claimedAzimuth;
		private IMyMotorStator value_statorEl, value_statorAz;

		/// <summary>Shall be the stator that is closer to the FaceBlock (by grids).</summary>
		public IMyMotorStator StatorEl
		{
			get { return value_statorEl; }
			private set
			{
				if (value_statorEl == value)
					return;
				if (m_claimedElevation)
				{
					m_claimedElevation = false;
					if (Static.claimedStators.Remove(value_statorEl))
						myLogger.debugLog("Released claim on elevation stator: " + value_statorEl.nameWithId(), Logger.severity.DEBUG);
					else
						myLogger.alwaysLog("Failed to remove claim on elevation stator: " + value_statorEl.nameWithId(), Logger.severity.ERROR);
				}
				value_statorEl = value;
			}
		}

		/// <summary>Shall be the stator that is further from the FaceBlock (by grids).</summary>
		public IMyMotorStator StatorAz
		{
			get { return value_statorAz; }
			private set
			{
				if (value_statorAz == value)
					return;
				if (m_claimedAzimuth)
				{
					m_claimedAzimuth = false;
					if (Static.claimedStators.Remove(value_statorAz))
						myLogger.debugLog("Released claim on azimuth stator: " + value_statorAz.nameWithId(), Logger.severity.DEBUG);
					else
						myLogger.alwaysLog("Failed to remove claim on azimuth stator: " + value_statorAz.nameWithId(), Logger.severity.ERROR);
				}
				value_statorAz = value;
			}
		}

		public MotorTurret(IMyCubeBlock block, StatorChangeHandler handler = null)
		{
			this.FaceBlock = block;
			this.myLogger = new Logger(block);
			this.OnStatorChange = handler;
			this.SetupStators();
		}

		~MotorTurret()
		{
			Dispose();
		}

		public void Dispose()
		{
			if (Static != null)
			{
				Stop();
				StatorEl = null;
				StatorAz = null;
			}
		}

		public float RotationSpeedMultiplier
		{
			get { return my_RotationSpeedMulitplier; }
			set { my_RotationSpeedMulitplier = Math.Abs(value); }
		}

		public float SpeedLimit
		{
			get { return mySpeedLimit; }
			set { mySpeedLimit = Math.Abs(value); }
		}

		/// <summary>
		/// Try to face in the target direction.
		/// </summary>
		public void FaceTowards(DirectionWorld target)
		{
			myLogger.debugLog("Not server!", Logger.severity.FATAL, condition: !MyAPIGateway.Multiplayer.IsServer);

			if (!SetupStators())
				return;

			float bestDeltaElevation;
			float bestDeltaAzimuth;
			CalcFaceTowards(target, out bestDeltaElevation, out bestDeltaAzimuth);

			if (m_claimedElevation)
				SetVelocity(StatorEl, bestDeltaElevation);
			if (m_claimedAzimuth)
				SetVelocity(StatorAz, bestDeltaAzimuth);
		}

		public bool CanFaceTowards(DirectionWorld target, float CanRotateMulti = 1f)
		{
			if (!StatorOK())
				return false;

			float bestDeltaElevation;
			float bestDeltaAzimuth;
			return CalcFaceTowards(target, out bestDeltaElevation, out bestDeltaAzimuth, CanRotateMulti);
		}

		private bool CalcFaceTowards(DirectionWorld target, out float bestDeltaElevation, out float bestDeltaAzimuth, float CanRotateMulti = 1f)
		{
			DirectionBlock blockTarget = target.ToBlock((IMyCubeBlock)StatorAz);
			float targetElevation, targetAzimuth;
			GetElevationAndAzimuth(blockTarget.vector, out targetElevation, out targetAzimuth);
			PositionBlock faceBlockPosition = new PositionWorld() { vector = FaceBlock.GetPosition() }.ToBlock((IMyCubeBlock)StatorAz);
			bool alternate = blockTarget.vector.Z * faceBlockPosition.vector.X - blockTarget.vector.X * faceBlockPosition.vector.Z > 0f; // Y component of cross product > 0

			bestDeltaElevation = 0f;
			bestDeltaAzimuth = 0f;

			bool first = true;
			bool canFace = false;
			foreach (Base6Directions.Direction direction in FaceBlock.FaceDirections(target))
			{
				DirectionBlock blockCurrent = new DirectionWorld() { vector = FaceBlock.WorldMatrix.GetDirectionVector(direction) }.ToBlock((IMyCubeBlock)StatorAz);

				float currentElevation, currentAzimuth;
				GetElevationAndAzimuth(blockCurrent.vector, out currentElevation, out currentAzimuth);

				// essentially we have done a pseudo calculation of target/current elevation/azimuth but they are either correct or "opposite" of the actual values
				// we can determine which with a simple calculation

				float deltaElevation, deltaAzimuth;
				CalcFaceTowards(currentElevation, currentAzimuth, targetElevation, targetAzimuth, out deltaElevation, out deltaAzimuth, alternate);
				canFace = CanRotate(StatorEl, deltaElevation * CanRotateMulti) && CanRotate(StatorAz, deltaAzimuth * CanRotateMulti);

				if (first)
				{
					first = false;
					bestDeltaElevation = deltaElevation;
					bestDeltaAzimuth = deltaAzimuth;
					if (canFace)
						break;
				}
				else if (canFace)
				{
					bestDeltaElevation = deltaElevation;
					bestDeltaAzimuth = deltaAzimuth;
					break;
				}

				CalcFaceTowards(currentElevation, currentAzimuth, targetElevation, targetAzimuth, out deltaElevation, out deltaAzimuth, !alternate, false);
				canFace = CanRotate(StatorEl, deltaElevation * CanRotateMulti) && CanRotate(StatorAz, deltaAzimuth * CanRotateMulti);

				if (canFace)
				{
					bestDeltaElevation = deltaElevation;
					bestDeltaAzimuth = deltaAzimuth;
					break;
				}
			}

			return canFace;
		}

		private void CalcFaceTowards(float currentElevation, float currentAzimuth, float targetElevation, float targetAzimuth, out float deltaElevation, out float deltaAzimuth, bool alternate = false, bool allowFlip = true)
		{
			if (alternate)
			{
				targetElevation = MathHelper.Pi - targetElevation;
				targetAzimuth += MathHelper.Pi;
			}
			deltaAzimuth = MathHelper.WrapAngle(currentAzimuth - targetAzimuth);
			if (allowFlip && Math.Abs(deltaAzimuth) > MathHelper.PiOver2)
			{
				currentElevation = MathHelper.Pi - currentElevation;
				deltaAzimuth = MathHelper.WrapAngle(deltaAzimuth + MathHelper.Pi);
			}
			deltaElevation = MathHelper.WrapAngle(currentElevation - targetElevation);
		}

		public void Stop()
		{
			myLogger.debugLog("Not server!", Logger.severity.FATAL, condition: !MyAPIGateway.Multiplayer.IsServer);

			if (!StatorOK())
				return;

			if (m_claimedElevation)
				SetVelocity(StatorEl, 0);
			if (m_claimedAzimuth)
				SetVelocity(StatorAz, 0);
		}

		private bool StatorOK()
		{ return StatorEl != null && !StatorEl.Closed && StatorEl.IsAttached && StatorAz != null && !StatorAz.Closed && StatorAz.IsAttached; }

		private bool ClaimStators()
		{
			if (!m_claimedElevation)
			{
				m_claimedElevation = Static.claimedStators.Add(StatorEl);
				myLogger.debugLog("claimed elevation stator: " + StatorEl.nameWithId(), Logger.severity.DEBUG, condition: m_claimedElevation);
			}
			if (!m_claimedAzimuth)
			{
				m_claimedAzimuth = Static.claimedStators.Add(StatorAz);
				myLogger.debugLog("claimed azimuth stator: " + StatorAz.nameWithId(), Logger.severity.DEBUG, condition: m_claimedAzimuth);
			}
			return m_claimedElevation || m_claimedAzimuth;
		}

		private bool SetupStators()
		{
			if (StatorOK())
			{
				//myLogger.debugLog("Stators are already set up", "SetupStators()");
				return ClaimStators();
			}

			// get StatorEl from FaceBlock's grid
			IMyMotorStator tempStator;
			IMyCubeBlock RotorEl;
			if (!GetStatorRotor(FaceBlock.CubeGrid, out tempStator, out RotorEl))
			{
				myLogger.debugLog("Failed to get StatorEl");
				StatorEl = null;
				if (OnStatorChange != null)
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
				myLogger.debugLog("Failed to get StatorAz");
				StatorAz = null;
				if (OnStatorChange != null)
					OnStatorChange(StatorEl, StatorAz);
				return false;
			}
			StatorAz = tempStator;

			myLogger.debugLog("Successfully got stators. Elevation = " + StatorEl.DisplayNameText + ", Azimuth = " + StatorAz.DisplayNameText);
			if (OnStatorChange != null)
				OnStatorChange(StatorEl, StatorAz);
			Stop();
			return ClaimStators();
		}

		private bool GetStatorRotor(IMyCubeGrid grid, out IMyMotorStator Stator, out IMyCubeBlock Rotor, IMyMotorStator IgnoreStator = null, IMyCubeBlock IgnoreRotor = null)
		{
			CubeGridCache cache = CubeGridCache.GetFor(grid);

			foreach (MyObjectBuilderType type in Static.types_Rotor)
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
			myLogger.debugLog("Not server!", Logger.severity.FATAL, condition: !MyAPIGateway.Multiplayer.IsServer);

			// keep in mind, azimuth is undefined if elevation is straight up or straight down
			float speed = angle.IsValid() ? MathHelper.Clamp(angle * RotationSpeedMultiplier, -mySpeedLimit, mySpeedLimit) : 0f;

			if (Static.statorVelocity == null)
				Static.statorVelocity = Stator.GetProperty("Velocity") as ITerminalProperty<float>;
			float currentSpeed = Static.statorVelocity.GetValue(Stator);
			if ((speed == 0f && currentSpeed != 0f) || Math.Abs(speed - currentSpeed) > 0.1f)
				Static.statorVelocity.SetValue(Stator, speed);
		}
	}
}
